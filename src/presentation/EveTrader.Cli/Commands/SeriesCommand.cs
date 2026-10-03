using EveTrader.Application.Series;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Материализация рядов, которые читает правило, за интервал.
///
/// Ряды пишутся в то же озеро, из которого читаются факты, — это производный набор, и
/// удалить и перестроить его можно в любой момент.
/// </summary>
internal static class SeriesCommand
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

        StationTradingScope scope = StationTradingScope.Jita44;
        TimeSpan step = RuleOptions.Step(options);
        IReadOnlyList<SeriesDefinition> definitions = StationTradingSeries.Definitions(
            TimeSpan.FromHours(RuleOptions.Number(options, "window", 2d)), step, (int)RuleOptions.Number(options, "band", 100d));

        SeriesMaterializationReport report = await MaterializeAsync(context, scope, interval, definitions, options.StaticData, cancellationToken)
            .ConfigureAwait(false);

        Output.Line("регион:", $"{scope.Region.Value}");
        Output.Line("интервал:", $"{interval.From:yyyy-MM-dd HH:mm} — {interval.To:yyyy-MM-dd HH:mm}");
        Output.Line("рядов:", $"{string.Join(", ", definitions.Select(static definition => definition.Key))}");
        Print(report);

        return 0;
    }

    public static Task<SeriesMaterializationReport> MaterializeAsync(
        CliContext context,
        StationTradingScope scope,
        TimeRange interval,
        IReadOnlyList<SeriesDefinition> definitions,
        string staticData,
        CancellationToken cancellationToken) =>
        new SeriesMaterialization(context.Rows, context.Coverage, context.Registry, context.Writer)
            .RunAsync(
                new SeriesRequest(
                    scope.Region,
                    interval,
                    definitions,
                    FeatureOptions.DefaultThresholds,
                    StaticDataVersion.From(staticData),
                    UpstreamCatalog.Empty),
                cancellationToken);

    public static void Print(SeriesMaterializationReport report)
    {
        Output.Line("окон рассмотрено:", $"{report.Windows}");
        Output.Line("окон отклонено:", $"{report.Refused}");
        Output.Line("окон неполных:", $"{report.Incomplete}");
        Output.Line("точек:", $"{report.Points}");
        Output.Line("порций записано:", $"{report.Written}");
        Output.Line("порций уже было:", $"{report.AlreadyPresent}");
    }
}
