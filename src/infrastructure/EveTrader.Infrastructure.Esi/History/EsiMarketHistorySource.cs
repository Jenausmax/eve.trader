using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using EveTrader.Domain.History;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Esi.History;

/// <summary>
/// Дневная история из живого ESI — тот же порт, что у архива, и это не совпадение,
/// а проверка: факты из обоих источников обязаны лечь в один набор неразличимо.
///
/// Форма запроса у ESI другая: он отдаёт историю по паре «регион и тип» сразу за год,
/// а не сутки на все регионы. Поэтому ответы группируются по рыночным суткам — наружу
/// выходит то же, что у архива.
///
/// Охват обязан называть регионы и типы: полная история по всем типам во всех регионах —
/// сотни тысяч запросов, и это внешнее ограничение, а не настройка.
/// </summary>
public sealed class EsiMarketHistorySource(
    HttpClient client,
    EsiOptions options,
    TimeProvider clock,
    ILogger<EsiMarketHistorySource> logger) : IMarketHistorySource
{
    public string Name => "esi";

    public async IAsyncEnumerable<MarketHistoryObservation> ObserveAsync(
        MarketHistoryScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.Regions.Count == 0 || scope.Types.Count == 0)
        {
            throw new ArgumentException(
                "Охват ESI обязан называть регионы и типы: полная история по всем типам во всех регионах неисполнима",
                nameof(scope));
        }

        var pairs = scope.Regions
            .SelectMany(region => scope.Types.Select(type => (Region: region, Type: type)))
            .ToList();

        var byDate = new ConcurrentDictionary<DateOnly, ConcurrentBag<MarketHistoryRow>>();
        var versions = new ConcurrentDictionary<DateOnly, long>();

        await Parallel.ForEachAsync(
            pairs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxParallelRequests,
                CancellationToken = cancellationToken,
            },
            async (pair, token) =>
            {
                (IReadOnlyList<MarketHistoryRow> rows, DateTimeOffset knownAt) =
                    await FetchAsync(pair.Region, pair.Type, token).ConfigureAwait(false);

                foreach (MarketHistoryRow row in rows)
                {
                    if (!scope.Within.Contains(new DateTimeOffset(row.MarketDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)))
                    {
                        continue;
                    }

                    byDate.GetOrAdd(row.MarketDate, _ => []).Add(row);
                    _ = versions.AddOrUpdate(
                        row.MarketDate,
                        knownAt.ToUnixTimeSeconds(),
                        (_, existing) => Math.Max(existing, knownAt.ToUnixTimeSeconds()));
                }
            }).ConfigureAwait(false);

        foreach (DateOnly date in byDate.Keys.Order())
        {
            List<MarketHistoryRow> rows = [.. byDate[date]
                .OrderBy(row => row.Region.Value)
                .ThenBy(row => row.TypeId)];

            var version = DateTimeOffset.FromUnixTimeSeconds(versions[date]);

            var observation = ObservationId.From(
                string.Create(CultureInfo.InvariantCulture, $"esi-history-{date:yyyy-MM-dd}-{version:yyyyMMddTHHmmssZ}"));

            yield return new MarketHistoryObservation(
                date,
                observation,
                rows,
                TimeSpan.FromDays(1),
                [.. rows.Select(row => row.Region).Distinct().Order()]);
        }
    }

    /// <summary>
    /// История по паре «регион и тип». Время получения берётся из заголовка ответа:
    /// собственные часы сказали бы, когда мы спросили, а не когда источник это знал.
    /// </summary>
    public async Task<(IReadOnlyList<MarketHistoryRow> Rows, DateTimeOffset KnownAt)> FetchAsync(
        RegionId region,
        int typeId,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"markets/{region.Value}/history/?type_id={typeId}&datasource={options.Datasource}");

        using HttpResponseMessage response = await client
            .GetAsync(new Uri(path, UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogDebug("ESI не знает истории по типу {Type} в регионе {Region}", typeId, region.Value);

            return ([], clock.GetUtcNow());
        }

        _ = response.EnsureSuccessStatusCode();

        DateTimeOffset knownAt = response.Content.Headers.LastModified ?? clock.GetUtcNow();

        IReadOnlyList<EsiHistoryEntry>? entries = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<EsiHistoryEntry>>(cancellationToken)
            .ConfigureAwait(false);

        if (entries is null)
        {
            return ([], knownAt);
        }

        var rows = new List<MarketHistoryRow>(entries.Count);

        foreach (EsiHistoryEntry entry in entries)
        {
            rows.Add(new MarketHistoryRow(
                region,
                typeId,
                DateOnly.ParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                entry.Average,
                entry.Highest,
                entry.Lowest,
                entry.OrderCount,
                entry.Volume,
                knownAt));
        }

        return (rows, knownAt);
    }
}
