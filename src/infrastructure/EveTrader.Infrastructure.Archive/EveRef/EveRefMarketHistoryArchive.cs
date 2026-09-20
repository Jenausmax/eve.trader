using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using EveTrader.Domain.History;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Дневная история из архива EVE Ref: <c>market-history/&lt;YYYY&gt;/market-history-YYYY-MM-DD.csv.bz2</c>,
/// сутки одним глобальным файлом на все регионы.
///
/// Вежливость здесь не украшение: параллелизм ограничен, каждый запрос условный по
/// времени изменения, и неизменившийся файл не скачивается вовсе — источник отвечает
/// 304 без тела. Сама механика живёт в <see cref="ArchiveDownload" /> и
/// <see cref="ArchiveRetry" />, общих со снимками стакана.
/// </summary>
public sealed partial class EveRefMarketHistoryArchive(
    HttpClient client,
    EveRefOptions options,
    ILogger<EveRefMarketHistoryArchive> logger) : IMarketHistorySource
{
    /// <summary>Имя файла и есть контракт источника — разметки страницы листинга нет.</summary>
    [GeneratedRegex(@"market-history-(\d{4}-\d{2}-\d{2})\.csv\.bz2")]
    private static partial Regex ArchiveFile { get; }

    public string Name => "everef-archive";

    public async IAsyncEnumerable<MarketHistoryObservation> ObserveAsync(
        MarketHistoryScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IReadOnlyList<DateOnly> days = await AvailableDaysAsync(scope.Within, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("EVE Ref публикует {Days} суток в запрошенном интервале", days.Count);

        var channel = Channel.CreateBounded<MarketHistoryObservation>(
            new BoundedChannelOptions(options.MaxParallelDownloads * 2) { SingleReader = true });

        Task producer = ArchivePump.RunAsync(
            channel.Writer,
            days,
            options.MaxParallelDownloads,
            (day, token) => FetchDayAsync(day, scope, token),
            cancellationToken);

        await foreach (MarketHistoryObservation observation in
            channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return observation;
        }

        await producer.ConfigureAwait(false);
    }

    /// <summary>Сутки, которые источник публикует: перечень берётся из листинга года.</summary>
    public async Task<IReadOnlyList<DateOnly>> AvailableDaysAsync(
        TimeRange within,
        CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(within.From.UtcDateTime);
        var to = DateOnly.FromDateTime(within.To.UtcDateTime);

        var days = new List<DateOnly>();

        for (var year = from.Year; year <= to.Year; year++)
        {
            using HttpResponseMessage response = await client
                .GetAsync(new Uri($"{year}/", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            _ = response.EnsureSuccessStatusCode();

            var listing = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            foreach (Match match in ArchiveFile.Matches(listing))
            {
                var day = DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

                if (day >= from && day <= to)
                {
                    days.Add(day);
                }
            }
        }

        return [.. days.Distinct().Order()];
    }

    /// <summary>
    /// Скачивает и разбирает одни сутки, повторяя при обрыве. Возвращает
    /// <see langword="null" />, если источник ответил «не изменилось», суток не
    /// публикует либо попытки исчерпаны.
    /// </summary>
    public Task<MarketHistoryObservation?> FetchDayAsync(
        DateOnly day,
        MarketHistoryScope scope,
        CancellationToken cancellationToken) =>
        ArchiveRetry.RunAsync(
            $"Сутки {day:yyyy-MM-dd}",
            options.MaxAttempts,
            options.RetryDelay,
            logger,
            token => FetchAttemptAsync(day, scope, token),
            cancellationToken);

    /// <summary>
    /// Одна попытка без повтора. Публична намеренно: повтор и одна попытка — разные
    /// вещи, и проверять их порознь честнее, чем через счётчик обрывов.
    /// </summary>
    public async Task<MarketHistoryObservation?> FetchAttemptAsync(
        DateOnly day,
        MarketHistoryScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        DateTimeOffset? loadedAt = scope.LoadedAt(day);

        (var body, DateTimeOffset lastModified, HttpStatusCode status) = await ArchiveDownload
            .FetchAsync(client, $"{day.Year}/market-history-{day:yyyy-MM-dd}.csv.bz2", loadedAt, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            if (status is HttpStatusCode.NotFound)
            {
                logger.LogWarning("EVE Ref не публикует сутки {Day}", day);
            }

            return null;
        }

        using StreamReader reader = ArchiveDownload.Read(body);

        var rows = new List<MarketHistoryRow>();
        var regions = new HashSet<RegionId>();
        HashSet<RegionId>? wantedRegions = scope.Regions.Count == 0 ? null : [.. scope.Regions];
        HashSet<int>? wantedTypes = scope.Types.Count == 0 ? null : [.. scope.Types];

        await foreach (MarketHistoryRow row in
            MarketHistoryCsv.ReadAsync(reader, lastModified, cancellationToken).ConfigureAwait(false))
        {
            if (wantedRegions is not null && !wantedRegions.Contains(row.Region))
            {
                continue;
            }

            if (wantedTypes is not null && !wantedTypes.Contains(row.TypeId))
            {
                continue;
            }

            // Строка, которую источник узнал раньше нашей прошлой загрузки, у нас уже есть.
            // Так досинхронизация дописывает только новое, не удваивая прежнее.
            if (loadedAt is { } known && row.KnownAt <= known)
            {
                continue;
            }

            rows.Add(row);
            _ = regions.Add(row.Region);
        }

        if (rows.Count == 0)
        {
            return null;
        }

        // Время изменения файла входит в идентификатор наблюдения: правка задним числом
        // даёт новое наблюдение и новую версию строк, а неизменившийся файл — то же имя
        // и короткий путь через «уже подтверждено».
        var observation = ObservationId.From(
            string.Create(CultureInfo.InvariantCulture, $"everef-history-{day:yyyy-MM-dd}-{lastModified:yyyyMMddTHHmmssZ}"));

        return new MarketHistoryObservation(day, observation, rows, TimeSpan.FromDays(1), [.. regions.Order()]);
    }
}
