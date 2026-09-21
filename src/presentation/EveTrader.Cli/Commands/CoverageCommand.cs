using EveTrader.Application.Reporting;
using EveTrader.Domain.Facts;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Отчёт о покрытии за интервал.
///
/// Состояние покрытия печатается рядом с долей покрытого времени, а не вместо неё:
/// «наблюдалось на 90 %» и «наблюдалось целиком» — разные ответы, и оператор обязан их
/// различать.
/// </summary>
internal static class CoverageCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<CoverageReportRow> rows = await context.Reports
            .CoverageReportAsync(TimeRange.Between(options.From, options.To), cancellationToken)
            .ConfigureAwait(false);

        Output.Line("интервал:", $"{options.From:yyyy-MM-dd HH:mm} — {options.To:yyyy-MM-dd HH:mm}");
        Output.Line("регионов в отчёте:", $"{rows.Count}");
        Output.Text(string.Empty);
        Output.Text("регион      наблюдений  покрыто  пропусков  частичных  отказов  состояние");

        foreach (CoverageReportRow row in rows)
        {
            Output.Text(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{row.Region.Value,-11} {row.Observations,10} {row.CoveredFraction,8:P1} {row.SourceGaps,10} {row.PartialObservations,10} {row.FailedObservations,8}  {row.State}"));
        }

        if (rows.Count == 0)
        {
            Output.Text("записей покрытия за интервал нет");
        }

        return 0;
    }
}
