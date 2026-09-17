using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using Parquet.Schema;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Реестр материализованных интервалов — один файл на набор.
///
/// Реестр переписывается целиком, и это не нарушает неизменяемости: он не факт, а
/// указатель на то, что лежит локально. Сдвиг окна убирает запись из реестра, но не
/// трогает данные — удаление сырья запрещено.
/// </summary>
public sealed class ParquetMaterializationRegistry(LakeLayout layout) : IMaterializationRegistry
{
    public const string Region = "region";
    public const string RangeFrom = "range_from";
    public const string RangeTo = "range_to";
    public const string Source = "source";
    public const string LoadedAt = "loaded_at";

    public static ParquetSchema Schema { get; } = new(
        new DataField<int>(Region),
        new DataField<DateTime>(RangeFrom),
        new DataField<DateTime>(RangeTo),
        new DataField<string>(Source),
        new DataField<DateTime>(LoadedAt));

    public async Task<IReadOnlyList<MaterializedInterval>> ReadAsync(
        FactSet set,
        CancellationToken cancellationToken)
    {
        var file = layout.RegistryFile(set);

        if (!File.Exists(file))
        {
            return [];
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = await ParquetCoverageLog.ReadFileAsync(file, cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row => new MaterializedInterval(
                set,
                RegionId.From(Convert.ToInt32(row[Region], CultureInfo.InvariantCulture)),
                TimeRange.Between(CoverageSchema.Utc(row[RangeFrom]), CoverageSchema.Utc(row[RangeTo])),
                (string)row[Source]!,
                CoverageSchema.Utc(row[LoadedAt])))
            .OrderBy(interval => interval.Region.Value)
            .ThenBy(interval => interval.Range.From)];
    }

    public async Task RecordAsync(
        FactSet set,
        TimeRange range,
        IReadOnlyCollection<RegionId> regions,
        string source,
        DateTimeOffset loadedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(regions);

        if (regions.Count == 0)
        {
            throw new ArgumentException(
                "Интервал материализуется целиком: за загруженный интервал отмечаются все присутствовавшие регионы",
                nameof(regions));
        }

        IReadOnlyList<MaterializedInterval> existing = await ReadAsync(set, cancellationToken).ConfigureAwait(false);

        IEnumerable<MaterializedInterval> added = regions.Distinct().Select(region => new MaterializedInterval(set, region, range, source, loadedAt));

        await SaveAsync(set, [.. existing, .. added], cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsMaterializedAsync(
        FactSet set,
        RegionId region,
        TimeRange range,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MaterializedInterval> intervals = await ReadAsync(set, cancellationToken).ConfigureAwait(false);

        IEnumerable<TimeRange> local = intervals
            .Where(interval => interval.Region == region)
            .Select(interval => interval.Range);

        return TimeRanges.Subtract(range, local).Count == 0;
    }

    public async Task<IReadOnlyList<MaterializedInterval>> ApplyWindowAsync(
        FactSet set,
        MaterializationWindow window,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        IReadOnlyList<MaterializedInterval> intervals = await ReadAsync(set, cancellationToken).ConfigureAwait(false);

        if (!MaterializationWindow.IsWindowed(set) || intervals.Count == 0)
        {
            return [];
        }

        DateTimeOffset keepFrom = asOf - window.OrderBookDepth;

        var kept = intervals.Where(interval => interval.Range.To > keepFrom).ToList();
        var dropped = intervals.Where(interval => interval.Range.To <= keepFrom).ToList();

        if (dropped.Count > 0)
        {
            await SaveAsync(set, kept, cancellationToken).ConfigureAwait(false);
        }

        return dropped;
    }

    public async Task SaveAsync(
        FactSet set,
        IReadOnlyList<MaterializedInterval> intervals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        var rows = intervals
            .OrderBy(interval => interval.Region.Value)
            .ThenBy(interval => interval.Range.From)
            .Select(interval => (IDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Region] = interval.Region.Value,
                [RangeFrom] = interval.Range.From.UtcDateTime,
                [RangeTo] = interval.Range.To.UtcDateTime,
                [Source] = interval.Source,
                [LoadedAt] = interval.LoadedAt.UtcDateTime,
            })
            .ToList();

        await AtomicParquet
            .WriteAsync(layout.RegistryFile(set), Schema, rows, cancellationToken)
            .ConfigureAwait(false);
    }
}
