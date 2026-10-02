using EveTrader.Application.Facts;
using EveTrader.Application.Replay;
using EveTrader.Application.Signals;
using EveTrader.Domain.Backtest;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Backtest;

/// <summary>
/// Прогон правила на истории.
///
/// Сигналы порождает тот же <see cref="SignalGeneration" />, что и в бою, — с моментом
/// решения на каждом шаге сетки и чтением строго на этот момент. Отдельного «пересчитать
/// на истории» нет: совпадение такого пересчёта с боем доказывало бы только то, что обе
/// реализации написаны одной рукой.
///
/// Исход же — наоборот — определяется всем, что наблюдалось после момента решения:
/// бэктест для того и существует, чтобы сверить решение с тем, что случилось дальше.
/// Прогон детерминирован: момент прогона приходит снаружи, суммы целые, порядок задан.
/// </summary>
public sealed class BacktestRun(
    SignalGeneration generation,
    IFactRowReader rows,
    ICoverageLog coverage,
    IMaterializationRegistry registry,
    IFactWriter writer)
{
    public async Task<BacktestResult> RunAsync(
        BacktestSetup setup,
        IReadOnlyList<int> thresholds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DateTimeOffset> decisions = SeriesGrid.Ends(setup.Interval, setup.Step);

        SignalRun run = await generation
            .EvaluateAsync(
                new SignalRequest(setup.Parameters, setup.Scope, setup.Step, decisions, thresholds),
                cancellationToken)
            .ConfigureAwait(false);

        var observed = TimeRange.Between(
            setup.Interval.From, setup.Interval.To + setup.Horizon + (setup.Step * SeriesCoverage.StepTolerance));

        List<OrderEvent> events = [];

        await foreach (FactRow row in rows
            .ReadAsync(FactSet.OrderEvents, observed, null, cancellationToken)
            .ConfigureAwait(false))
        {
            if (row.Region == setup.Scope.Region)
            {
                events.Add(LakeFacts.Event(row));
            }
        }

        HashSet<ObservationId> baselines = [];

        await foreach (FactRow row in rows
            .ReadAsync(FactSet.OrderBaselines, observed, null, cancellationToken)
            .ConfigureAwait(false))
        {
            _ = baselines.Add(row.Observation);
        }

        // Исход оценивается задним числом, по всему наблюдённому: хвоста «ещё не
        // наступившего» снимка здесь нет — снимок, который не пришёл, уже пропущен.
        IReadOnlyList<CoverageEntry> entries = SeriesCoverage.Chain(
            SeriesCoverage.Of(
                await coverage.ReadAsync(observed, [setup.Scope.Region], cancellationToken).ConfigureAwait(false)),
            baselines);

        IReadOnlyList<MaterializedInterval> materialized = await registry
            .ReadAsync(FactSet.OrderEvents, cancellationToken)
            .ConfigureAwait(false);

        List<AssessedSignal> assessed = [];

        foreach (StationTradingVerdict signal in run.Verdicts.Where(static verdict => verdict.IsSignal))
        {
            TimeRange? closed = OutcomeAssessment.Observed(signal.Decision, setup.Horizon, entries);

            SeriesWindow? window = closed is { } range
                ? SeriesWindow.Of(
                    range,
                    CoverageResolver.Resolve(
                        setup.Scope.Region, range, FactSet.OrderEvents, entries, materialized, UpstreamCatalog.Empty))
                : null;

            assessed.Add(OutcomeAssessment.Assess(signal, setup.Horizon, closed, window, events));
        }

        var report = BacktestReport.Of(setup, decisions.Count, run.Verdicts, assessed);

        ObservationId observation = BacktestReportFacts.ObservationFor(report);

        _ = await writer.WriteAsync(
            BacktestReportFacts.ToBatch(report, observation),
            [CoverageEntries.Derived(observation, setup.Scope.Region, setup.Interval, BacktestReportFacts.Source, setup.RanAt)],
            cancellationToken).ConfigureAwait(false);

        return new BacktestResult(report, assessed, run);
    }
}
