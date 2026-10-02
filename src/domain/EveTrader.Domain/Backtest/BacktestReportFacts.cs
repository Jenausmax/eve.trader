using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Backtest;

/// <summary>
/// Перевод отчёта о прогоне в факт.
///
/// Отчёт пишется с интервалом, именем набора параметров, версией статических данных и
/// моментом прогона: сравнение двух наборов строится по записанным отчётам, а не
/// повторным прогоном. Время события — интервал прогона, момент знания — момент
/// прогона.
/// </summary>
public static class BacktestReportFacts
{
    public const string StationId = "station_id";
    public const string StationName = "station_name";
    public const string ParameterSet = "parameter_set";
    public const string Label = "label";
    public const string Canonical = "canonical";
    public const string StepSeconds = "step_seconds";
    public const string HorizonSeconds = "horizon_seconds";
    public const string RanAt = "ran_at_unix_ms";
    public const string Decisions = "decisions";
    public const string Considered = "considered";
    public const string Signals = "signals";
    public const string ConditionsNotMet = "conditions_not_met";
    public const string InsufficientData = "insufficient_data";
    public const string Successes = "successes";
    public const string Failures = "failures";
    public const string Unknown = "unknown";
    public const string TotalRealizedCents = "total_realized_cents";
    public const string MeanRealizedCents = "mean_realized_cents";
    public const string DeviationRealizedCents = "deviation_realized_cents";
    public const string BaselineTotalCents = "baseline_total_cents";
    public const string HitRate = "hit_rate";
    public const string UnknownShare = "unknown_share";
    public const string MaxUnknownShare = "max_unknown_share";
    public const string BeatsBaseline = "beats_baseline";
    public const string Conclusive = "conclusive";

    /// <summary>Источник в записи покрытия отчёта.</summary>
    public const string Source = "backtest";

    public static FactBatch ToBatch(BacktestReport report, ObservationId observation)
    {
        var envelope = new FactEnvelope(
            FactKey(report),
            EventTime.Between(report.Interval.From, report.Interval.To),
            report.RanAt,
            observation,
            report.StaticData);

        return FactBatch.Of(
            FactSet.BacktestReports,
            report.Scope.Region,
            observation,
            DateOnly.FromDateTime(report.Interval.From.UtcDateTime),
            [envelope],
            [
                FactColumn.OfInt64(StationId, [report.Scope.StationId]),
                FactColumn.OfString(StationName, [report.Scope.Name]),
                FactColumn.OfString(ParameterSet, [report.ParameterSet.Value]),
                FactColumn.OfString(Label, [report.Label]),
                FactColumn.OfString(Canonical, [report.Canonical]),
                FactColumn.OfInt64(StepSeconds, [(long)report.Step.TotalSeconds]),
                FactColumn.OfInt64(HorizonSeconds, [(long)report.Horizon.TotalSeconds]),
                FactColumn.OfInt64(RanAt, [report.RanAt.ToUnixTimeMilliseconds()]),
                FactColumn.OfInt64(Decisions, [report.Decisions]),
                FactColumn.OfInt64(Considered, [report.Considered]),
                FactColumn.OfInt64(Signals, [report.Signals]),
                FactColumn.OfInt64(ConditionsNotMet, [report.ConditionsNotMet]),
                FactColumn.OfInt64(InsufficientData, [report.InsufficientData]),
                FactColumn.OfInt64(Successes, [report.Successes]),
                FactColumn.OfInt64(Failures, [report.Failures]),
                FactColumn.OfInt64(Unknown, [report.Unknown]),
                FactColumn.OfInt64(TotalRealizedCents, [report.TotalRealizedCents]),
                FactColumn.OfString(MeanRealizedCents, [Decimal(report.MeanRealizedCents)]),
                FactColumn.OfDouble(DeviationRealizedCents, [report.DeviationRealizedCents]),
                FactColumn.OfInt64(BaselineTotalCents, [report.BaselineTotalCents]),
                FactColumn.OfString(HitRate, [Decimal(report.HitRate)]),
                FactColumn.OfString(UnknownShare, [Decimal(report.UnknownShare)]),
                FactColumn.OfString(MaxUnknownShare, [Decimal(report.MaxUnknownShare)]),
                FactColumn.OfInt64(BeatsBaseline, [report.BeatsBaseline ? 1L : 0L]),
                FactColumn.OfInt64(Conclusive, [report.IsConclusive ? 1L : 0L]),
            ]);
    }

    /// <summary>
    /// Ключ отчёта: набор, станция, интервал и момент прогона. Повторный прогон того же
    /// набора на том же интервале — новый отчёт рядом, а не уточнение прежнего: он
    /// состоялся в другой момент.
    /// </summary>
    public static string FactKey(BacktestReport report) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"backtest/{report.ParameterSet.Value}/{report.Scope.Region.Value}/{report.Scope.StationId}/{report.Interval.From:yyyyMMddTHHmmssZ}/{report.Interval.To:yyyyMMddTHHmmssZ}/{report.RanAt:yyyyMMddTHHmmssfffZ}");

    public static ObservationId ObservationFor(BacktestReport report) =>
        ObservationId.From(string.Create(
            CultureInfo.InvariantCulture,
            $"backtest-{report.Scope.Region.Value}-{report.ParameterSet.Value}-{report.Interval.From:yyyyMMddTHHmmss}-{report.Interval.To:yyyyMMddTHHmmss}-{report.RanAt:yyyyMMddTHHmmssfff}"));

    public static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
