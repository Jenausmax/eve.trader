using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Application.Series;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Signals;

/// <summary>
/// Чтение записанных исходов обратно — на момент знания.
///
/// Ради этого сигнал и пишется фактом: его можно прочитать задним числом ровно таким,
/// каким его видел бы оператор, вместе с обоснованием, порогами и ставками, и сверить
/// с тем, что случилось дальше.
/// </summary>
public sealed class SignalFactReader(IFactRowReader rows)
{
    /// <param name="scope">Станция.</param>
    /// <param name="decisions">
    /// Интервал моментов решения, включая правую границу — как у сетки концов окон,
    /// которая правую границу интервала включает.
    /// </param>
    /// <param name="asOf">Момент знания; <see langword="null" /> — последняя известная версия.</param>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task<IReadOnlyList<StationTradingVerdict>> ReadAsync(
        StationTradingScope scope,
        TimeRange decisions,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        var verdicts = new List<StationTradingVerdict>();

        await foreach (FactRow row in rows
            .ReadAsync(FactSet.Signals, decisions, asOf, cancellationToken)
            .ConfigureAwait(false))
        {
            if (row.Region == scope.Region
                && SeriesFactRows.Int64Of(row, SignalFacts.StationId) == scope.StationId
                && row.Envelope.EventTime.From >= decisions.From
                && row.Envelope.EventTime.From <= decisions.To)
            {
                verdicts.Add(SignalRows.Verdict(row, scope));
            }
        }

        return
        [
            .. verdicts
                .OrderBy(static verdict => verdict.Decision)
                .ThenBy(static verdict => verdict.ParameterSet.Value, StringComparer.Ordinal)
                .ThenBy(static verdict => verdict.TypeId),
        ];
    }
}

file static class SignalRows
{
    public static StationTradingVerdict Verdict(FactRow row, StationTradingScope scope)
    {
        StationTradingParameters parameters = Parameters(row);
        var recorded = ParameterSetName.From(Text(row, SignalFacts.ParameterSet));

        return recorded != parameters.Name
            ? throw new InvalidOperationException(
                $"Сигнал '{row.FactKey}' записан набором '{recorded}', а его пороги дают имя '{parameters.Name}'")
            : new StationTradingVerdict(
            scope,
            (int)SeriesFactRows.Int64Of(row, SignalFacts.TypeId),
            row.Envelope.EventTime.From,
            recorded,
            (ConsiderationOutcome)SeriesFactRows.Int64Of(row, SignalFacts.Outcome),
            [.. Text(row, SignalFacts.Reasons)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(static reason => Enum.Parse<VerdictReason>(reason))],
            SeriesFactRows.Optional(row, SignalFacts.CoverageState) is { } state ? (CoverageState)state : null,
            SeriesFactRows.Int64Of(row, SignalFacts.Justified) == 1 ? Justification(row, parameters) : null);
    }

    public static SignalJustification Justification(FactRow row, StationTradingParameters parameters) =>
        new(
            IskPrice.FromCents(SeriesFactRows.Int64Of(row, SignalFacts.BestBidCents)),
            IskPrice.FromCents(SeriesFactRows.Int64Of(row, SignalFacts.BestAskCents)),
            DateTimeOffset.FromUnixTimeMilliseconds(SeriesFactRows.Int64Of(row, SignalFacts.QuoteObservedAt)),
            IskAmount.FromCents(SeriesFactRows.Int64Of(row, SignalFacts.NetMarginCents)),
            Decimal(row, SignalFacts.NetMarginRate),
            SeriesFactRows.DoubleOf(row, SignalFacts.BuyTurnover),
            SeriesFactRows.DoubleOf(row, SignalFacts.SellTurnover),
            SeriesFactRows.DoubleOf(row, SignalFacts.BuyCompetitors),
            SeriesFactRows.DoubleOf(row, SignalFacts.SellCompetitors),
            SeriesFactRows.DoubleOf(row, SignalFacts.BuyRelists),
            SeriesFactRows.DoubleOf(row, SignalFacts.SellRelists),
            Text(row, SignalFacts.FilledShare) is { Length: > 0 } share
                ? double.Parse(share, CultureInfo.InvariantCulture)
                : null,
            parameters,
            [.. Text(row, SignalFacts.Series).Split(';', StringSplitOptions.RemoveEmptyEntries)]);

    /// <summary>Набор параметров из строки — пороги и ставки записаны в каждом сигнале.</summary>
    public static StationTradingParameters Parameters(FactRow row) =>
        StationTradingParameters.Of(
            Text(row, SignalFacts.Label),
            FeeSchedule.Of(
                Decimal(row, SignalFacts.BrokerFeeRate),
                Decimal(row, SignalFacts.SalesTaxRate),
                Decimal(row, SignalFacts.RelistFeeRate)),
            TimeSpan.FromSeconds(SeriesFactRows.Int64Of(row, SignalFacts.WindowSeconds)),
            Decimal(row, SignalFacts.MinNetMarginRate),
            SeriesFactRows.Int64Of(row, SignalFacts.MinObservedTurnover),
            (int)SeriesFactRows.Int64Of(row, SignalFacts.BandBasisPoints),
            (int)SeriesFactRows.Int64Of(row, SignalFacts.MaxCompetitors),
            (int)SeriesFactRows.Int64Of(row, SignalFacts.MaxRelistPressure),
            (int)SeriesFactRows.Int64Of(row, SignalFacts.ExpectedBuyRelists),
            (int)SeriesFactRows.Int64Of(row, SignalFacts.ExpectedSellRelists));

    public static string Text(FactRow row, string column) =>
        row.Values.TryGetValue(column, out var value) && value is string text ? text : string.Empty;

    public static decimal Decimal(FactRow row, string column) =>
        Text(row, column) is { Length: > 0 } text ? decimal.Parse(text, CultureInfo.InvariantCulture) : 0m;
}
