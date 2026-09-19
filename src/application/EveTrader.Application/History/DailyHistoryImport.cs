using EveTrader.Application.Facts;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.History;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.History;

/// <summary>
/// Импорт дневной истории через единый вход. Источник подставляется снаружи — архив,
/// живой ESI или что угодно ещё, реализующее <see cref="IMarketHistorySource" />;
/// ниже по течению разницы нет.
///
/// Дневная история не подпадает под окно локального хранения: 4.4 ГиБ за двадцать три
/// года — плата, которую нет смысла экономить, а тренды и сезонность живут именно там.
/// </summary>
public sealed class DailyHistoryImport(
    IFactWriter writer,
    IMaterializationRegistry registry,
    TimeProvider clock,
    ILogger<DailyHistoryImport> logger)
{
    public async Task<DailyHistoryImportReport> RunAsync(
        IMarketHistorySource source,
        MarketHistoryScope scope,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scope);

        var written = 0;
        var alreadyPresent = 0;
        long rows = 0;
        var regions = new HashSet<RegionId>();
        var types = new HashSet<int>();
        var loadedDays = new List<DateOnly>();

        await foreach (MarketHistoryObservation observation in
            source.ObserveAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            if (observation.Rows.Count == 0)
            {
                continue;
            }

            FactBatch batch = DailyHistoryFacts.ToBatch(
                observation.Observation, observation.MarketDate, observation.Rows, staticData);

            IReadOnlyList<CoverageEntry> coverage = CoverageFor(observation, source.Name, clock.GetUtcNow());

            FactWriteOutcome outcome = await writer
                .WriteAsync(batch, coverage, cancellationToken)
                .ConfigureAwait(false);

            if (outcome == FactWriteOutcome.AlreadyPresent)
            {
                alreadyPresent++;
                continue;
            }

            written++;
            rows += observation.Rows.Count;
            loadedDays.Add(observation.MarketDate);

            foreach (MarketHistoryRow row in observation.Rows)
            {
                _ = regions.Add(row.Region);
                _ = types.Add(row.TypeId);
            }

            if (written % 100 == 0)
            {
                logger.LogInformation(
                    "Импорт дневной истории из {Source}: {Days} суток, {Rows} строк",
                    source.Name, written, rows);
            }
        }

        if (loadedDays.Count > 0)
        {
            await RecordAsync(loadedDays, regions, source.Name, cancellationToken).ConfigureAwait(false);
        }

        return new DailyHistoryImportReport(
            source.Name, written, alreadyPresent, 0, rows, regions.Count, types.Count);
    }

    /// <summary>
    /// Запись покрытия на регион: норма требует записи по каждой попытке наблюдения
    /// региона. Одно наблюдение архива накрывает их все разом, поэтому записей столько,
    /// сколько регионов, а файл наблюдения один.
    /// </summary>
    public static IReadOnlyList<CoverageEntry> CoverageFor(
        MarketHistoryObservation observation,
        string source,
        DateTimeOffset knownAt)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var from = new DateTimeOffset(observation.MarketDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var collected = TimeRange.Between(from, from.AddDays(1));

        var entries = new List<CoverageEntry>(observation.Regions.Count);

        foreach (RegionId region in observation.Regions)
        {
            var counted = observation.Rows.Count(row => row.Region == region);

            entries.Add(CoverageEntries.Success(
                observation.Observation,
                region,
                collected,
                pages: 1,
                orderCount: counted,
                source: source,
                observationStep: observation.Step,
                knownAt: knownAt));
        }

        return entries;
    }

    private async Task RecordAsync(
        IReadOnlyList<DateOnly> days,
        IReadOnlyCollection<RegionId> regions,
        string source,
        CancellationToken cancellationToken)
    {
        DateOnly earliest = days.Min();
        DateOnly latest = days.Max();

        var from = new DateTimeOffset(earliest.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = new DateTimeOffset(latest.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);

        await registry
            .RecordAsync(FactSet.HistoryDaily, TimeRange.Between(from, to), regions, source, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
    }
}
