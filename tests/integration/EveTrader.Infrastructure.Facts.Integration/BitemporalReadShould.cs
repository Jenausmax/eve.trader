using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Сценарии <c>market-facts/bitemporal-store</c> §«Чтение на момент времени»,
/// §«Каждый факт несёт два времени» и §«Факты неизменяемы».
/// </summary>
public sealed class BitemporalReadShould
{
    private static readonly TimeRange Week = TimeRange.Between(Sample.Day(1), Sample.Day(8));

    /// <summary>Строка за 1 января, полученная дважды: во второй раз с уточнённым объёмом.</summary>
    private static async Task<Lake> WithRefinedHistoryAsync(CancellationToken token)
    {
        var lake = new Lake();

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-first", calendarDay: 1, volume: 100, knownAt: Sample.Day(1).AddHours(6)),
            [Sample.Covering("obs-first", observedDay: 1)],
            token).ConfigureAwait(true);

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-refined", calendarDay: 1, volume: 175, knownAt: Sample.Day(3), observedDay: 3),
            [Sample.Covering("obs-refined", observedDay: 3)],
            token).ConfigureAwait(true);

        return lake;
    }

    [Fact]
    public async Task KeepBothVersionsOnDisk()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using Lake lake = await WithRefinedHistoryAsync(token).ConfigureAwait(true);

        IReadOnlyList<FactRow> all = await lake.Rows.SelectAsync(FactSet.HistoryDaily, Week, null, token).ConfigureAwait(true);

        all.Count.ShouldBe(2);
        all.Select(static row => row.Values["volume"]).Order().ShouldBe([100L, 175L]);
        all.Select(static row => row.FactKey).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task HideTheRefinementFromABacktestThatPredatesIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using Lake lake = await WithRefinedHistoryAsync(token).ConfigureAwait(true);

        List<FactRow> seen = await lake.Rows.ReadAsync(FactSet.HistoryDaily, Week, Sample.Day(2), token).ToListAsync(token).ConfigureAwait(true);

        seen.Count.ShouldBe(1);
        seen[0].Values["volume"].ShouldBe(100L);
    }

    [Fact]
    public async Task ReturnLatestVersionWhenNoInstantGiven()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using Lake lake = await WithRefinedHistoryAsync(token).ConfigureAwait(true);

        List<FactRow> seen = await lake.Rows.ReadAsync(FactSet.HistoryDaily, Week, null, token).ToListAsync(token).ConfigureAwait(true);

        seen.Count.ShouldBe(1);
        seen[0].Values["volume"].ShouldBe(175L);
    }

    [Fact]
    public async Task CarryBothTimesAndStaticDataVersion()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using Lake lake = await WithRefinedHistoryAsync(token).ConfigureAwait(true);

        FactRow row = (await lake.Rows.ReadAsync(FactSet.HistoryDaily, Week, null, token).ToListAsync(token).ConfigureAwait(true)).Single();

        row.Envelope.EventTime.Kind.ShouldBe(EventTimeKind.Instant);
        row.Envelope.EventTime.Instant.ShouldBe(Sample.Day(1));
        row.Envelope.KnownAt.ShouldBe(Sample.Day(3));
        row.Envelope.StaticData.Value.ShouldBe("sde-2026.01");
        row.Region.ShouldBe(Sample.TheForge);
    }

    [Fact]
    public async Task KeepFactsOfOlderStaticDataVersionUnchanged()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var lake = new Lake();

        FactBatch old = Sample.History("obs-old", calendarDay: 1, volume: 10, knownAt: Sample.Day(2));
        _ = await lake.Writer.WriteAsync(old, [Sample.Covering("obs-old", observedDay: 1)], token).ConfigureAwait(true);

        var recomputed = FactBatch.Of(
            FactSet.HistoryDaily,
            null,
            ObservationId.From("obs-new-sde"),
            Sample.DayOnly(3),
            [new FactEnvelope(
                old.Envelopes[0].FactKey,
                EventTime.At(Sample.Day(1)),
                Sample.Day(3),
                ObservationId.From("obs-new-sde"),
                StaticDataVersion.From("sde-2026.02"))],
            [
                FactColumn.OfInt64("region", [Sample.TheForge.Value]),
                FactColumn.OfInt64("volume", [11]),
            ]);

        _ = await lake.Writer.WriteAsync(recomputed, [Sample.Covering("obs-new-sde", observedDay: 3)], token).ConfigureAwait(true);

        var versions = (await lake.Rows.SelectAsync(FactSet.HistoryDaily, Week, null, token).ConfigureAwait(true))
            .Select(static row => row.Envelope.StaticData.Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        versions.ShouldBe(["sde-2026.01", "sde-2026.02"]);
    }
}
