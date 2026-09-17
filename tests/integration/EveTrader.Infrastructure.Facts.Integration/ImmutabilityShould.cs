using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Сценарии <c>market-facts/bitemporal-store</c> §«Факты неизменяемы» и
/// §«Сырьё не удаляется по сроку».
/// </summary>
public sealed class ImmutabilityShould
{
    [Theory]
    [InlineData(FactSet.OrderEvents)]
    [InlineData(FactSet.OrderBaselines)]
    [InlineData(FactSet.BookCheckpoints)]
    [InlineData(FactSet.HistoryDaily)]
    [InlineData(FactSet.Coverage)]
    public async Task RejectRetentionOnRawSets(FactSet set)
    {
        using var lake = new Lake();

        FactsAreImmutableException rejected = await Should.ThrowAsync<FactsAreImmutableException>(
            () => lake.Maintenance.ApplyRetentionAsync(set, TimeSpan.FromDays(30), TestContext.Current.CancellationToken)).ConfigureAwait(true);

        rejected.Message.ShouldContain(FactSets.PathSegment(set));
    }

    [Fact]
    public async Task RejectDroppingRawSets()
    {
        using var lake = new Lake();

        _ = await Should.ThrowAsync<FactsAreImmutableException>(
            () => lake.Maintenance.DropDerivedAsync(
                FactSet.HistoryDaily,
                TimeRange.Between(Sample.Day(1), Sample.Day(8)),
                TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task AllowDroppingDerivedSetsBecauseTheyRebuildFromRaw()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        var id = ObservationId.From("obs-features");
        var features = FactBatch.Of(
            FactSet.BookFeatures,
            Sample.TheForge,
            id,
            Sample.DayOnly(1),
            [new FactEnvelope("features/10000002/34", EventTime.At(Sample.Day(1)), Sample.Day(1), id, StaticDataVersion.None)],
            [FactColumn.OfDouble("spread", [1.5])]);

        _ = await lake.Writer.WriteAsync(features, Sample.Covering("obs-features", observedDay: 1), token).ConfigureAwait(true);

        var removed = await lake.Maintenance.DropDerivedAsync(
            FactSet.BookFeatures, TimeRange.Between(Sample.Day(1), Sample.Day(2)), token).ConfigureAwait(true);

        removed.ShouldBe(1);

        List<FactRow> rows = await lake.Rows.ReadAsync(
            FactSet.BookFeatures, TimeRange.Between(Sample.Day(1), Sample.Day(8)), null, token).ToListAsync(token).ConfigureAwait(true);
        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeepRawDataWhenTheMaterializationWindowShrinks()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-old", calendarDay: 1, volume: 1, knownAt: Sample.Day(2)),
            Sample.Covering("obs-old", observedDay: 1),
            token).ConfigureAwait(true);

        await lake.Registry.RecordAsync(
            FactSet.OrderEvents,
            TimeRange.Between(Sample.Day(1), Sample.Day(2)),
            [Sample.TheForge],
            "archive",
            Sample.Day(2),
            token).ConfigureAwait(true);

        IReadOnlyList<MaterializedInterval> dropped = await lake.Registry.ApplyWindowAsync(
            FactSet.OrderEvents, MaterializationWindow.Of(TimeSpan.FromHours(1)), Sample.Day(8), token).ConfigureAwait(true);

        dropped.Count.ShouldBe(1);

        // Реестр опустел, данные на месте: удаление сырья запрещено.
        (await lake.Registry.ReadAsync(FactSet.OrderEvents, token).ConfigureAwait(true)).ShouldBeEmpty();
        (await lake.Rows.ReadAsync(FactSet.HistoryDaily, TimeRange.Between(Sample.Day(1), Sample.Day(8)), null, token)
            .ToListAsync(token).ConfigureAwait(true)).Count.ShouldBe(1);
    }
}
