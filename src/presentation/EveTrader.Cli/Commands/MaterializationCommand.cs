using EveTrader.Application.Reporting;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Отчёт о материализации: что лежит локально и по какому набору.
///
/// Перечисление файлов таким ответом не является: файл может лежать неподтверждённым,
/// а подтверждённого файла может не быть на месте. Основание — реестр.
/// </summary>
internal static class MaterializationCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<MaterializationReportRow> rows = await context.Reports
            .MaterializationReportAsync(cancellationToken)
            .ConfigureAwait(false);

        Output.Line("наборов в реестре:", $"{rows.Count}");
        Output.Text(string.Empty);
        Output.Text("набор              регионов  с                    по");

        foreach (MaterializationReportRow row in rows)
        {
            Output.Text(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{row.Set,-18} {row.Regions,8}  {row.Earliest:yyyy-MM-dd HH:mm:ss}  {row.Latest:yyyy-MM-dd HH:mm:ss}"));
        }

        if (rows.Count == 0)
        {
            Output.Text("реестр пуст: локально не материализовано ничего");
        }

        return 0;
    }
}
