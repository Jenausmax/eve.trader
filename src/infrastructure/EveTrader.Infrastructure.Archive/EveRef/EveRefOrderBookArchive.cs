using System.Globalization;
using System.Net;
using System.Text.Json;
using EveTrader.Application.Book;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Снимки стакана из архива EVE Ref:
/// <c>market-orders/history/&lt;YYYY&gt;/&lt;YYYY-MM-DD&gt;/market-orders-&lt;YYYY-MM-DD_HH-mm-ss&gt;.v3.csv.bz2</c>,
/// один файл на момент, все регионы разом.
///
/// Источник снимает рынок дважды в час — на 15-й и на 45-й минуте, — и это его шаг, а не
/// наша настройка опроса. Шаг объявляется потребителю и доезжает до записи покрытия:
/// тридцать минут недосэмплируют перестановки цены вшестеро, потому что игра допускает
/// правку ордера раз в пять минут, и молчать об этом нельзя.
///
/// Вежливость и повтор — общие с дневной историей: <see cref="ArchiveDownload" /> и
/// <see cref="ArchiveRetry" />.
/// </summary>
public sealed class EveRefOrderBookArchive(
    HttpClient client,
    EveRefOptions options,
    ILogger<EveRefOrderBookArchive> logger) : IOrderBookSource
{
    /// <summary>Формат отметки времени в имени файла снимка.</summary>
    public const string StampFormat = "yyyy-MM-dd_HH-mm-ss";

    public const string FilePrefix = "market-orders-";

    public const string FileSuffix = ".v3.csv.bz2";

    public string Name => "everef-orders";

    public TimeSpan Step => options.OrdersStep;

    /// <summary>
    /// Имя снимка. Из момента, а не из времени изменения файла: снимок стакана источник
    /// публикует однажды и задним числом не правит — в отличие от суток дневной истории,
    /// которые досинхронизируются неделями. Поэтому имя устойчиво между прогонами, и по
    /// нему узнаётся, что снимок уже свёрнут.
    /// </summary>
    public string KeyFor(DateTimeOffset snapshotAt) =>
        string.Create(CultureInfo.InvariantCulture, $"everef-orders-{snapshotAt:yyyyMMddTHHmmssZ}");

    public async Task<IReadOnlyList<DateTimeOffset>> AvailableSnapshotsAsync(
        TimeRange within,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DateOnly> days = await AvailableDaysAsync(within, cancellationToken).ConfigureAwait(false);

        var snapshots = new List<DateTimeOffset>();

        // Суточные листинги тоже тянутся с пределом вежливости: за окно в два года их
        // семь с лишним сотен, и лупить ими в источник без ограничения нельзя.
        await Parallel.ForEachAsync(
            days,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxParallelDownloads,
                CancellationToken = cancellationToken,
            },
            async (day, token) =>
            {
                IReadOnlyList<DateTimeOffset> published = await SnapshotsOfDayAsync(day, token).ConfigureAwait(false);

                lock (snapshots)
                {
                    snapshots.AddRange(published.Where(within.Contains));
                }
            }).ConfigureAwait(false);

        logger.LogInformation(
            "EVE Ref публикует {Snapshots} снимков стакана за {Days} суток в запрошенном интервале",
            snapshots.Count, days.Count);

        return [.. snapshots.Order()];
    }

    /// <summary>Сутки, по которым источник публикует снимки: перечень берётся из листинга года.</summary>
    public async Task<IReadOnlyList<DateOnly>> AvailableDaysAsync(
        TimeRange within,
        CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(within.From.UtcDateTime);
        var to = DateOnly.FromDateTime(within.To.UtcDateTime);

        var days = new List<DateOnly>();

        for (var year = from.Year; year <= to.Year; year++)
        {
            IReadOnlyList<string> names = await IndexAsync($"{year}/", "directories", cancellationToken)
                .ConfigureAwait(false);

            foreach (var name in names)
            {
                if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
                    && day >= from
                    && day <= to)
                {
                    days.Add(day);
                }
            }
        }

        return [.. days.Distinct().Order()];
    }

    /// <summary>Моменты снимков одних суток, по возрастанию.</summary>
    public async Task<IReadOnlyList<DateTimeOffset>> SnapshotsOfDayAsync(
        DateOnly day,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> files = await IndexAsync(DayPath(day), "files", cancellationToken).ConfigureAwait(false);

        var snapshots = new List<DateTimeOffset>(files.Count);

        foreach (var name in files)
        {
            if (InstantOf(name) is { } at)
            {
                snapshots.Add(at);
            }
        }

        return [.. snapshots.Order()];
    }

    public IAsyncEnumerable<OrderBookObservation> ObserveAsync(
        IReadOnlyList<DateTimeOffset> snapshots,
        OrderBookScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(scope);

        // Строго по порядку: свёртка держит состояние, и снимок, доехавший раньше
        // предыдущего, дал бы выдуманные события. Параллелизм при этом сохраняется —
        // пока сворачивается текущий, следующие уже едут.
        return ArchivePump.InOrderAsync(
            snapshots,
            options.MaxParallelDownloads,
            (at, token) => FetchSnapshotAsync(at, scope, token),
            cancellationToken);
    }

    /// <summary>
    /// Скачивает и разбирает один снимок, повторяя при обрыве. Возвращает
    /// <see langword="null" />, если источник снимка не публикует либо попытки исчерпаны.
    /// </summary>
    public Task<OrderBookObservation?> FetchSnapshotAsync(
        DateTimeOffset at,
        OrderBookScope scope,
        CancellationToken cancellationToken) =>
        ArchiveRetry.RunAsync(
            $"Снимок {at:yyyy-MM-dd HH:mm:ss}",
            options.MaxAttempts,
            options.RetryDelay,
            logger,
            token => FetchAttemptAsync(at, scope, token),
            cancellationToken);

    /// <summary>
    /// Одна попытка без повтора.
    ///
    /// Условного запроса здесь нет намеренно, и это не послабление вежливости. Снимок,
    /// который у нас уже свёрнут, не спрашивается вовсе: точка продолжения выбирается по
    /// журналу покрытия до первой загрузки. Условный запрос по снимку либо вернул бы те
    /// же байты, либо ответил бы «не изменилось» и оставил свёртку без стакана.
    /// </summary>
    public async Task<OrderBookObservation?> FetchAttemptAsync(
        DateTimeOffset at,
        OrderBookScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{DayPath(DateOnly.FromDateTime(at.UtcDateTime))}{FilePrefix}{at.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture)}{FileSuffix}");

        (var body, DateTimeOffset lastModified, HttpStatusCode status) = await ArchiveDownload
            .FetchAsync(client, path, ifModifiedSince: null, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            if (status is HttpStatusCode.NotFound)
            {
                logger.LogWarning("EVE Ref не публикует снимок {At:yyyy-MM-dd HH:mm:ss}", at);
            }

            return null;
        }

        using StreamReader reader = ArchiveDownload.Read(body);

        IReadOnlyList<RegionBook> regions = await OrderBookCsv
            .ReadAsync(reader, lastModified, scope.Regions, cancellationToken)
            .ConfigureAwait(false);

        return regions.Count == 0
            ? null
            : new OrderBookObservation(at, KeyFor(at), Step, regions);
    }

    /// <summary>Каталог суток в наборе.</summary>
    public static string DayPath(DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"{day.Year}/{day:yyyy-MM-dd}/");

    /// <summary>
    /// Момент снимка из имени файла. Секунды в имени плавают — источник пишет их такими,
    /// какими начался обход, — поэтому момент берётся из имени, а не с получасовой сетки.
    /// </summary>
    public static DateTimeOffset? InstantOf(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!file.StartsWith(FilePrefix, StringComparison.Ordinal)
            || !file.EndsWith(FileSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var stamp = file[FilePrefix.Length..^FileSuffix.Length];

        return DateTime.TryParseExact(
            stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// Имена из <c>index.json</c> — документированный контракт набора, в отличие от
    /// разметки страницы листинга.
    /// </summary>
    public async Task<IReadOnlyList<string>> IndexAsync(
        string directory,
        string section,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client
            .GetAsync(new Uri(directory + "index.json", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return [];
        }

        _ = response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return Names(json, section);
    }

    public static IReadOnlyList<string> Names(string json, string section)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(section, out JsonElement entries)
            || entries.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.TryGetProperty("name", out JsonElement name) && name.GetString() is { } value)
            {
                names.Add(value);
            }
        }

        return names;
    }
}
