using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.Replay;

/// <summary>
/// Сценарии спеки <c>market-observation/intake</c> §«Наблюдения поступают через единый
/// вход»: реплей неотличим от живого прогона и детерминирован.
/// </summary>
public sealed class ReplayShould
{
    private static readonly TimeRange Window =
        TimeRange.Between(RecordedRun.At(-60), RecordedRun.At(600));

    /// <summary>
    /// Прогон, дающий все виды событий: появление, перестановку, исполнение и
    /// подтверждённое исчезновение.
    /// </summary>
    private static RecordedRun Run() =>
        new("live",
        [
            RecordedRun.Observation(0,
                [RecordedRun.Order(1, 100m), RecordedRun.Order(2, 90m, isBuy: true)]),
            RecordedRun.Observation(5,
                [RecordedRun.Order(1, 120m, issuedMinute: 4), RecordedRun.Order(2, 90m, isBuy: true)]),
            RecordedRun.Observation(10,
                [RecordedRun.Order(1, 120m, remain: 70, issuedMinute: 4), RecordedRun.Order(2, 90m, isBuy: true),
                 RecordedRun.Order(3, 95m, issuedMinute: 9)]),
            RecordedRun.Observation(15,
                [RecordedRun.Order(1, 120m, remain: 70, issuedMinute: 4), RecordedRun.Order(3, 95m, issuedMinute: 9)]),
            RecordedRun.Observation(20,
                [RecordedRun.Order(1, 120m, remain: 70, issuedMinute: 4), RecordedRun.Order(3, 95m, issuedMinute: 9)]),
        ]);

    private static async Task<List<FactRow>> ReadAsync(
        ReplayLake lake,
        FactSet set,
        CancellationToken cancellationToken)
    {
        var rows = new List<FactRow>();

        await foreach (FactRow row in lake.Rows.ReadAsync(set, Window, null, cancellationToken).ConfigureAwait(true))
        {
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Сравнимая часть события: всё, кроме идентификатора наблюдения.</summary>
    private static string Shape(FactRow row) =>
        string.Join('|',
            row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))
        + $"|from={row.Envelope.EventTime.From:O}|to={row.Envelope.EventTime.To:O}"
        + $"|kind={row.Envelope.EventTime.Kind}|region={row.Region}";

    [Fact]
    public async Task ProduceTheSameEventsAsTheRunItReplays()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        using var original = new ReplayLake("original");
        using var replayed = new ReplayLake("replayed");

        IntakeReport first = await original.RunAsync(Run(), token).ConfigureAwait(true);

        first.Written.ShouldBe(5);
        first.Events.ShouldBeGreaterThan(0);

        // Реплей читает первое озеро и подаёт восстановленные наблюдения на тот же вход.
        IntakeReport second = await replayed
            .RunAsync(original.Replay(Window), token)
            .ConfigureAwait(true);

        second.Written.ShouldBe(first.Written);

        List<FactRow> before = await ReadAsync(original, FactSet.OrderEvents, token).ConfigureAwait(true);
        List<FactRow> after = await ReadAsync(replayed, FactSet.OrderEvents, token).ConfigureAwait(true);

        after.Select(Shape).Order(StringComparer.Ordinal)
            .ShouldBe(before.Select(Shape).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProduceTheSameFeaturesAsTheRunItReplays()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        using var original = new ReplayLake("original");
        using var replayed = new ReplayLake("replayed");

        _ = await original.RunAsync(Run(), token).ConfigureAwait(true);
        _ = await replayed.RunAsync(original.Replay(Window), token).ConfigureAwait(true);

        List<FactRow> before = await ReadAsync(original, FactSet.BookFeatures, token).ConfigureAwait(true);
        List<FactRow> after = await ReadAsync(replayed, FactSet.BookFeatures, token).ConfigureAwait(true);

        before.ShouldNotBeEmpty();
        after.Select(Shape).Order(StringComparer.Ordinal)
            .ShouldBe(before.Select(Shape).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BeDeterministicIncludingRowOrder()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        using var original = new ReplayLake("original");
        using var once = new ReplayLake("once");
        using var twice = new ReplayLake("twice");

        _ = await original.RunAsync(Run(), token).ConfigureAwait(true);

        _ = await once.RunAsync(original.Replay(Window), token).ConfigureAwait(true);
        _ = await twice.RunAsync(original.Replay(Window), token).ConfigureAwait(true);

        // Порядок строк — часть раскладки озера, и недетерминированный реплей был бы
        // непригоден как эталон: расхождение с прогоном пришлось бы сначала отличать
        // от собственного шума.
        List<FactRow> first = await ReadAsync(once, FactSet.OrderEvents, token).ConfigureAwait(true);
        List<FactRow> second = await ReadAsync(twice, FactSet.OrderEvents, token).ConfigureAwait(true);

        second.Select(Shape).ShouldBe(first.Select(Shape));
    }

    [Fact]
    public async Task LeavePartialObservationsOutOfTheReplayChain()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        using var original = new ReplayLake("original");
        using var replayed = new ReplayLake("replayed");

        var withPartial = new RecordedRun("live",
        [
            RecordedRun.Observation(0, [RecordedRun.Order(1, 100m)]),
            RecordedRun.Observation(5, [RecordedRun.Order(1, 100m)],
                CoverageOutcome.Partial, pagesReceived: 1, pagesExpected: 4),
            RecordedRun.Observation(10, [RecordedRun.Order(1, 110m, issuedMinute: 9)]),
        ]);

        _ = await original.RunAsync(withPartial, token).ConfigureAwait(true);

        IntakeReport report = await replayed
            .RunAsync(original.Replay(Window), token)
            .ConfigureAwait(true);

        // Частичное наблюдение в цепочку восстановления не входит: по нему неизвестно,
        // чего в стакане не было. Сверка ведётся с точностью до помеченных неполными.
        report.Written.ShouldBe(2);

        IReadOnlyList<CoverageEntry> coverage = await replayed.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        coverage.ShouldAllBe(static entry => entry.Outcome == CoverageOutcome.Success);
    }

    [Fact]
    public async Task CarryReplayAsItsOwnOriginInCoverage()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        using var original = new ReplayLake("original");
        using var replayed = new ReplayLake("replayed");

        _ = await original.RunAsync(Run(), token).ConfigureAwait(true);
        _ = await replayed.RunAsync(original.Replay(Window), token).ConfigureAwait(true);

        IReadOnlyList<CoverageEntry> coverage = await replayed.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        // Происхождение известно — но только из журнала покрытия. Сами факты о нём
        // молчат, и это то, что доказывает единство входа.
        coverage.ShouldAllBe(static entry => entry.Source == "replay");
        coverage.ShouldAllBe(static entry => entry.ObservationStep == TimeSpan.FromMinutes(5));
    }
}
