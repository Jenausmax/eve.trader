using EveTrader.Domain.Backtest;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Domain.Unit.Backtest;

/// <summary>
/// Сценарии <c>market-signals/backtest</c> §«Исход сигнала определяется наблюдёнными
/// фактами»: исход называет горизонт, а неподтверждённый наблюдением — неизвестен.
/// </summary>
public sealed class OutcomeAssessmentShould
{
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(1);

    private static readonly DateTimeOffset Decision = BacktestVerdicts.Decision;

    [Fact]
    public void CloseTheRoundTripWhenBothSidesFillAtTheSignalPrices()
    {
        AssessedSignal assessed = OutcomeAssessment.Assess(
            BacktestVerdicts.Signal(),
            Horizon,
            Range(),
            Observed(),
            [
                BacktestVerdicts.Fill(isBuy: true, price: 90m, Decision.AddMinutes(30)),
                BacktestVerdicts.Fill(isBuy: false, price: 100m, Decision.AddMinutes(45)),
            ]);

        assessed.Outcome.ShouldBe(SignalOutcome.Success);
        assessed.Realized.ShouldBe(IskAmount.FromIsk(5.91m));

        // Горизонт назван.
        assessed.Horizon.ShouldBe(TimeRange.Between(Decision, Decision + Horizon));
    }

    [Fact]
    public void FailWhenOneSideNeverFillsInAnObservedHorizon()
    {
        AssessedSignal assessed = OutcomeAssessment.Assess(
            BacktestVerdicts.Signal(),
            Horizon,
            Range(),
            Observed(),
            [BacktestVerdicts.Fill(isBuy: true, price: 90m, Decision.AddMinutes(30))]);

        // Ордера пришлось бы снять: потеряна брокерская комиссия за обе стороны,
        // 1 % от 90 и от 100.
        assessed.Outcome.ShouldBe(SignalOutcome.Failure);
        assessed.Realized.ShouldBe(IskAmount.FromIsk(-1.90m));
    }

    [Fact]
    public void NotCountFillsBelowTheSignalLevel()
    {
        // Продажа исполнилась по 101 — выше цены сигнала: это не наш уровень, и наш ордер
        // по 100 так не исполнился бы раньше чужого.
        AssessedSignal assessed = OutcomeAssessment.Assess(
            BacktestVerdicts.Signal(),
            Horizon,
            Range(),
            Observed(),
            [
                BacktestVerdicts.Fill(isBuy: true, price: 90m, Decision.AddMinutes(30)),
                BacktestVerdicts.Fill(isBuy: false, price: 101m, Decision.AddMinutes(45)),
            ]);

        assessed.Outcome.ShouldBe(SignalOutcome.Failure);
    }

    [Fact]
    public void LeaveTheOutcomeUnknownWhenTheHorizonWasNotFullyObserved()
    {
        AssessedSignal assessed = OutcomeAssessment.Assess(
            BacktestVerdicts.Signal(),
            Horizon,
            TimeRange.Between(Decision, Decision + Horizon),
            new SeriesWindow(TimeRange.Between(Decision, Decision + Horizon), SeriesAdmission.Refused, CoverageState.NotObserved, 0),
            [
                BacktestVerdicts.Fill(isBuy: true, price: 90m, Decision.AddMinutes(30)),
                BacktestVerdicts.Fill(isBuy: false, price: 100m, Decision.AddMinutes(45)),
            ]);

        // Ни в успех, ни в неудачу: результата у неизвестного исхода нет.
        assessed.Outcome.ShouldBe(SignalOutcome.Unknown);
        assessed.Realized.ShouldBeNull();
    }

    [Fact]
    public void IgnoreFillsOutsideTheHorizon()
    {
        AssessedSignal assessed = OutcomeAssessment.Assess(
            BacktestVerdicts.Signal(),
            Horizon,
            Range(),
            Observed(),
            [
                BacktestVerdicts.Fill(isBuy: true, price: 90m, Decision.AddMinutes(-5)),
                BacktestVerdicts.Fill(isBuy: false, price: 100m, Decision.AddHours(2)),
            ]);

        assessed.Outcome.ShouldBe(SignalOutcome.Failure);
    }

    [Fact]
    public void CloseTheHorizonWithTheFirstSnapshotPastItsEnd()
    {
        // Снимки через 31 минуту при объявленных 30: горизонт в час закрывает снимок
        // на 1:02 — первый не раньше конца горизонта и в пределах полутора шагов.
        IReadOnlyList<CoverageEntry> chain = Chain(TimeSpan.FromMinutes(31), slots: 4);

        OutcomeAssessment.Observed(Decision, Horizon, chain)
            .ShouldBe(TimeRange.Between(Decision, Decision.AddMinutes(62)));
    }

    [Fact]
    public void LeaveTheHorizonOpenWhenNoSnapshotClosesIt()
    {
        // Последний снимок — на 0:31: горизонт в час закрыть нечем.
        IReadOnlyList<CoverageEntry> chain = Chain(TimeSpan.FromMinutes(31), slots: 1);

        OutcomeAssessment.Observed(Decision, Horizon, chain).ShouldBeNull();
    }

    [Fact]
    public void RefuseToAssessSomethingThatIsNotASignal() =>
        Should.Throw<ArgumentException>(static () =>
            OutcomeAssessment.Assess(BacktestVerdicts.NotMet(), Horizon, Range(), Observed(), []));

    private static IReadOnlyList<CoverageEntry> Chain(TimeSpan spacing, int slots) =>
        SeriesCoverage.Chain(
            [
                .. Enumerable.Range(0, slots + 1).Select(slot => CoverageEntries.Success(
                    ObservationId.From($"obs-{slot:00}"),
                    RegionId.From(10000002),
                    TimeRange.Between(Decision + (spacing * slot) - TimeSpan.FromSeconds(20), Decision + (spacing * slot)),
                    pages: 1,
                    orderCount: 10,
                    source: "everef-orders",
                    observationStep: TimeSpan.FromMinutes(30),
                    knownAt: Decision + (spacing * slot))),
            ],
            new HashSet<ObservationId> { ObservationId.From("obs-00") });

    private static TimeRange Range() => TimeRange.Between(Decision, Decision + Horizon);

    private static SeriesWindow Observed() =>
        new(TimeRange.Between(Decision, Decision + Horizon), SeriesAdmission.Admitted, CoverageState.Observed, 0);
}
