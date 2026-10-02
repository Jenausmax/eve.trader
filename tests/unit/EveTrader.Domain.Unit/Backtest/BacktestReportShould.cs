using EveTrader.Domain.Backtest;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Domain.Unit.Backtest;

/// <summary>
/// Сценарии <c>market-signals/backtest</c> §«Отчёт называет качество и базовую линию»:
/// метрика без базовой линии запрещена, доля неизвестных исходов называется всегда.
/// </summary>
public sealed class BacktestReportShould
{
    private static readonly TimeRange Interval =
        TimeRange.Between(BacktestVerdicts.Decision.AddHours(-3), BacktestVerdicts.Decision.AddHours(3));

    [Fact]
    public void NameQualityAgainstTheDoNotTradeBaseline()
    {
        // Два успеха по 5.91 и одна неудача −1.90: сумма 9.92, среднее 3.3067.
        var report = BacktestReport.Of(
            Setup(),
            decisions: 12,
            [BacktestVerdicts.Signal(), BacktestVerdicts.Signal(), BacktestVerdicts.Signal(), BacktestVerdicts.NotMet(), BacktestVerdicts.Unknown()],
            [Outcome(SignalOutcome.Success, 591), Outcome(SignalOutcome.Success, 591), Outcome(SignalOutcome.Failure, -190)]);

        report.Signals.ShouldBe(3);
        report.ConditionsNotMet.ShouldBe(1);
        report.InsufficientData.ShouldBe(1);
        report.Determined.ShouldBe(3);
        report.HitRate.ShouldBe(2m / 3m);
        report.TotalRealizedCents.ShouldBe(992L);
        report.MeanRealizedCents.ShouldBe(992m / 3m);
        report.DeviationRealizedCents.ShouldBe(368.167, 0.001);

        // Базовая линия — «не торговать»: ноль. Правило обязано быть лучше неё.
        report.BaselineTotalCents.ShouldBe(0L);
        report.BeatsBaseline.ShouldBeTrue();
        report.IsConclusive.ShouldBeTrue();
    }

    [Fact]
    public void ReportALosingRuleAsLosing()
    {
        var report = BacktestReport.Of(
            Setup(), 12, [BacktestVerdicts.Signal()], [Outcome(SignalOutcome.Failure, -190)]);

        // Убыточный результат называется как есть: хуже базовой линии.
        report.TotalRealizedCents.ShouldBe(-190L);
        report.BeatsBaseline.ShouldBeFalse();
    }

    [Fact]
    public void MarkARunOfMostlyUnknownOutcomesInconclusive()
    {
        var report = BacktestReport.Of(
            Setup(maxUnknownShare: 0.5m),
            12,
            [BacktestVerdicts.Signal(), BacktestVerdicts.Signal(), BacktestVerdicts.Signal()],
            [Outcome(SignalOutcome.Success, 591), Outcome(SignalOutcome.Unknown, null), Outcome(SignalOutcome.Unknown, null)]);

        // Две трети исходов неизвестны при пороге в половину — прогон недоказателен, и
        // доля неизвестных названа.
        report.UnknownShare.ShouldBe(2m / 3m);
        report.IsConclusive.ShouldBeFalse();

        // Неизвестные в метрики не входят.
        report.Determined.ShouldBe(1);
        report.TotalRealizedCents.ShouldBe(591L);
    }

    [Fact]
    public void NotCallARunWithoutSignalsConclusive()
    {
        var report = BacktestReport.Of(Setup(), 12, [BacktestVerdicts.NotMet()], []);

        report.Signals.ShouldBe(0);
        report.IsConclusive.ShouldBeFalse();
        report.BeatsBaseline.ShouldBeFalse();
    }

    [Fact]
    public void ComputeTheSameReportWhateverTheOrderOfOutcomes()
    {
        AssessedSignal[] outcomes =
            [Outcome(SignalOutcome.Success, 591), Outcome(SignalOutcome.Failure, -190), Outcome(SignalOutcome.Success, 450)];

        var first = BacktestReport.Of(Setup(), 12, [BacktestVerdicts.Signal()], outcomes);
        var second = BacktestReport.Of(Setup(), 12, [BacktestVerdicts.Signal()], [.. outcomes.Reverse()]);

        second.ShouldBe(first);
    }

    [Fact]
    public void RefuseToCompareReportsOfDifferentIntervals()
    {
        var first = BacktestReport.Of(Setup(), 12, [BacktestVerdicts.Signal()], [Outcome(SignalOutcome.Success, 591)]);
        BacktestReport other = first with { Interval = TimeRange.Between(Interval.From, Interval.To.AddDays(1)) };

        var comparison = new BacktestComparison(first, other);

        // Разница результатов на разных интервалах говорит о рынках, а не о правилах.
        comparison.Comparable.ShouldBeFalse();
        comparison.Leader.ShouldBeNull();
    }

    private static BacktestSetup Setup(decimal maxUnknownShare = 0.5m) =>
        new(
            BacktestVerdicts.Parameters,
            StationTradingScope.Jita44,
            Interval,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
            maxUnknownShare,
            StaticDataVersion.From("sde-test"),
            BacktestVerdicts.Decision.AddDays(1));

    private static AssessedSignal Outcome(SignalOutcome outcome, long? cents) =>
        new(
            BacktestVerdicts.Signal(),
            TimeRange.Between(BacktestVerdicts.Decision, BacktestVerdicts.Decision.AddHours(1)),
            outcome,
            cents is { } value ? IskAmount.FromCents(value) : null);
}
