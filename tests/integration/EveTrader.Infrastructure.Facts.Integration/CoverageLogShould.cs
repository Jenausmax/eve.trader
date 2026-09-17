using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>Сценарии спеки <c>market-observation/coverage-log</c> на настоящих файлах.</summary>
public sealed class CoverageLogShould
{
    private static readonly TimeRange Week = TimeRange.Between(Sample.Day(1), Sample.Day(8));

    [Fact]
    public async Task RecordEveryAttemptWithItsOutcome()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteCoverageOnlyAsync(Sample.Covering("obs-ok", observedDay: 1), token).ConfigureAwait(true);

        _ = await lake.Writer.WriteCoverageOnlyAsync(
            CoverageEntries.Failed(
                ObservationId.From("obs-dead"),
                Sample.TheForge,
                TimeRange.Between(Sample.Day(2), Sample.Day(2).AddHours(1)),
                "esi",
                TimeSpan.FromMinutes(5),
                "источник ответил 503",
                Sample.Day(2).AddHours(1)),
            token).ConfigureAwait(true);

        IReadOnlyList<CoverageEntry> entries = await lake.Coverage.ReadAsync(Week, [], token).ConfigureAwait(true);

        entries.Count.ShouldBe(2);
        entries[0].Outcome.ShouldBe(CoverageOutcome.Success);
        entries[0].OrderCount.ShouldBe(1);
        entries[1].Outcome.ShouldBe(CoverageOutcome.Failure);
        entries[1].FailureReason.ShouldBe("источник ответил 503");
    }

    [Fact]
    public async Task DistinguishThreeStatesOverStoredEntries()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Наблюдались первые сутки; вторые доступны наверху; третьи не доступны нигде.
        _ = await lake.Writer.WriteCoverageOnlyAsync(
            CoverageEntries.Success(
                ObservationId.From("obs-day1"), Sample.TheForge,
                TimeRange.Between(Sample.Day(1), Sample.Day(2)),
                pages: 1, orderCount: 10, source: "archive",
                observationStep: TimeSpan.FromMinutes(30), knownAt: Sample.Day(2)),
            token).ConfigureAwait(true);

        IReadOnlyList<CoverageEntry> entries = await lake.Coverage.ReadAsync(Week, [], token).ConfigureAwait(true);
        var upstream = UpstreamCatalog.Of(new Dictionary<FactSet, TimeRange>
        {
            [FactSet.OrderEvents] = TimeRange.Between(Sample.Day(1), Sample.Day(3)),
        });

        CoverageResolver.Resolve(Sample.TheForge, TimeRange.Between(Sample.Day(1), Sample.Day(2)), FactSet.OrderEvents, entries, [], upstream)
            .State.ShouldBe(CoverageState.Observed);

        CoverageResolver.Resolve(Sample.TheForge, TimeRange.Between(Sample.Day(2), Sample.Day(3)), FactSet.OrderEvents, entries, [], upstream)
            .State.ShouldBe(CoverageState.NotMaterialized);

        CoverageResolver.Resolve(Sample.TheForge, TimeRange.Between(Sample.Day(3), Sample.Day(4)), FactSet.OrderEvents, entries, [], upstream)
            .State.ShouldBe(CoverageState.NotObserved);
    }

    [Fact]
    public async Task KeepSourceGapsForChoosingTheDisappearanceWindow()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteCoverageOnlyAsync(Sample.Covering("obs-a", observedDay: 1, sourceGaps: 2), token).ConfigureAwait(true);
        _ = await lake.Writer.WriteCoverageOnlyAsync(Sample.Covering("obs-b", observedDay: 2, sourceGaps: 5), token).ConfigureAwait(true);

        IReadOnlyList<CoverageEntry> entries = await lake.Coverage.ReadAsync(Week, [], token).ConfigureAwait(true);

        entries.Sum(static entry => entry.SourceGaps).ShouldBe(7);
    }

    [Fact]
    public async Task FilterByRegionWhenAsked()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;
        var domain = RegionId.From(10000043);

        _ = await lake.Writer.WriteCoverageOnlyAsync(Sample.Covering("obs-forge", observedDay: 1), token).ConfigureAwait(true);
        _ = await lake.Writer.WriteCoverageOnlyAsync(Sample.Covering("obs-domain", observedDay: 1, region: domain), token).ConfigureAwait(true);

        (await lake.Coverage.ReadAsync(Week, [], token).ConfigureAwait(true)).Count.ShouldBe(2);
        (await lake.Coverage.ReadAsync(Week, [domain], token).ConfigureAwait(true)).Single().Region.ShouldBe(domain);
    }

    [Fact]
    public async Task PreserveObservationStepDeclaredBySource()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteCoverageOnlyAsync(
            CoverageEntries.Success(
                ObservationId.From("obs-archive"), Sample.TheForge,
                TimeRange.Between(Sample.Day(1), Sample.Day(1).AddMinutes(30)),
                pages: 1, orderCount: 10, source: "archive",
                observationStep: TimeSpan.FromMinutes(30), knownAt: Sample.Day(2)),
            token).ConfigureAwait(true);

        CoverageEntry entry = (await lake.Coverage.ReadAsync(Week, [], token).ConfigureAwait(true)).Single();

        entry.ObservationStep.ShouldBe(TimeSpan.FromMinutes(30));
        entry.Source.ShouldBe("archive");
    }
}
