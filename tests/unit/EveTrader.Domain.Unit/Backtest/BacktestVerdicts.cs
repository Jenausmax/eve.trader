using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Domain.Unit.Backtest;

/// <summary>Сигналы и параметры для тестов бэктеста.</summary>
internal static class BacktestVerdicts
{
    public static readonly DateTimeOffset Decision = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);

    public static readonly StationTradingParameters Parameters = StationTradingParameters.Of(
        "jita",
        FeeSchedule.Of(brokerFeeRate: 0.01m, salesTaxRate: 0.02m, relistFeeRate: 0.001m),
        TimeSpan.FromHours(2),
        minNetMarginRate: 0.02m,
        minObservedTurnover: 1,
        competitorBandBasisPoints: 100,
        maxCompetitors: 5,
        maxRelistPressure: 20,
        expectedBuyRelists: 1,
        expectedSellRelists: 1);

    public static StationTradingVerdict Signal(DateTimeOffset? decision = null) =>
        new(
            StationTradingScope.Jita44,
            34,
            decision ?? Decision,
            Parameters.Name,
            ConsiderationOutcome.Signal,
            [],
            null,
            new SignalJustification(
                IskPrice.FromIsk(90m),
                IskPrice.FromIsk(100m),
                (decision ?? Decision).AddMinutes(-15),
                IskAmount.FromIsk(5.91m),
                5.91m / 90.99m,
                50d,
                40d,
                2d,
                3d,
                4d,
                6d,
                null,
                Parameters,
                []));

    public static StationTradingVerdict NotMet() =>
        Signal() with { Outcome = ConsiderationOutcome.ConditionsNotMet, Reasons = [VerdictReason.TooManyCompetitors] };

    public static StationTradingVerdict Unknown() =>
        Signal() with { Outcome = ConsiderationOutcome.InsufficientData, Reasons = [VerdictReason.NoQuote], Justification = null };

    public static OrderEvent Fill(bool isBuy, decimal price, DateTimeOffset at) =>
        new(
            OrderEventKind.ObservedFill,
            isBuy ? 3 : 1,
            34,
            StationTradingScope.Jita44.StationId,
            isBuy,
            IskPrice.FromIsk(price),
            IskPrice.FromIsk(price),
            VolumeRemain: 100,
            FilledVolume: 10,
            EventTime.At(at),
            at,
            Decision.ToUnixTimeSeconds(),
            DurationDays: 90,
            IsNpc: false,
            UndersampledStep: false);
}
