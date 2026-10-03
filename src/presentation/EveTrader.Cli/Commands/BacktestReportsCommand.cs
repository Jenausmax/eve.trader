using EveTrader.Application.Backtest;
using EveTrader.Domain.Backtest;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Записанные отчёты о прогонах за интервал и сравнение наборов параметров — по
/// отчётам, без повторного прогона.
/// </summary>
internal static class BacktestReportsCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        if (RuleOptions.Interval(options) is not { } interval)
        {
            return 2;
        }

        IReadOnlyList<BacktestReport> reports = await new BacktestReportReader(context.Rows)
            .ReadAsync(interval, cancellationToken)
            .ConfigureAwait(false);

        Output.Line("отчётов:", $"{reports.Count}");

        foreach (BacktestReport report in reports)
        {
            Output.Text(string.Empty);
            BacktestCommand.Print(report);
        }

        // Последний прогон каждого набора против последнего прогона каждого другого — на
        // одном и том же интервале, станции, шаге и горизонте.
        List<BacktestReport> latest =
        [
            .. reports
                .GroupBy(static report => report.ParameterSet)
                .Select(static group => group.OrderByDescending(static report => report.RanAt).First()),
        ];

        for (var left = 0; left < latest.Count; left++)
        {
            for (var right = left + 1; right < latest.Count; right++)
            {
                var comparison = new BacktestComparison(latest[left], latest[right]);

                Output.Text(string.Empty);
                Output.Line("сравнение:", $"{comparison.First.ParameterSet} против {comparison.Second.ParameterSet}");
                Output.Line("сравнимы:", $"{(comparison.Comparable ? "да" : "нет — разные интервал, станция, шаг или горизонт")}");
                Output.Line("лидер:", $"{comparison.Leader?.ParameterSet.Value ?? "нет"}");
                Output.Line("доказательно:", $"{(comparison.Conclusive ? "да" : "нет — хотя бы один прогон недоказателен")}");
            }
        }

        return 0;
    }
}
