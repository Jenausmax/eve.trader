using EveTrader.Application.Book;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Archive.EveRef;
using Microsoft.Extensions.Logging;

namespace EveTrader.Cli.Commands;

/// <summary>Конвертация архивных снимков стакана в факты за интервал.</summary>
internal static class ImportOrderBookCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var everef = new EveRefOptions();

        using var client = new HttpClient { BaseAddress = everef.OrdersBaseAddress };
        client.DefaultRequestHeaders.Add("User-Agent", everef.UserAgent);
        client.Timeout = TimeSpan.FromMinutes(10);

        var archive = new EveRefOrderBookArchive(
            client, everef, context.Loggers.CreateLogger<EveRefOrderBookArchive>());

        var import = new OrderBookImport(
            context.Intake, context.Coverage, context.Registry, TimeProvider.System, context.Loggers);

        OrderBookImportReport report = await import.RunAsync(
            archive,
            new OrderBookScope(
                TimeRange.Between(options.From, options.To),
                [.. options.Regions.Select(RegionId.From)]),
            DiffOptions.Default,
            FeatureOptions.Default,
            StaticDataVersion.From(options.StaticData),
            cancellationToken).ConfigureAwait(false);

        Output.Line("источник:", $"{report.Source}");
        Output.Line("снимков публикуется:", $"{report.SnapshotsPublished}");
        Output.Line("снимков свёрнуто:", $"{report.SnapshotsRead}");
        Output.Line("продолжено с:", $"{report.ResumedFrom:yyyy-MM-dd HH:mm:ss}");
        Output.Line("наблюдений записано:", $"{report.ObservationsWritten}");
        Output.Line("наблюдений уже было:", $"{report.ObservationsAlreadyPresent}");
        Output.Line("событий записано:", $"{report.EventsWritten}");
        Output.Line("пропусков источника:", $"{report.SourceGaps}");
        Output.Line("регионов:", $"{report.Regions}");
        Output.Line("суток:", $"{report.Days}");

        MetricsBlock.Print(context.Metrics);

        return 0;
    }
}
