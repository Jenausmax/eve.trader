using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Domain.Unit.Signals;

/// <summary>
/// Правило станционной торговли: сценарии <c>market-signals/station-trading</c>
/// §«Маржа считается после комиссий», §«Сигнал несёт обоснование», §«Сигнал не
/// порождается на недостаточных данных» — и каждое условие по отдельности.
/// </summary>
public sealed class StationTradingRuleShould
{
    private const int Tritanium = 34;

    private static readonly DateTimeOffset Decision = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(30);

    private static readonly StationTradingParameters Parameters = StationTradingParameters.Of(
        "jita",
        FeeSchedule.Of(brokerFeeRate: 0.01m, salesTaxRate: 0.02m, relistFeeRate: 0.001m),
        TimeSpan.FromHours(2),
        minNetMarginRate: 0.02m,
        minObservedTurnover: 10,
        competitorBandBasisPoints: 100,
        maxCompetitors: 5,
        maxRelistPressure: 20,
        expectedBuyRelists: 1,
        expectedSellRelists: 1);

    [Fact]
    public void EmitASignalWhenEveryConditionHolds()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input());

        verdict.Outcome.ShouldBe(ConsiderationOutcome.Signal);
        verdict.Reasons.ShouldBeEmpty();
        verdict.ParameterSet.ShouldBe(Parameters.Name);
    }

    [Fact]
    public void CountTheMarginAfterFeesAndCarryTheRatesInTheSignal()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input());

        // Продажа 100 после брокерской 1 %, налога 2 % и одной перестановки 0.1 % — 96.90;
        // покупка 90 с брокерской и перестановкой — 90.99. Маржа 5.91, а не валовые 10.
        SignalJustification justification = verdict.Justification.ShouldNotBeNull();
        justification.NetMargin.ShouldBe(IskAmount.FromIsk(5.91m));
        justification.NetMarginRate.ShouldBe(5.91m / 90.99m);

        // Ставки, по которым посчитана маржа, доступны в самом сигнале.
        justification.Parameters.Fees.ShouldBe(Parameters.Fees);
    }

    [Fact]
    public void GiveNoSignalWhenFeesEatThePositiveGrossSpread()
    {
        // Валовая разница 3 ISK положительна, после комиссий — минус.
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(bid: 97m, ask: 100m));

        verdict.Outcome.ShouldBe(ConsiderationOutcome.ConditionsNotMet);
        verdict.Reasons.ShouldBe([VerdictReason.MarginNotPositive]);
        verdict.Justification.ShouldNotBeNull().NetMargin.IsPositive.ShouldBeFalse();
    }

    [Fact]
    public void GiveNoSignalBelowTheMarginThreshold()
    {
        // Маржа после комиссий положительна (≈ 1.4 %), но ниже порога в 2 %.
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(bid: 95m, ask: 100m));

        verdict.Reasons.ShouldBe([VerdictReason.MarginBelowThreshold]);
    }

    [Fact]
    public void GiveNoSignalWhenEitherSideTurnsOverTooSlowly()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(sellTurnover: 5d));

        verdict.Reasons.ShouldBe([VerdictReason.TurnoverBelowThreshold]);
    }

    [Fact]
    public void ReadAMissingTurnoverPointInAnAdmittedWindowAsNoFills()
    {
        // Окно допущено, а точки оборота по продаже нет: исполнений не было, и это факт о
        // рынке — «знаем и не советуем», а не «не знаем».
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(sellTurnover: null));

        verdict.Outcome.ShouldBe(ConsiderationOutcome.ConditionsNotMet);
        verdict.Reasons.ShouldBe([VerdictReason.TurnoverBelowThreshold]);
        verdict.Justification.ShouldNotBeNull().SellTurnover.ShouldBe(0d);
    }

    [Fact]
    public void GiveNoSignalWhenTheBandIsCrowded()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(sellCompetitors: 7d));

        verdict.Reasons.ShouldBe([VerdictReason.TooManyCompetitors]);
    }

    [Fact]
    public void GiveNoSignalUnderHeavyRelistPressure()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(buyRelists: 25d));

        verdict.Reasons.ShouldBe([VerdictReason.RelistPressureTooHigh]);
    }

    [Fact]
    public void NameEveryFailedConditionAtOnce()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(
            Parameters, Input(bid: 97m, sellTurnover: 1d, buyCompetitors: 9d, sellRelists: 30d));

        verdict.Reasons.ShouldBe(
        [
            VerdictReason.MarginNotPositive,
            VerdictReason.TurnoverBelowThreshold,
            VerdictReason.TooManyCompetitors,
            VerdictReason.RelistPressureTooHigh,
        ]);
    }

    [Fact]
    public void CarryFeatureValuesAndThresholdsAsJustification()
    {
        SignalJustification justification = StationTradingRule.Evaluate(Parameters, Input()).Justification.ShouldNotBeNull();

        justification.BestBid.ShouldBe(IskPrice.FromIsk(90m));
        justification.BestAsk.ShouldBe(IskPrice.FromIsk(100m));
        justification.BuyTurnover.ShouldBe(50d);
        justification.SellTurnover.ShouldBe(40d);
        justification.BuyCompetitors.ShouldBe(2d);
        justification.SellCompetitors.ShouldBe(3d);
        justification.BuyRelists.ShouldBe(4d);
        justification.SellRelists.ShouldBe(6d);
        justification.FilledShare.ShouldBe(0.5d);
        justification.Parameters.MinNetMarginRate.ShouldBe(0.02m);
        justification.Parameters.MaxCompetitors.ShouldBe(5);
        justification.Series.ShouldContain(StationTradingSeries.Turnover(Parameters.Window, Step).Key);
    }

    [Fact]
    public void KnowNothingWhenTheWindowWasNotObserved()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(
            Parameters, Input(admission: SeriesAdmission.Refused, coverage: CoverageState.NotObserved));

        verdict.Outcome.ShouldBe(ConsiderationOutcome.InsufficientData);
        verdict.Reasons.ShouldBe([VerdictReason.WindowRefused]);

        // Причина доступна оператору: состояние покрытия окна.
        verdict.Coverage.ShouldBe(CoverageState.NotObserved);
        verdict.Justification.ShouldBeNull();
    }

    [Fact]
    public void KnowNothingWhenTheFeatureIsMarkedIncomplete()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(
            Parameters, Input(admission: SeriesAdmission.AdmittedIncomplete));

        verdict.Outcome.ShouldBe(ConsiderationOutcome.InsufficientData);
        verdict.Reasons.ShouldBe([VerdictReason.WindowIncomplete]);
    }

    [Fact]
    public void KnowNothingWhenTheSeriesWasNeverMaterialized()
    {
        StationTradingInput input = Input();

        StationTradingVerdict verdict = StationTradingRule.Evaluate(
            Parameters, input with { Series = new SeriesComputed([], input.Series.Points) });

        verdict.Outcome.ShouldBe(ConsiderationOutcome.InsufficientData);
        verdict.Reasons.ShouldBe([VerdictReason.SeriesNotMaterialized]);
    }

    [Fact]
    public void KnowNothingWithoutAQuote()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input() with { Quote = null });

        verdict.Outcome.ShouldBe(ConsiderationOutcome.InsufficientData);
        verdict.Reasons.ShouldBe([VerdictReason.NoQuote]);
    }

    [Fact]
    public void TellConditionsNotMetApartFromInsufficientData()
    {
        // Данных хватило, пороги не пройдены: записано, что пара рассмотрена и сигнала нет.
        StationTradingVerdict considered = StationTradingRule.Evaluate(Parameters, Input(sellCompetitors: 7d));
        StationTradingVerdict unknown = StationTradingRule.Evaluate(Parameters, Input() with { Quote = null });

        considered.Outcome.ShouldBe(ConsiderationOutcome.ConditionsNotMet);
        unknown.Outcome.ShouldBe(ConsiderationOutcome.InsufficientData);
        considered.Outcome.ShouldNotBe(unknown.Outcome);
    }

    [Fact]
    public void ReportAOneSidedBookAsConsideredAndNotAdvised()
    {
        StationTradingInput input = Input();

        StationTradingVerdict verdict = StationTradingRule.Evaluate(
            Parameters, input with { Quote = input.Quote! with { BestBid = null } });

        verdict.Outcome.ShouldBe(ConsiderationOutcome.ConditionsNotMet);
        verdict.Reasons.ShouldBe([VerdictReason.OneSidedBook]);
    }

    [Fact]
    public void KnowNothingWhereTheCompetitorDepthWasNotCounted()
    {
        StationTradingVerdict verdict = StationTradingRule.Evaluate(Parameters, Input(buyCompetitors: null));

        verdict.Outcome.ShouldBe(ConsiderationOutcome.InsufficientData);
        verdict.Reasons.ShouldBe([VerdictReason.CompetitorDepthUnavailable]);
    }

    [Fact]
    public void RefuseAQuoteTakenAfterTheDecision()
    {
        StationTradingInput input = Input();

        _ = Should.Throw<ArgumentException>(() => StationTradingRule.Evaluate(
            Parameters, input with { Quote = input.Quote! with { ObservedAt = Decision.AddMinutes(1) } }));
    }

    [Fact]
    public void KeepVerdictsOfTwoParameterSetsDistinguishable()
    {
        var stricter = StationTradingParameters.Of(
            "jita", Parameters.Fees, Parameters.Window, 0.08m, 10, 100, 5, 20, 1, 1);

        StationTradingVerdict first = StationTradingRule.Evaluate(Parameters, Input());
        StationTradingVerdict second = StationTradingRule.Evaluate(stricter, Input());

        // Два набора на одном интервале: исходы несут разные имена и разные ключи фактов.
        first.ParameterSet.ShouldNotBe(second.ParameterSet);
        first.FactKey.ShouldNotBe(second.FactKey);
        second.Reasons.ShouldBe([VerdictReason.MarginBelowThreshold]);
    }

    private static StationTradingInput Input(
        decimal bid = 90m,
        decimal ask = 100m,
        double? buyTurnover = 50d,
        double? sellTurnover = 40d,
        double? buyCompetitors = 2d,
        double? sellCompetitors = 3d,
        double? buyRelists = 4d,
        double? sellRelists = 6d,
        SeriesAdmission admission = SeriesAdmission.Admitted,
        CoverageState coverage = CoverageState.Observed)
    {
        StationTradingScope scope = StationTradingScope.Jita44;
        var range = TimeRange.Between(Decision - Parameters.Window, Decision);
        var window = new SeriesWindow(range, admission, coverage, admission == SeriesAdmission.AdmittedIncomplete ? 1 : 0);

        SeriesDefinition turnover = StationTradingSeries.Turnover(Parameters.Window, Step);
        SeriesDefinition relists = StationTradingSeries.RelistPressure(Parameters.Window, Step);
        SeriesDefinition depth = StationTradingSeries.CompetitorDepth(Parameters.Window, Step, Parameters.CompetitorBandBasisPoints);
        SeriesDefinition share = StationTradingSeries.FilledShare(Parameters.Window, Step);

        var points = new List<SeriesPoint>();

        void Add(SeriesDefinition definition, SeriesSide side, double? value)
        {
            if (value is { } present && admission != SeriesAdmission.Refused)
            {
                points.Add(new SeriesPoint(
                    definition, scope.Region, Tritanium, scope.StationId, side, range, present,
                    admission == SeriesAdmission.AdmittedIncomplete));
            }
        }

        Add(turnover, SeriesSide.Buy, buyTurnover);
        Add(turnover, SeriesSide.Sell, sellTurnover);
        Add(depth, SeriesSide.Buy, buyCompetitors);
        Add(depth, SeriesSide.Sell, sellCompetitors);
        Add(relists, SeriesSide.Buy, buyRelists);
        Add(relists, SeriesSide.Sell, sellRelists);
        Add(share, SeriesSide.Both, 0.5d);

        return new StationTradingInput(
            scope,
            Tritanium,
            Decision,
            Step,
            new BookFeatures(
                Tritanium,
                scope.StationId,
                IskPrice.FromIsk(bid),
                IskPrice.FromIsk(ask),
                BuyOrders: 10,
                SellOrders: 12,
                BuyDepth: [100, 500],
                SellDepth: [120, 600],
                BuyOrdersWithin: [2, 6],
                SellOrdersWithin: [3, 7],
                ObservedAt: Decision.AddMinutes(-15),
                Observation: ObservationId.From("obs-1445"),
                Incomplete: false),
            new SeriesComputed(
                [.. ((SeriesDefinition[])[turnover, relists, depth, share])
                    .Select(definition => new SeriesWindowVerdict(definition, scope.Region, window))],
                points));
    }
}
