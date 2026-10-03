using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Перевод исходов рассмотрения в порцию фактов набора сигналов.
///
/// Пишутся все три исхода, а не только сигналы: «пара рассмотрена, сигнала нет» и
/// «данных не хватило» — тоже знание, и без них оценка качества считала бы знаменатель
/// по чужому правилу.
///
/// Время события строки — момент решения; момент знания — когда система исход
/// породила. Сигнал неизменяем: пересмотр другим набором параметров даёт новый ключ (имя
/// набора входит в ключ) и ложится рядом, а не поверх. Пороги и ставки пишутся в каждую
/// строку: прочитанный задним числом сигнал обязан быть ровно таким, каким его видел бы
/// оператор, вместе с обоснованием.
/// </summary>
public static class SignalFacts
{
    public const string StationId = "station_id";
    public const string TypeId = "type_id";
    public const string ParameterSet = "parameter_set";
    public const string Label = "label";
    public const string Outcome = "outcome";
    public const string Reasons = "reasons";
    public const string CoverageState = "coverage_state";
    public const string Justified = "justified";
    public const string BestBidCents = "best_bid_cents";
    public const string BestAskCents = "best_ask_cents";
    public const string QuoteObservedAt = "quote_observed_at_unix_ms";
    public const string NetMarginCents = "net_margin_cents";
    public const string NetMarginRate = "net_margin_rate";
    public const string BuyTurnover = "buy_turnover";
    public const string SellTurnover = "sell_turnover";
    public const string BuyCompetitors = "buy_competitors";
    public const string SellCompetitors = "sell_competitors";
    public const string BuyRelists = "buy_relists";
    public const string SellRelists = "sell_relists";
    public const string FilledShare = "filled_share";
    public const string BrokerFeeRate = "broker_fee_rate";
    public const string SalesTaxRate = "sales_tax_rate";
    public const string RelistFeeRate = "relist_fee_rate";
    public const string WindowSeconds = "window_seconds";
    public const string MinNetMarginRate = "min_net_margin_rate";
    public const string MinObservedTurnover = "min_observed_turnover";
    public const string BandBasisPoints = "band_bp";
    public const string MaxCompetitors = "max_competitors";
    public const string MaxRelistPressure = "max_relist_pressure";
    public const string ExpectedBuyRelists = "expected_buy_relists";
    public const string ExpectedSellRelists = "expected_sell_relists";
    public const string Series = "series";

    /// <summary>Источник в записи покрытия порции сигналов.</summary>
    public const string Source = "signals";

    /// <param name="region">Регион станции — партиция.</param>
    /// <param name="observation">Наблюдение, подтверждающее порцию.</param>
    /// <param name="partition">Суточная партиция — дата моментов решения порции.</param>
    /// <param name="parameters">Набор, которым порождены исходы: его пороги и ставки пишутся в строку.</param>
    /// <param name="verdicts">Исходы рассмотрения.</param>
    /// <param name="generatedAt">Когда система исходы породила — момент знания строк.</param>
    /// <param name="staticData">Версия статических данных.</param>
    public static FactBatch ToBatch(
        RegionId region,
        ObservationId observation,
        DateOnly partition,
        StationTradingParameters parameters,
        IReadOnlyList<StationTradingVerdict> verdicts,
        DateTimeOffset generatedAt,
        StaticDataVersion staticData)
    {
        if (verdicts.Any(verdict => verdict.ParameterSet != parameters.Name))
        {
            throw new ArgumentException("Порция сигналов принадлежит одному набору параметров", nameof(verdicts));
        }

        if (verdicts.Any(verdict => verdict.Decision > generatedAt))
        {
            throw new ArgumentException("Исход не может быть порождён раньше момента решения", nameof(generatedAt));
        }

        var envelopes = verdicts
            .Select(verdict => new FactEnvelope(
                verdict.FactKey, EventTime.At(verdict.Decision), generatedAt, observation, staticData))
            .ToList();

        return FactBatch.Of(
            FactSet.Signals,
            region,
            observation,
            partition,
            envelopes,
            [
                FactColumn.OfInt64(StationId, [.. verdicts.Select(static verdict => verdict.Scope.StationId)]),
                FactColumn.OfInt64(TypeId, [.. verdicts.Select(static verdict => (long)verdict.TypeId)]),
                FactColumn.OfString(ParameterSet, [.. verdicts.Select(static verdict => verdict.ParameterSet.Value)]),
                FactColumn.OfString(Label, [.. verdicts.Select(_ => parameters.Label)]),
                FactColumn.OfInt64(Outcome, [.. verdicts.Select(static verdict => (long)verdict.Outcome)]),
                FactColumn.OfString(Reasons, [.. verdicts.Select(static verdict => string.Join(',', verdict.Reasons))]),
                FactColumn.OfNullableInt64(CoverageState, [.. verdicts.Select(static verdict => (long?)verdict.Coverage)]),
                FactColumn.OfInt64(Justified, [.. verdicts.Select(static verdict => verdict.Justification is null ? 0L : 1L)]),
                FactColumn.OfNullableInt64(BestBidCents, [.. verdicts.Select(static verdict => verdict.Justification?.BestBid.Cents)]),
                FactColumn.OfNullableInt64(BestAskCents, [.. verdicts.Select(static verdict => verdict.Justification?.BestAsk.Cents)]),
                FactColumn.OfNullableInt64(QuoteObservedAt, [.. verdicts.Select(static verdict => verdict.Justification?.QuoteObservedAt.ToUnixTimeMilliseconds())]),
                FactColumn.OfNullableInt64(NetMarginCents, [.. verdicts.Select(static verdict => verdict.Justification?.NetMargin.Cents)]),
                FactColumn.OfString(NetMarginRate, [.. verdicts.Select(static verdict => Decimal(verdict.Justification?.NetMarginRate))]),
                FactColumn.OfDouble(BuyTurnover, [.. verdicts.Select(static verdict => verdict.Justification?.BuyTurnover ?? 0d)]),
                FactColumn.OfDouble(SellTurnover, [.. verdicts.Select(static verdict => verdict.Justification?.SellTurnover ?? 0d)]),
                FactColumn.OfDouble(BuyCompetitors, [.. verdicts.Select(static verdict => verdict.Justification?.BuyCompetitors ?? 0d)]),
                FactColumn.OfDouble(SellCompetitors, [.. verdicts.Select(static verdict => verdict.Justification?.SellCompetitors ?? 0d)]),
                FactColumn.OfDouble(BuyRelists, [.. verdicts.Select(static verdict => verdict.Justification?.BuyRelists ?? 0d)]),
                FactColumn.OfDouble(SellRelists, [.. verdicts.Select(static verdict => verdict.Justification?.SellRelists ?? 0d)]),
                FactColumn.OfString(FilledShare, [.. verdicts.Select(static verdict => Double(verdict.Justification?.FilledShare))]),
                FactColumn.OfString(BrokerFeeRate, [.. verdicts.Select(_ => Decimal(parameters.Fees.BrokerFeeRate))]),
                FactColumn.OfString(SalesTaxRate, [.. verdicts.Select(_ => Decimal(parameters.Fees.SalesTaxRate))]),
                FactColumn.OfString(RelistFeeRate, [.. verdicts.Select(_ => Decimal(parameters.Fees.RelistFeeRate))]),
                FactColumn.OfInt64(WindowSeconds, [.. verdicts.Select(_ => (long)parameters.Window.TotalSeconds)]),
                FactColumn.OfString(MinNetMarginRate, [.. verdicts.Select(_ => Decimal(parameters.MinNetMarginRate))]),
                FactColumn.OfInt64(MinObservedTurnover, [.. verdicts.Select(_ => parameters.MinObservedTurnover)]),
                FactColumn.OfInt64(BandBasisPoints, [.. verdicts.Select(_ => (long)parameters.CompetitorBandBasisPoints)]),
                FactColumn.OfInt64(MaxCompetitors, [.. verdicts.Select(_ => (long)parameters.MaxCompetitors)]),
                FactColumn.OfInt64(MaxRelistPressure, [.. verdicts.Select(_ => (long)parameters.MaxRelistPressure)]),
                FactColumn.OfInt64(ExpectedBuyRelists, [.. verdicts.Select(_ => (long)parameters.ExpectedBuyRelists)]),
                FactColumn.OfInt64(ExpectedSellRelists, [.. verdicts.Select(_ => (long)parameters.ExpectedSellRelists)]),
                FactColumn.OfString(Series, [.. verdicts.Select(static verdict => string.Join(';', verdict.Justification?.Series ?? []))]),
            ]);
    }

    /// <summary>
    /// Наблюдение, подтверждающее порцию сигналов: регион, набор и отрезок моментов
    /// решения. Повторное порождение того же отрезка тем же набором — та же порция, и
    /// прежний сигнал не переписывается: сигнал неизменяем.
    /// </summary>
    public static ObservationId ObservationFor(
        RegionId region,
        ParameterSetName parameterSet,
        DateTimeOffset firstDecision,
        DateTimeOffset lastDecision) =>
        ObservationId.From(string.Create(
            CultureInfo.InvariantCulture,
            $"signals-{region.Value}-{parameterSet.Value}-{firstDecision:yyyyMMddTHHmmss}-{lastDecision:yyyyMMddTHHmmss}"));

    /// <summary>Десятичное строкой без потерь: ставка 0.015, записанная вещественным, вернулась бы 0.0149999.</summary>
    public static string Decimal(decimal? value) =>
        value is { } present ? present.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Вещественное строкой туда и обратно без потерь; пусто — значения нет.</summary>
    public static string Double(double? value) =>
        value is { } present ? present.ToString("R", CultureInfo.InvariantCulture) : string.Empty;
}
