using EveTrader.Application.Reporting;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Query;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Сценарии <c>market-facts/bitemporal-store</c> §«Границы вычислений в хранилище» и
/// <c>market-observation/coverage-log</c> §«Покрытие доступно как данные».
/// </summary>
public sealed class OperationalReportShould
{
    private static readonly TimeRange Day = TimeRange.Between(Sample.Day(1), Sample.Day(2));

    [Fact]
    public async Task AggregateCoverageInTheStoreForTheOperator()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;
        var domain = RegionId.From(10000043);

        _ = await lake.Writer.WriteCoverageOnlyAsync([Sample.Covering("obs-a", observedDay: 1, sourceGaps: 2)], token).ConfigureAwait(true);
        _ = await lake.Writer.WriteCoverageOnlyAsync([Sample.Covering("obs-b", observedDay: 1, sourceGaps: 1)], token).ConfigureAwait(true);
        _ = await lake.Writer.WriteCoverageOnlyAsync([Sample.Covering("obs-c", observedDay: 1, region: domain)], token).ConfigureAwait(true);

        IReadOnlyDictionary<RegionId, CoverageCounts> counts = await lake.Reports(UpstreamCatalog.Empty).CountsAsync(Day, token).ConfigureAwait(true);

        counts[Sample.TheForge].Observations.ShouldBe(2);
        counts[Sample.TheForge].SourceGaps.ShouldBe(3);
        counts[domain].Observations.ShouldBe(1);
    }

    [Fact]
    public async Task CountPartialAndFailedObservationsSeparately()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteCoverageOnlyAsync([Sample.Covering("obs-ok", observedDay: 1)], token).ConfigureAwait(true);

        _ = await lake.Writer.WriteCoverageOnlyAsync(
            [CoverageEntries.Partial(
                ObservationId.From("obs-torn"), Sample.TheForge,
                TimeRange.Between(Sample.Day(1).AddHours(2), Sample.Day(1).AddHours(3)),
                pagesReceived: 30, pagesExpected: 40, orderCount: 900,
                source: "esi", observationStep: TimeSpan.FromMinutes(5), knownAt: Sample.Day(1).AddHours(3))],
            token).ConfigureAwait(true);

        _ = await lake.Writer.WriteCoverageOnlyAsync(
            [CoverageEntries.Failed(
                ObservationId.From("obs-dead"), Sample.TheForge,
                TimeRange.Between(Sample.Day(1).AddHours(4), Sample.Day(1).AddHours(5)),
                "esi", TimeSpan.FromMinutes(5), "503", Sample.Day(1).AddHours(5))],
            token).ConfigureAwait(true);

        CoverageCounts counts = (await lake.Reports(UpstreamCatalog.Empty).CountsAsync(Day, token).ConfigureAwait(true))[Sample.TheForge];

        counts.Observations.ShouldBe(3);
        counts.Partial.ShouldBe(1);
        counts.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task ReportStateAlongsideTheAggregatedCounts()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteCoverageOnlyAsync(
            [CoverageEntries.Success(
                ObservationId.From("obs-half"), Sample.TheForge,
                TimeRange.Between(Sample.Day(1), Sample.Day(1).AddHours(12)),
                pages: 1, orderCount: 5, source: "esi",
                observationStep: TimeSpan.FromMinutes(5), knownAt: Sample.Day(1).AddHours(12))],
            token).ConfigureAwait(true);

        var upstream = UpstreamCatalog.Of(new Dictionary<FactSet, TimeRange>
        {
            [FactSet.OrderEvents] = TimeRange.Between(Sample.Day(1), Sample.Day(8)),
        });

        CoverageReportRow row = (await lake.Reports(upstream).CoverageReportAsync(Day, token).ConfigureAwait(true)).Single();

        row.Region.ShouldBe(Sample.TheForge);
        row.Observations.ShouldBe(1);
        row.CoveredFraction.ShouldBe(0.5d, 0.001d);
        row.State.ShouldBe(CoverageState.NotMaterialized);
    }

    [Fact]
    public async Task ReportWhatIsMaterializedLocally()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        await lake.Registry.RecordAsync(
            FactSet.OrderEvents, Day, [Sample.TheForge, RegionId.From(10000043)], "archive", Sample.Day(2), token).ConfigureAwait(true);

        IReadOnlyList<MaterializationReportRow> rows = await lake.Reports(UpstreamCatalog.Empty).MaterializationReportAsync(token).ConfigureAwait(true);

        MaterializationReportRow events = rows.Single(static row => row.Set == FactSet.OrderEvents);
        events.Regions.ShouldBe(2);
        events.Earliest.ShouldBe(Sample.Day(1));
        events.Latest.ShouldBe(Sample.Day(2));
    }

    [Fact]
    public async Task ReturnNothingWhenTheLakeIsEmpty()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        (await lake.Reports(UpstreamCatalog.Empty).CoverageReportAsync(Day, token).ConfigureAwait(true)).ShouldBeEmpty();
        (await lake.Reports(UpstreamCatalog.Empty).MaterializationReportAsync(token).ConfigureAwait(true)).ShouldBeEmpty();
    }
}
