using EveTrader.Domain.Facts;

namespace EveTrader.Cli.Commands;

/// <summary>Замеры по импортированной дневной истории.</summary>
internal static class HistoryStatsCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        HistoryStats stats = await HistoryStats.MeasureAsync(
            context.Layout, TimeRange.Between(options.From, options.To), cancellationToken)
            .ConfigureAwait(false);

        Output.Line("строк:", $"{stats.Rows}");
        Output.Line("торгуемых типов:", $"{stats.Types}");
        Output.Line("активных регионов:", $"{stats.Regions}");
        Output.Line("суток:", $"{stats.Days}");
        Output.Line("первые сутки:", $"{stats.Earliest:yyyy-MM-dd}");
        Output.Line("последние сутки:", $"{stats.Latest:yyyy-MM-dd}");

        return 0;
    }
}
