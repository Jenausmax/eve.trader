using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Coverage;

/// <summary>
/// Сводит журнал покрытия, реестр материализации и каталог источника в одно из трёх
/// состояний. Чистая функция: всё, что нужно для ответа, приходит аргументами.
///
/// Порядок разбора пробела важен и не произволен:
/// 1. интервал материализован локально, а наблюдений нет — знание утрачено, восполнять
///    нечего: данные были под рукой и не были сняты;
/// 2. не материализован, но источник его публикует — восполним;
/// 3. не публикует — утрачен безвозвратно.
/// </summary>
public static class CoverageResolver
{
    public static CoverageVerdict Resolve(
        RegionId region,
        TimeRange requested,
        FactSet set,
        IReadOnlyCollection<CoverageEntry> entries,
        IReadOnlyCollection<MaterializedInterval> materialized,
        UpstreamCatalog upstream)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(materialized);
        ArgumentNullException.ThrowIfNull(upstream);

        var relevant = entries
            .Where(entry => entry.Region == region && entry.Collected.Overlaps(requested))
            .ToList();

        IReadOnlyList<TimeRange> covered = TimeRanges.Intersect(
            requested,
            relevant.Where(entry => entry.Covers).Select(entry => entry.Collected));

        var localRanges = materialized
            .Where(interval => interval.Set == set && interval.Region == region)
            .Select(interval => interval.Range)
            .ToList();

        var gaps = new List<CoverageGap>();

        foreach (TimeRange hole in TimeRanges.Subtract(requested, covered))
        {
            // Материализованное, но ненаблюдённое — утрачено: данные лежали локально.
            foreach (TimeRange local in TimeRanges.Intersect(hole, localRanges))
            {
                gaps.Add(new CoverageGap(local, CoverageState.NotObserved));
            }

            foreach (TimeRange missing in TimeRanges.Subtract(hole, localRanges))
            {
                IReadOnlyList<TimeRange> publishedParts = upstream.PublishedParts(set, missing);

                foreach (TimeRange published in publishedParts)
                {
                    gaps.Add(new CoverageGap(published, CoverageState.NotMaterialized));
                }

                foreach (TimeRange lost in TimeRanges.Subtract(missing, publishedParts))
                {
                    gaps.Add(new CoverageGap(lost, CoverageState.NotObserved));
                }
            }
        }

        var ordered = gaps.OrderBy(gap => gap.Range.From).ToList();

        CoverageState state = ordered.Count == 0
            ? CoverageState.Observed
            : ordered.Max(gap => gap.State);

        TimeSpan coveredDuration = TimeRanges.TotalDuration(covered);

        return new CoverageVerdict(
            region,
            requested,
            state,
            requested.Duration == TimeSpan.Zero ? 0d : coveredDuration / requested.Duration,
            ordered,
            relevant.Sum(entry => entry.SourceGaps),
            relevant.Count(entry => entry.Outcome == CoverageOutcome.Partial),
            relevant.Count(entry => entry.Outcome == CoverageOutcome.NotModified));
    }
}
