using EveTrader.Application.Facts;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Сценарии спеки <c>market-facts/bitemporal-store</c> §«Факт — строка, подтверждённая
/// покрытием» и §«Приём идемпотентен» из <c>market-observation/intake</c>.
/// </summary>
public sealed class FactDefinitionShould
{
    private static readonly TimeRange Week =
        TimeRange.Between(Sample.Day(1), Sample.Day(8));

    [Fact]
    public async Task HideRowsThatCoverageDoesNotConfirm()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Данные записаны в обход протокола — покрытие не создано.
        FactBatch batch = Sample.History("obs-orphan", calendarDay: 1, volume: 100, knownAt: Sample.Day(2));
        await AtomicParquet.WriteAsync(
            lake.Layout.FileFor(batch.Set, batch.ObservedDate, batch.Region, batch.Observation),
            FactFileSchema.For(batch.Columns),
            FactFileSchema.Rows(batch),
            token).ConfigureAwait(true);

        // Одно подтверждённое наблюдение, чтобы озеро не было пустым.
        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-real", calendarDay: 2, volume: 7, knownAt: Sample.Day(3)),
            Sample.Covering("obs-real", observedDay: 2),
            token).ConfigureAwait(true);

        List<FactRow> rows = await lake.Rows.ReadAsync(FactSet.HistoryDaily, Week, null, token).ToListAsync(token).ConfigureAwait(true);

        rows.Count.ShouldBe(1);
        rows[0].Observation.Value.ShouldBe("obs-real");
    }

    [Fact]
    public async Task SweepUnconfirmedRowsWithoutTouchingFacts()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        FactBatch orphan = Sample.History("obs-orphan", calendarDay: 1, volume: 100, knownAt: Sample.Day(2));
        var orphanFile = lake.Layout.FileFor(orphan.Set, orphan.ObservedDate, orphan.Region, orphan.Observation);
        await AtomicParquet.WriteAsync(orphanFile, FactFileSchema.For(orphan.Columns), FactFileSchema.Rows(orphan), token).ConfigureAwait(true);

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-real", calendarDay: 2, volume: 7, knownAt: Sample.Day(3)),
            Sample.Covering("obs-real", observedDay: 2),
            token).ConfigureAwait(true);

        var removed = await lake.Maintenance.SweepUnconfirmedAsync(token).ConfigureAwait(true);

        removed.ShouldBe(1);
        File.Exists(orphanFile).ShouldBeFalse();

        List<FactRow> rows = await lake.Rows.ReadAsync(FactSet.HistoryDaily, Week, null, token).ToListAsync(token).ConfigureAwait(true);
        rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LeaveNoFactsWhenInterruptedBeforeCoverage()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Обрыв ровно между переименованием данных и записью покрытия.
        FactBatch batch = Sample.History("obs-torn", calendarDay: 1, volume: 5, knownAt: Sample.Day(2));
        await AtomicParquet.WriteAsync(
            lake.Layout.FileFor(batch.Set, batch.ObservedDate, batch.Region, batch.Observation),
            FactFileSchema.For(batch.Columns),
            FactFileSchema.Rows(batch),
            token).ConfigureAwait(true);

        IReadOnlySet<ObservationId> confirmed = await lake.Coverage.ConfirmedObservationsAsync(token).ConfigureAwait(true);

        confirmed.ShouldBeEmpty();
        (await lake.Maintenance.SweepUnconfirmedAsync(token).ConfigureAwait(true)).ShouldBe(1);
    }

    [Fact]
    public async Task NotDoubleFactsWhenTheSameObservationIsSubmittedTwice()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        FactBatch batch = Sample.History("obs-once", calendarDay: 1, volume: 42, knownAt: Sample.Day(2));
        CoverageEntry coverage = Sample.Covering("obs-once", observedDay: 1);

        (await lake.Writer.WriteAsync(batch, coverage, token).ConfigureAwait(true)).ShouldBe(FactWriteOutcome.Written);
        (await lake.Writer.WriteAsync(batch, coverage, token).ConfigureAwait(true)).ShouldBe(FactWriteOutcome.AlreadyPresent);

        List<FactRow> rows = await lake.Rows.ReadAsync(FactSet.HistoryDaily, Week, null, token).ToListAsync(token).ConfigureAwait(true);

        rows.Count.ShouldBe(1);
        rows[0].Values["volume"].ShouldBe(42L);
    }

    [Fact]
    public async Task RecordCoverageForObservationsThatCarryNoRows()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        CoverageEntry unchanged = CoverageEntries.NotModified(
            ObservationId.From("obs-quiet"),
            Sample.TheForge,
            TimeRange.Between(Sample.Day(1), Sample.Day(1).AddHours(1)),
            "esi",
            TimeSpan.FromMinutes(5),
            Sample.Day(1).AddHours(1));

        (await lake.Writer.WriteCoverageOnlyAsync(unchanged, token).ConfigureAwait(true)).ShouldBe(FactWriteOutcome.Written);
        (await lake.Writer.WriteCoverageOnlyAsync(unchanged, token).ConfigureAwait(true)).ShouldBe(FactWriteOutcome.AlreadyPresent);

        IReadOnlyList<CoverageEntry> entries = await lake.Coverage.ReadAsync(Week, [], token).ConfigureAwait(true);

        entries.Count.ShouldBe(1);
        entries[0].Outcome.ShouldBe(CoverageOutcome.NotModified);
    }
}
