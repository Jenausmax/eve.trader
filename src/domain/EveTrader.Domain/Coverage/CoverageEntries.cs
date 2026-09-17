using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Coverage;

/// <summary>Построение записей покрытия с проверкой инвариантов каждого исхода.</summary>
public static class CoverageEntries
{
    public static CoverageEntry Success(
        ObservationId observation,
        RegionId region,
        TimeRange collected,
        int pages,
        int orderCount,
        string source,
        TimeSpan observationStep,
        DateTimeOffset knownAt,
        int sourceGaps = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceGaps);
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);

        return new CoverageEntry(
            observation, region, collected, CoverageOutcome.Success,
            pages, pages, orderCount, Named(source), observationStep, sourceGaps, null, knownAt);
    }

    /// <summary>
    /// Источник ответил «не изменилось». Наблюдение состоялось, событий нет — и это
    /// факт о рынке, а не пробел.
    /// </summary>
    public static CoverageEntry NotModified(
        ObservationId observation,
        RegionId region,
        TimeRange collected,
        string source,
        TimeSpan observationStep,
        DateTimeOffset knownAt) =>
        new(observation, region, collected, CoverageOutcome.NotModified,
            0, 0, 0, Named(source), observationStep, 0, null, knownAt);

    public static CoverageEntry Partial(
        ObservationId observation,
        RegionId region,
        TimeRange collected,
        int pagesReceived,
        int pagesExpected,
        int orderCount,
        string source,
        TimeSpan observationStep,
        DateTimeOffset knownAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orderCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(pagesReceived, 0);

        return pagesReceived >= pagesExpected
            ? throw new ArgumentOutOfRangeException(
                nameof(pagesReceived), pagesReceived, "Частичное наблюдение получило меньше страниц, чем объявил источник")
            : new CoverageEntry(
            observation, region, collected, CoverageOutcome.Partial,
            pagesReceived, pagesExpected, orderCount, Named(source), observationStep, 0, null, knownAt);
    }

    public static CoverageEntry Failed(
        ObservationId observation,
        RegionId region,
        TimeRange collected,
        string source,
        TimeSpan observationStep,
        string reason,
        DateTimeOffset knownAt)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("Отказ несёт причину: запись без причины не отличима от пробела", nameof(reason))
            : new CoverageEntry(
            observation, region, collected, CoverageOutcome.Failure,
            0, 0, 0, Named(source), observationStep, 0, reason, knownAt);
    }

    public static string Named(string source) =>
        string.IsNullOrWhiteSpace(source)
            ? throw new ArgumentException("Источник наблюдения назван", nameof(source))
            : source;
}
