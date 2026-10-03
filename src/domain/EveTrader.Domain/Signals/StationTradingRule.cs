using EveTrader.Domain.Coverage;
using EveTrader.Domain.Series;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Правило станционной торговли: стоит ли встать в стакан пары с покупкой по лучшей
/// цене покупки и продажей по лучшей цене продажи.
///
/// Одно на бой и на бэктест. Отдельного «пересчитать на истории» нет: прогон подменяет
/// момент решения и горизонт чтения, а правило исполняется то же самое — иначе
/// совпадение бэктеста с боем доказывало бы только то, что обе реализации написаны
/// одной рукой.
///
/// Порядок разбора не произволен. Сначала — хватило ли данных: признак, не порождённый
/// из-за пробела или помеченный неполным, сигнала не даёт, и это «не знаем», а не «не
/// советуем». Потом — условия, все сразу: оператору нужны все причины молчания, а не
/// первая попавшаяся.
/// </summary>
public static class StationTradingRule
{
    public static StationTradingVerdict Evaluate(StationTradingParameters parameters, StationTradingInput input)
    {
        if (input.Quote is { } early && early.ObservedAt > input.Decision)
        {
            throw new ArgumentException(
                "Снимок стакана снят позже момента решения: правило не заглядывает вперёд", nameof(input));
        }

        TimeSpan window = parameters.Window;
        SeriesDefinition turnover = StationTradingSeries.Turnover(window, input.Step);
        SeriesDefinition relists = StationTradingSeries.RelistPressure(window, input.Step);
        SeriesDefinition depth = StationTradingSeries.CompetitorDepth(window, input.Step, parameters.CompetitorBandBasisPoints);
        SeriesDefinition share = StationTradingSeries.FilledShare(window, input.Step);

        var missing = new List<VerdictReason>();
        CoverageState? coverage = null;

        foreach (SeriesDefinition required in (SeriesDefinition[])[turnover, relists, depth])
        {
            SeriesWindowVerdict? verdict = input.Series.Windows
                .FirstOrDefault(candidate => candidate.Definition == required && candidate.Window.Range.To == input.Decision);

            if (verdict is null)
            {
                missing.Add(VerdictReason.SeriesNotMaterialized);
            }
            else if (!verdict.Window.IsAdmitted)
            {
                missing.Add(VerdictReason.WindowRefused);
                coverage = RuleSteps.Worst(coverage, verdict.Window.Coverage);
            }
            else if (verdict.Window.IsIncomplete)
            {
                missing.Add(VerdictReason.WindowIncomplete);
            }
        }

        if (input.Quote is not { } quote)
        {
            missing.Add(VerdictReason.NoQuote);
        }
        else if (quote.Incomplete)
        {
            missing.Add(VerdictReason.QuoteIncomplete);
        }

        if (missing.Count > 0 || input.Quote is not { } snapshot)
        {
            return RuleSteps.Verdict(input, parameters, ConsiderationOutcome.InsufficientData, missing, coverage, null);
        }

        if (snapshot.BestBid is not { } bid || snapshot.BestAsk is not { } ask)
        {
            // Сторона отсутствует, и это наблюдено: снимок полный, окно допущено.
            // Знаем — и не советуем: маржу считать не от чего.
            return RuleSteps.Verdict(
                input, parameters, ConsiderationOutcome.ConditionsNotMet, [VerdictReason.OneSidedBook], null, null);
        }

        IReadOnlyList<SeriesPoint> points = [.. input.Series.Points.Where(point => point.TypeId == input.TypeId
            && point.LocationId == input.Scope.StationId
            && point.Window.To == input.Decision)];

        // В допущенном окне отсутствие точки оборота или перестановок — не пробел, а факт:
        // исполнений и перестановок не было. Глубина же без точки означает, что считать
        // её было не из чего, — и это уже «не знаем».
        if (RuleSteps.Value(points, depth, SeriesSide.Buy) is not { } buyDepth
            || RuleSteps.Value(points, depth, SeriesSide.Sell) is not { } sellDepth)
        {
            return RuleSteps.Verdict(
                input, parameters, ConsiderationOutcome.InsufficientData, [VerdictReason.CompetitorDepthUnavailable], null, null);
        }

        var buyTurnover = RuleSteps.Value(points, turnover, SeriesSide.Buy) ?? 0d;
        var sellTurnover = RuleSteps.Value(points, turnover, SeriesSide.Sell) ?? 0d;
        var buyRelists = RuleSteps.Value(points, relists, SeriesSide.Buy) ?? 0d;
        var sellRelists = RuleSteps.Value(points, relists, SeriesSide.Sell) ?? 0d;

        FeeSchedule fees = parameters.Fees;
        IskAmount margin = fees.NetMargin(bid, ask, parameters.ExpectedBuyRelists, parameters.ExpectedSellRelists);
        IskAmount cost = fees.BuyCost(bid, parameters.ExpectedBuyRelists);
        var rate = cost.Cents > 0 ? margin.ToIsk() / cost.ToIsk() : 0m;

        var reasons = new List<VerdictReason>();

        if (!margin.IsPositive)
        {
            reasons.Add(VerdictReason.MarginNotPositive);
        }
        else if (rate < parameters.MinNetMarginRate)
        {
            reasons.Add(VerdictReason.MarginBelowThreshold);
        }

        if (Math.Min(buyTurnover, sellTurnover) < parameters.MinObservedTurnover)
        {
            reasons.Add(VerdictReason.TurnoverBelowThreshold);
        }

        if (Math.Max(buyDepth, sellDepth) > parameters.MaxCompetitors)
        {
            reasons.Add(VerdictReason.TooManyCompetitors);
        }

        if (Math.Max(buyRelists, sellRelists) > parameters.MaxRelistPressure)
        {
            reasons.Add(VerdictReason.RelistPressureTooHigh);
        }

        var justification = new SignalJustification(
            bid,
            ask,
            snapshot.ObservedAt,
            margin,
            rate,
            buyTurnover,
            sellTurnover,
            buyDepth,
            sellDepth,
            buyRelists,
            sellRelists,
            RuleSteps.Value(points, share, SeriesSide.Both),
            parameters,
            [turnover.Key, relists.Key, depth.Key, share.Key]);

        return RuleSteps.Verdict(
            input,
            parameters,
            reasons.Count == 0 ? ConsiderationOutcome.Signal : ConsiderationOutcome.ConditionsNotMet,
            reasons,
            null,
            justification);
    }
}

file static class RuleSteps
{
    public static double? Value(IReadOnlyList<SeriesPoint> points, SeriesDefinition definition, SeriesSide side) =>
        points.FirstOrDefault(point => point.Definition == definition && point.Side == side)?.Value;

    /// <summary>Худшее из состояний покрытия: утраченное хуже восполнимого.</summary>
    public static CoverageState Worst(CoverageState? current, CoverageState next) =>
        current is { } known && known > next ? known : next;

    public static StationTradingVerdict Verdict(
        StationTradingInput input,
        StationTradingParameters parameters,
        ConsiderationOutcome outcome,
        IReadOnlyList<VerdictReason> reasons,
        CoverageState? coverage,
        SignalJustification? justification) =>
        new(
            input.Scope,
            input.TypeId,
            input.Decision,
            parameters.Name,
            outcome,
            [.. reasons.Distinct()],
            coverage,
            justification);
}
