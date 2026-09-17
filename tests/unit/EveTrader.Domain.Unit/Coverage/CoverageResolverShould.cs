using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Domain.Unit.Coverage;

/// <summary>
/// Сценарии спеки <c>market-observation/coverage-log</c> §«Три состояния покрытия».
/// </summary>
public sealed class CoverageResolverShould
{
    private static readonly RegionId TheForge = RegionId.From(10000002);

    private static readonly DateTimeOffset Midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour) => Midnight.AddHours(hour);

    private static TimeRange Range(int from, int to) => TimeRange.Between(At(from), At(to));

    private static CoverageEntry Observed(int from, int to, int sourceGaps = 0) =>
        CoverageEntries.Success(
            ObservationId.From($"obs-{from}-{to}"), TheForge, Range(from, to),
            pages: 1, orderCount: 100, source: "esi", observationStep: TimeSpan.FromMinutes(5),
            knownAt: At(to), sourceGaps: sourceGaps);

    private static CoverageVerdict Resolve(
        TimeRange requested,
        IReadOnlyCollection<CoverageEntry> entries,
        IReadOnlyCollection<MaterializedInterval> materialized,
        UpstreamCatalog upstream) =>
        CoverageResolver.Resolve(TheForge, requested, FactSet.OrderEvents, entries, materialized, upstream);

    private static UpstreamCatalog Publishing(TimeRange range) =>
        UpstreamCatalog.Of(new Dictionary<FactSet, TimeRange> { [FactSet.OrderEvents] = range });

    private static MaterializedInterval Local(int from, int to) =>
        new(FactSet.OrderEvents, TheForge, Range(from, to), "archive", At(to));

    [Fact]
    public void ReportObservedWhenIntervalIsFullyCovered()
    {
        CoverageVerdict verdict = Resolve(Range(0, 4), [Observed(0, 2), Observed(2, 4)], [], UpstreamCatalog.Empty);

        verdict.State.ShouldBe(CoverageState.Observed);
        verdict.Gaps.ShouldBeEmpty();
        verdict.CoveredFraction.ShouldBe(1d);
    }

    [Fact]
    public void TreatAbsenceOfChangesAsFactNotAsGap()
    {
        CoverageEntry unchanged = CoverageEntries.NotModified(
            ObservationId.From("obs-quiet"), TheForge, Range(0, 4),
            "esi", TimeSpan.FromMinutes(5), At(4));

        CoverageVerdict verdict = Resolve(Range(0, 4), [unchanged], [], UpstreamCatalog.Empty);

        verdict.State.ShouldBe(CoverageState.Observed);
        verdict.UnchangedObservations.ShouldBe(1);
        verdict.UnchangedIsFact.ShouldBeTrue();
    }

    [Fact]
    public void ReportNotMaterializedWhenUpstreamStillPublishesTheGap()
    {
        CoverageVerdict verdict = Resolve(Range(0, 4), [Observed(0, 2)], [], Publishing(Range(0, 24)));

        verdict.State.ShouldBe(CoverageState.NotMaterialized);
        verdict.Replenishable.ShouldBeTrue();
        verdict.Gaps.ShouldBe([new CoverageGap(Range(2, 4), CoverageState.NotMaterialized)]);
        verdict.CoveredFraction.ShouldBe(0.5d);
    }

    [Fact]
    public void ReportNotObservedWhenNobodyHasTheInterval()
    {
        CoverageVerdict verdict = Resolve(Range(0, 4), [Observed(0, 2)], [], UpstreamCatalog.Empty);

        verdict.State.ShouldBe(CoverageState.NotObserved);
        verdict.Replenishable.ShouldBeFalse();
        verdict.Gaps.ShouldBe([new CoverageGap(Range(2, 4), CoverageState.NotObserved)]);
    }

    [Fact]
    public void ReportNotObservedWhenIntervalWasLocalButNeverObserved()
    {
        // Данные лежали под рукой и не были сняты — восполнять нечего.
        CoverageVerdict verdict = Resolve(Range(0, 4), [Observed(0, 2)], [Local(0, 4)], Publishing(Range(0, 24)));

        verdict.State.ShouldBe(CoverageState.NotObserved);
        verdict.Gaps.ShouldBe([new CoverageGap(Range(2, 4), CoverageState.NotObserved)]);
    }

    [Fact]
    public void PickTheWorstStateWhenGapsDiffer()
    {
        // Пробел 1–2 восполним, пробел 3–4 утрачен: ответ по худшему.
        CoverageVerdict verdict = Resolve(Range(0, 4), [Observed(0, 1), Observed(2, 3)], [], Publishing(Range(0, 2)));

        verdict.State.ShouldBe(CoverageState.NotObserved);
        verdict.Replenishable.ShouldBeFalse();
        verdict.Gaps.Select(static gap => gap.State)
            .ShouldBe([CoverageState.NotMaterialized, CoverageState.NotObserved]);
    }

    [Fact]
    public void NotLetFailedObservationCoverItsInterval()
    {
        CoverageEntry failed = CoverageEntries.Failed(
            ObservationId.From("obs-dead"), TheForge, Range(0, 4),
            "esi", TimeSpan.FromMinutes(5), "источник ответил 503", At(4));

        CoverageVerdict verdict = Resolve(Range(0, 4), [failed], [], UpstreamCatalog.Empty);

        verdict.State.ShouldBe(CoverageState.NotObserved);
        verdict.CoveredFraction.ShouldBe(0d);
    }

    [Fact]
    public void CountSourceGapsAndPartialObservations()
    {
        CoverageEntry partial = CoverageEntries.Partial(
            ObservationId.From("obs-torn"), TheForge, Range(2, 4),
            pagesReceived: 30, pagesExpected: 40, orderCount: 900,
            source: "esi", observationStep: TimeSpan.FromMinutes(5), knownAt: At(4));

        CoverageVerdict verdict = Resolve(Range(0, 4), [Observed(0, 2, sourceGaps: 3), partial], [], UpstreamCatalog.Empty);

        verdict.SourceGaps.ShouldBe(3);
        verdict.PartialObservations.ShouldBe(1);
        verdict.State.ShouldBe(CoverageState.Observed);
    }

    [Fact]
    public void IgnoreObservationsOfOtherRegions()
    {
        CoverageEntry elsewhere = CoverageEntries.Success(
            ObservationId.From("obs-other"), RegionId.From(10000043), Range(0, 4),
            pages: 1, orderCount: 5, source: "esi", observationStep: TimeSpan.FromMinutes(5), knownAt: At(4));

        Resolve(Range(0, 4), [elsewhere], [], UpstreamCatalog.Empty)
            .State.ShouldBe(CoverageState.NotObserved);
    }
}
