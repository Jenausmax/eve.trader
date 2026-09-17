using System.Data.Common;
using DuckDB.NET.Data;
using EveTrader.Application.Facts;
using EveTrader.Application.Reporting;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;

namespace EveTrader.Infrastructure.Facts.Query;

/// <summary>
/// Порт агрегатов — только для отчётов оператору. Здесь агрегация средствами хранилища
/// разрешена: расхождения обучения с боем для отчёта не существует по определению.
///
/// Состояние покрытия агрегатом не считается и считаться не может: отличить восполнимый
/// пробел от безвозвратного нельзя, не зная, что публикует источник, а это не лежит в
/// файлах. Счётчики берутся запросом, состояние — доменным резолвером.
/// </summary>
public sealed class DuckDbOperationalReportReader(
    LakeLayout layout,
    ICoverageLog coverage,
    IMaterializationRegistry registry,
    UpstreamCatalog upstream) : IOperationalReportReader
{
    public async Task<IReadOnlyList<CoverageReportRow>> CoverageReportAsync(
        TimeRange observed,
        CancellationToken cancellationToken)
    {
        if (!layout.HasFiles(FactSet.Coverage))
        {
            return [];
        }

        IReadOnlyDictionary<RegionId, CoverageCounts> counts = await CountsAsync(observed, cancellationToken).ConfigureAwait(false);

        if (counts.Count == 0)
        {
            return [];
        }

        IReadOnlyList<CoverageEntry> entries = await coverage.ReadAsync(observed, [], cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MaterializedInterval> materialized = await registry.ReadAsync(FactSet.OrderEvents, cancellationToken).ConfigureAwait(false);

        var rows = new List<CoverageReportRow>(counts.Count);

        foreach ((RegionId region, CoverageCounts count) in counts.OrderBy(pair => pair.Key.Value))
        {
            CoverageVerdict verdict = CoverageResolver.Resolve(
                region, observed, FactSet.OrderEvents, entries, materialized, upstream);

            rows.Add(new CoverageReportRow(
                region,
                count.Observations,
                verdict.CoveredFraction,
                count.SourceGaps,
                count.Partial,
                count.Failed,
                verdict.State));
        }

        return rows;
    }

    public async Task<IReadOnlyList<MaterializationReportRow>> MaterializationReportAsync(
        CancellationToken cancellationToken)
    {
        var rows = new List<MaterializationReportRow>();

        foreach (FactSet set in Enum.GetValues<FactSet>())
        {
            IReadOnlyList<MaterializedInterval> intervals = await registry.ReadAsync(set, cancellationToken).ConfigureAwait(false);

            if (intervals.Count == 0)
            {
                continue;
            }

            rows.Add(new MaterializationReportRow(
                set,
                intervals.Select(interval => interval.Region).Distinct().Count(),
                intervals.Min(interval => interval.Range.From),
                intervals.Max(interval => interval.Range.To)));
        }

        return rows;
    }

    /// <summary>Счётчики покрытия по регионам — агрегация средствами хранилища.</summary>
    public async Task<IReadOnlyDictionary<RegionId, CoverageCounts>> CountsAsync(
        TimeRange observed,
        CancellationToken cancellationToken)
    {
        var sql =
            $"""
             SELECT {CoverageSchema.Region} AS region,
                    count(*) AS observations,
                    sum({CoverageSchema.SourceGaps}) AS source_gaps,
                    count(*) FILTER (WHERE {CoverageSchema.Outcome} = {(int)CoverageOutcome.Partial}) AS partial_observations,
                    count(*) FILTER (WHERE {CoverageSchema.Outcome} = {(int)CoverageOutcome.Failure}) AS failed_observations
             FROM read_parquet({DuckDb.Literal(layout.SetGlob(FactSet.Coverage))}, hive_partitioning = 1)
             WHERE {CoverageSchema.CollectedFrom} < {DuckDb.Timestamp(observed.To)}
               AND {CoverageSchema.CollectedTo} > {DuckDb.Timestamp(observed.From)}
             GROUP BY {CoverageSchema.Region}
             ORDER BY {CoverageSchema.Region}
             """;

        await using DuckDBConnection connection = DuckDb.Open();
        await using DuckDBCommand command = connection.CreateCommand();

        // CA2100: путь к озеру приходит из конфигурации, не от пользователя, и проходит
        // через DuckDb.Literal; read_parquet принимает путь только литералом.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var counts = new Dictionary<RegionId, CoverageCounts>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[RegionId.From((int)DuckDb.ToInt64(reader.GetValue(0)))] = new CoverageCounts(
                DuckDb.ToInt64(reader.GetValue(1)),
                DuckDb.ToInt64(reader.GetValue(2)),
                DuckDb.ToInt64(reader.GetValue(3)),
                DuckDb.ToInt64(reader.GetValue(4)));
        }

        return counts;
    }
}
