using EveTrader.Application.Diagnostics;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Печать снятых метрик после прогона.
///
/// Нужна затем, что счётчик без читателя неотличим от отсутствующего. Пока коллектора
/// OTel у проекта нет, единственный способ убедиться, что инструмент действительно
/// пишет, — показать его оператору здесь же.
/// </summary>
internal static class MetricsBlock
{
    public static void Print(MetricExport metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        Output.Text(string.Empty);
        Output.Text("метрики:");

        if (metrics.Names.Count == 0)
        {
            Output.Text("  замеров не было");

            return;
        }

        foreach (var name in metrics.Names)
        {
            MetricTally tally = metrics.Of(name)!;

            Output.Text(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"  {name,-44} сумма {tally.Total,14:F2}  замеров {tally.Count,8}  последнее {tally.Last,12:F2}"));
        }
    }
}
