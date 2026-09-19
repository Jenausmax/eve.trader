using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using EveTrader.Domain.History;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Дневная история из архива EVE Ref: <c>market-history/&lt;YYYY&gt;/market-history-YYYY-MM-DD.csv.bz2</c>,
/// сутки одним глобальным файлом на все регионы.
///
/// Вежливость здесь не украшение: параллелизм ограничен, каждый запрос условный по
/// времени изменения, и неизменившийся файл не скачивается вовсе — источник отвечает
/// 304 без тела.
/// </summary>
public sealed partial class EveRefMarketHistoryArchive(
    HttpClient client,
    EveRefOptions options,
    ILogger<EveRefMarketHistoryArchive> logger) : IMarketHistorySource
{
    /// <summary>Имя файла и есть контракт источника — разметка страницы листинга нет.</summary>
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

        Task producer = ProduceAsync(channel.Writer, days, scope, cancellationToken);

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
    /// Скачивает и разбирает одни сутки. Возвращает <see langword="null" />, если
    /// источник ответил «не изменилось» — тела в таком ответе нет, и трафик не потрачен.
    /// </summary>
    public async Task<MarketHistoryObservation?> FetchDayAsync(
        DateOnly day,
        MarketHistoryScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var path = $"{day.Year}/market-history-{day:yyyy-MM-dd}.csv.bz2";

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (scope.KnownSince is { } since)
        {
            request.Headers.IfModifiedSince = since;
        }

        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotModified)
        {
            return null;
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogWarning("EVE Ref не публикует сутки {Day}", day);

            return null;
        }

        _ = response.EnsureSuccessStatusCode();

        DateTimeOffset lastModified = response.Content.Headers.LastModified ?? DateTimeOffset.UnixEpoch;

        await using Stream compressed = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var decompressed = new BZip2InputStream(compressed);
        using var reader = new StreamReader(decompressed);

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
            if (scope.KnownSince is { } known && row.KnownAt <= known)
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

    private async Task ProduceAsync(
        ChannelWriter<MarketHistoryObservation> writer,
        IReadOnlyList<DateOnly> days,
        MarketHistoryScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                days,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MaxParallelDownloads,
                    CancellationToken = cancellationToken,
                },
                async (day, token) =>
                {
                    MarketHistoryObservation? observation = await FetchDayAsync(day, scope, token).ConfigureAwait(false);

                    if (observation is not null)
                    {
                        await writer.WriteAsync(observation, token).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

            _ = writer.TryComplete();
        }
        catch (Exception exception)
        {
            _ = writer.TryComplete(exception);
        }
    }
}
