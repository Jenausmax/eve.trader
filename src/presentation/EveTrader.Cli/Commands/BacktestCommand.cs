using EveTrader.Application.Backtest;
using EveTrader.Application.Signals;
using EveTrader.Domain.Backtest;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Прогон правила на истории: ряды, сигналы тем же путём, что в бою, исходы на
/// названном горизонте и отчёт с базовой линией «не торговать».
///
/// Отчёт записывается фактом — сравнение наборов параметров идёт по записанным отчётам
/// (<c>backtest-reports</c>), а не повторным прогоном.
/// </summary>
internal static class BacktestCommand
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
        TimeSpan horizon = RuleOptions.Horizon(options);

        RuleOptions.Print(parameters, scope, step);
        Output.Line("интервал:", $"{interval.From:yyyy-MM-dd HH:mm} — {interval.To:yyyy-MM-dd HH:mm}");
        Output.Line("горизонт исхода:", $"{horizon.TotalHours} ч");
        Output.Text(string.Empty);

        // Ряды досчитываются и за горизонт: исход последних решений смотрит за конец интервала.
        SeriesCommand.Print(await SeriesCommand.MaterializeAsync(
            context, scope, interval, StationTradingSeries.Definitions(parameters, step), options.StaticData, cancellationToken)
            .ConfigureAwait(false));

        var run = new BacktestRun(
            new SignalGeneration(context.Rows), context.Rows, context.Coverage, context.Registry, context.Writer);

        BacktestResult result = await run.RunAsync(
            new BacktestSetup(
                parameters,
                scope,
                interval,
                step,
                horizon,
                RuleOptions.MaxUnknownShare(options),
                StaticDataVersion.From(options.StaticData),
                TimeProvider.System.GetUtcNow()),
            FeatureOptions.DefaultThresholds,
            cancellationToken).ConfigureAwait(false);

        Output.Text(string.Empty);
        Print(result.Report);

        Output.Text(string.Empty);
        Output.Text("сигналы и исходы:");

        foreach (AssessedSignal assessed in result.Assessed.Take(options.Limit))
        {
            Output.Text($"  {assessed.Outcome,-8} {assessed.Realized?.ToString() ?? "—",10}  {VerdictText.Signal(assessed.Signal)}");
        }

        Output.Text(string.Empty);
        Output.Text("причины молчания:");
        VerdictText.Silence(result.Run.Verdicts);

        return 0;
    }

    public static void Print(BacktestReport report)
    {
        Output.Line("набор параметров:", $"{report.ParameterSet}");
        Output.Line("момент прогона:", $"{report.RanAt:yyyy-MM-dd HH:mm:ss}");
        Output.Line("версия статических:", $"{report.StaticData}");
        Output.Line("моментов решения:", $"{report.Decisions}");
        Output.Line("пар рассмотрено:", $"{report.Considered}");
        Output.Line("сигналов:", $"{report.Signals}");
        Output.Line("условия не выполнены:", $"{report.ConditionsNotMet}");
        Output.Line("данных не хватило:", $"{report.InsufficientData}");
        Output.Line("исход определён:", $"{report.Determined} ({report.DeterminedShare:P1})");
        Output.Line("исход неизвестен:", $"{report.Unknown} ({report.UnknownShare:P1}, порог {report.MaxUnknownShare:P0})");
        Output.Line("успехов / неудач:", $"{report.Successes} / {report.Failures}");
        Output.Line("доля успехов:", $"{report.HitRate:P1}");
        Output.Line("результат, ISK/ед.:", $"сумма {report.TotalRealizedCents / 100m:0.00}, среднее {report.MeanRealizedCents / 100m:0.00}, разброс {report.DeviationRealizedCents / 100d:0.00}");
        Output.Line("базовая линия:", $"«не торговать» — {report.BaselineTotalCents / 100m:0.00} ISK");
        Output.Line("лучше базовой линии:", $"{(report.BeatsBaseline ? "да" : "нет")}");
        Output.Line("прогон доказателен:", $"{(report.IsConclusive ? "да" : "нет")}");
    }
}
