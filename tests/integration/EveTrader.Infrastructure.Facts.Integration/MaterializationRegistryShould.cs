using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>Сценарии спеки <c>market-facts/materialization</c>.</summary>
public sealed class MaterializationRegistryShould
{
    private static readonly RegionId Domain = RegionId.From(10000043);

    [Fact]
    public async Task AnswerFromTheRegistryNotFromTheFileSystem()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var range = TimeRange.Between(Sample.Day(1), Sample.Day(3));

        // Файлы на диске есть, а записи в реестре нет — значит не материализовано.
        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-1", calendarDay: 1, volume: 1, knownAt: Sample.Day(2)),
            Sample.Covering("obs-1", observedDay: 1),
            token).ConfigureAwait(true);

        (await lake.Registry.IsMaterializedAsync(FactSet.HistoryDaily, Sample.TheForge, range, token).ConfigureAwait(true))
            .ShouldBeFalse();

        await lake.Registry.RecordAsync(FactSet.HistoryDaily, range, [Sample.TheForge], "archive", Sample.Day(3), token).ConfigureAwait(true);

        (await lake.Registry.IsMaterializedAsync(FactSet.HistoryDaily, Sample.TheForge, range, token).ConfigureAwait(true))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task MarkEveryRegionOfALoadedInterval()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;
        var range = TimeRange.Between(Sample.Day(1), Sample.Day(2));

        // Архивные снимки глобальны на момент: за интервал отмечаются все регионы.
        await lake.Registry.RecordAsync(
            FactSet.OrderEvents, range, [Sample.TheForge, Domain], "archive", Sample.Day(2), token).ConfigureAwait(true);

        IReadOnlyList<MaterializedInterval> intervals = await lake.Registry.ReadAsync(FactSet.OrderEvents, token).ConfigureAwait(true);

        intervals.Select(interval => interval.Region).ShouldBe([Sample.TheForge, Domain]);
        intervals.ShouldAllBe(interval => interval.Source == "archive");
        intervals.ShouldAllBe(interval => interval.Range == range);
    }

    [Fact]
    public async Task RefuseToRecordAnIntervalWithoutRegions()
    {
        using var lake = new Lake();

        _ = await Should.ThrowAsync<ArgumentException>(
            () => lake.Registry.RecordAsync(
                FactSet.OrderEvents,
                TimeRange.Between(Sample.Day(1), Sample.Day(2)),
                [],
                "archive",
                Sample.Day(2),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DropIntervalsOutsideTheWindowAndLeaveThemReplenishable()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var old = TimeRange.Between(Sample.Day(1), Sample.Day(2));
        var recent = TimeRange.Between(Sample.Day(6), Sample.Day(7));

        await lake.Registry.RecordAsync(FactSet.OrderEvents, old, [Sample.TheForge], "archive", Sample.Day(2), token).ConfigureAwait(true);
        await lake.Registry.RecordAsync(FactSet.OrderEvents, recent, [Sample.TheForge], "archive", Sample.Day(7), token).ConfigureAwait(true);

        IReadOnlyList<MaterializedInterval> dropped = await lake.Registry.ApplyWindowAsync(
            FactSet.OrderEvents, MaterializationWindow.Of(TimeSpan.FromDays(3)), Sample.Day(8), token).ConfigureAwait(true);

        dropped.Single().Range.ShouldBe(old);

        IReadOnlyList<MaterializedInterval> kept = await lake.Registry.ReadAsync(FactSet.OrderEvents, token).ConfigureAwait(true);
        kept.Single().Range.ShouldBe(recent);

        // Вне окна — восполнимо, если источник публикует.
        var upstream = UpstreamCatalog.Of(new Dictionary<FactSet, TimeRange>
        {
            [FactSet.OrderEvents] = TimeRange.Between(Sample.Day(1), Sample.Day(8)),
        });
        CoverageVerdict verdict = CoverageResolver.Resolve(
            Sample.TheForge, old, FactSet.OrderEvents, [], kept, upstream);

        verdict.State.ShouldBe(CoverageState.NotMaterialized);
        verdict.Replenishable.ShouldBeTrue();
    }

    [Fact]
    public async Task NeverWindowDailyHistory()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        await lake.Registry.RecordAsync(
            FactSet.HistoryDaily,
            TimeRange.Between(Sample.Day(1), Sample.Day(2)),
            [Sample.TheForge],
            "archive",
            Sample.Day(2),
            token).ConfigureAwait(true);

        IReadOnlyList<MaterializedInterval> dropped = await lake.Registry.ApplyWindowAsync(
            FactSet.HistoryDaily, MaterializationWindow.Of(TimeSpan.FromHours(1)), Sample.Day(8), token).ConfigureAwait(true);

        dropped.ShouldBeEmpty();
        (await lake.Registry.ReadAsync(FactSet.HistoryDaily, token).ConfigureAwait(true)).Count.ShouldBe(1);
    }
}
