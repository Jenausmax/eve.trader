using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Archive.EveRef;
using Microsoft.Extensions.Logging;

namespace EveTrader.Cli.Commands;

/// <summary>Импорт дневной истории из архива EVE Ref за интервал.</summary>
internal static class ImportHistoryCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var everef = new EveRefOptions();

        using var client = new HttpClient { BaseAddress = everef.HistoryBaseAddress };
        client.DefaultRequestHeaders.Add("User-Agent", everef.UserAgent);
        client.Timeout = TimeSpan.FromMinutes(5);

        var archive = new EveRefMarketHistoryArchive(
            client, everef, context.Loggers.CreateLogger<EveRefMarketHistoryArchive>());

        var import = new DailyHistoryImport(
            context.Writer, context.Registry, context.Coverage, TimeProvider.System,
            context.Loggers.CreateLogger<DailyHistoryImport>());

        DailyHistoryImportReport report = await import.RunAsync(
            archive,
            MarketHistoryScope.Fresh(TimeRange.Between(options.From, options.To)),
            StaticDataVersion.From(options.StaticData),
            cancellationToken).ConfigureAwait(false);

        Output.Line("источник:", $"{report.Source}");
        Output.Line("суток записано:", $"{report.DaysWritten}");
        Output.Line("суток уже было:", $"{report.DaysAlreadyPresent}");
        Output.Line("строк записано:", $"{report.RowsWritten}");
        Output.Line("регионов встречено:", $"{report.Regions}");
        Output.Line("типов встречено:", $"{report.Types}");

        return 0;
    }
}
