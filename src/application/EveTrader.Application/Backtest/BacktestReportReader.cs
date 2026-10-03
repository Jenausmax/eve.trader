using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Application.Series;
using EveTrader.Domain.Backtest;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Backtest;

/// <summary>
/// Чтение записанных отчётов о прогонах — основа сравнения наборов параметров без
/// повторного прогона.
/// </summary>
public sealed class BacktestReportReader(IFactRowReader rows)
{
    /// <summary>Отчёты о прогонах, чей интервал начинается внутри указанного, — по моменту прогона.</summary>
    public async Task<IReadOnlyList<BacktestReport>> ReadAsync(
        TimeRange within,
        CancellationToken cancellationToken)
    {
        var reports = new List<BacktestReport>();

        await foreach (FactRow row in rows
            .ReadAsync(FactSet.BacktestReports, within, null, cancellationToken)
            .ConfigureAwait(false))
        {
            reports.Add(ReportRows.Report(row));
        }

        return
        [
            .. reports
                .OrderBy(static report => report.Interval.From)
                .ThenBy(static report => report.RanAt)
                .ThenBy(static report => report.ParameterSet.Value, StringComparer.Ordinal),
        ];
    }
}

file static class ReportRows
{
    public static BacktestReport Report(FactRow row) =>
        new(
            ParameterSetName.From(Text(row, BacktestReportFacts.ParameterSet)),
            Text(row, BacktestReportFacts.Label),
            Text(row, BacktestReportFacts.Canonical),
            new StationTradingScope(
                row.Region,
                SeriesFactRows.Int64Of(row, BacktestReportFacts.StationId),
                Text(row, BacktestReportFacts.StationName)),
            TimeRange.Between(row.Envelope.EventTime.From, row.Envelope.EventTime.To),
            TimeSpan.FromSeconds(SeriesFactRows.Int64Of(row, BacktestReportFacts.StepSeconds)),
            TimeSpan.FromSeconds(SeriesFactRows.Int64Of(row, BacktestReportFacts.HorizonSeconds)),
            row.Envelope.StaticData,
            DateTimeOffset.FromUnixTimeMilliseconds(SeriesFactRows.Int64Of(row, BacktestReportFacts.RanAt)),
            Count(row, BacktestReportFacts.Decisions),
            Count(row, BacktestReportFacts.Considered),
            Count(row, BacktestReportFacts.Signals),
            Count(row, BacktestReportFacts.ConditionsNotMet),
            Count(row, BacktestReportFacts.InsufficientData),
            Count(row, BacktestReportFacts.Successes),
            Count(row, BacktestReportFacts.Failures),
            Count(row, BacktestReportFacts.Unknown),
            SeriesFactRows.Int64Of(row, BacktestReportFacts.TotalRealizedCents),
            Decimal(row, BacktestReportFacts.MeanRealizedCents),
            SeriesFactRows.DoubleOf(row, BacktestReportFacts.DeviationRealizedCents),
            Decimal(row, BacktestReportFacts.MaxUnknownShare));

    public static int Count(FactRow row, string column) => (int)SeriesFactRows.Int64Of(row, column);

    public static string Text(FactRow row, string column) =>
        row.Values.TryGetValue(column, out var value) && value is string text ? text : string.Empty;

    public static decimal Decimal(FactRow row, string column) =>
        Text(row, column) is { Length: > 0 } text ? decimal.Parse(text, CultureInfo.InvariantCulture) : 0m;
}
