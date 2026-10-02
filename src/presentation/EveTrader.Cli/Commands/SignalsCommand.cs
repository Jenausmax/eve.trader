using EveTrader.Application.Signals;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Сигналы станционной торговли за интервал — с обоснованием и причинами молчания.
///
/// Сначала материализуются ряды, которые правило читает, затем на каждом шаге сетки
/// пары станции рассматриваются ровно так, как их рассмотрела бы система в тот момент,
/// и исходы записываются фактами. Сигнал — рекомендация: команда описывает возможность
/// и её обоснование, а действий в игре не выполняет и выполнять не умеет.
/// </summary>
internal static class SignalsCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        if (RuleOptions.Parameters(options) is not { } parameters || RuleOptions.Interval(options) is not { } interval)
        {
            return 2;
        }

        StationTradingScope scope = StationTradingScope.Jita44;
        TimeSpan step = RuleOptions.Step(options);

        RuleOptions.Print(parameters, scope, step);
        Output.Line("интервал:", $"{interval.From:yyyy-MM-dd HH:mm} — {interval.To:yyyy-MM-dd HH:mm}");
        Output.Text(string.Empty);

        SeriesCommand.Print(await SeriesCommand.MaterializeAsync(
            context, scope, interval, StationTradingSeries.Definitions(parameters, step), options.StaticData, cancellationToken)
            .ConfigureAwait(false));

        SignalRun run = await new SignalGeneration(context.Rows)
            .EvaluateAsync(
                new SignalRequest(parameters, scope, step, SeriesGrid.Ends(interval, step), FeatureOptions.DefaultThresholds),
                cancellationToken)
            .ConfigureAwait(false);

        SignalRecordingReport recorded = await new SignalRecording(context.Writer)
            .WriteAsync(
                parameters, scope, run.Verdicts, step, TimeProvider.System.GetUtcNow(),
                StaticDataVersion.From(options.StaticData), cancellationToken)
            .ConfigureAwait(false);

        Output.Text(string.Empty);
        Output.Text("моменты решения:");

        foreach (DecisionSummary summary in run.Decisions)
        {
            Output.Text("  " + VerdictText.Decision(summary));
        }

        List<StationTradingVerdict> signals = [.. run.Verdicts.Where(static verdict => verdict.IsSignal)];

        Output.Text(string.Empty);
        Output.Line("пар рассмотрено:", $"{run.Verdicts.Count}");
        Output.Line("сигналов:", $"{signals.Count}");
        Output.Line("условия не выполнены:", $"{run.Verdicts.Count(static verdict => verdict.Outcome is ConsiderationOutcome.ConditionsNotMet)}");
        Output.Line("данных не хватило:", $"{run.Verdicts.Count(static verdict => verdict.Outcome is ConsiderationOutcome.InsufficientData)}");
        Output.Line("порций записано:", $"{recorded.Written} (уже было {recorded.AlreadyPresent})");

        Output.Text(string.Empty);
        Output.Text("сигналы — рекомендации, а не действия: система ордеров не выставляет и в игре не действует.");

        foreach (StationTradingVerdict signal in signals.Take(options.Limit))
        {
            Output.Text("  " + VerdictText.Signal(signal));
        }

        if (signals.Count > options.Limit)
        {
            Output.Text($"  … ещё {signals.Count - options.Limit}; --limit покажет больше");
        }

        Output.Text(string.Empty);
        Output.Text("причины молчания:");
        VerdictText.Silence(run.Verdicts);

        return 0;
    }
}
