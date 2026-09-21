using System.Globalization;
using EveTrader.Application.Book;
using EveTrader.Application.History;
using EveTrader.Application.Intake;
using EveTrader.Cli;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Archive.EveRef;
using EveTrader.Infrastructure.Facts.Lake;
using EveTrader.Infrastructure.Facts.Query;
using Microsoft.Extensions.Logging;

// Composition root. Полный набор команд приезжает с add-pipeline-acceptance; здесь ровно
// то, без чего нельзя выполнить импорт и снять замеры.
using CancellationTokenSource cancellation = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var options = CommandLine.Parse(args);

if (options is null)
{
    Console.Error.WriteLine(CommandLine.Usage);

    return 2;
}

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.TimestampFormat = "HH:mm:ss ";
    }));

var lake = new LakeOptions { Root = options.Lake };
var layout = new LakeLayout(lake);
var coverage = new ParquetCoverageLog(layout);
var writer = new ParquetFactWriter(layout, coverage);
var registry = new ParquetMaterializationRegistry(layout);
var rows = new DuckDbFactRowReader(layout);

switch (options.Command)
{
    case "import-history":
        {
            var everef = new EveRefOptions();

            using var client = new HttpClient { BaseAddress = everef.HistoryBaseAddress };
            client.DefaultRequestHeaders.Add("User-Agent", everef.UserAgent);
            client.Timeout = TimeSpan.FromMinutes(5);

            var archive = new EveRefMarketHistoryArchive(
                client, everef, loggerFactory.CreateLogger<EveRefMarketHistoryArchive>());

            var import = new DailyHistoryImport(
                writer, registry, coverage, TimeProvider.System, loggerFactory.CreateLogger<DailyHistoryImport>());

            DailyHistoryImportReport report = await import.RunAsync(
                archive,
                MarketHistoryScope.Fresh(TimeRange.Between(options.From, options.To)),
                StaticDataVersion.From(options.StaticData),
                cancellation.Token).ConfigureAwait(false);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"источник:            {report.Source}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"суток записано:      {report.DaysWritten}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"суток уже было:      {report.DaysAlreadyPresent}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"строк записано:      {report.RowsWritten}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"регионов встречено:  {report.Regions}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"типов встречено:     {report.Types}"));

            return 0;
        }

    case "import-orderbook":
        {
            var everef = new EveRefOptions();

            using var client = new HttpClient { BaseAddress = everef.OrdersBaseAddress };
            client.DefaultRequestHeaders.Add("User-Agent", everef.UserAgent);
            client.Timeout = TimeSpan.FromMinutes(10);

            var archive = new EveRefOrderBookArchive(
                client, everef, loggerFactory.CreateLogger<EveRefOrderBookArchive>());

            var intake = new ObservationIntake(
                new ObservationDerivation(writer, new DailyCheckpointPolicy()),
                loggerFactory.CreateLogger<ObservationIntake>());

            var import = new OrderBookImport(
                intake, coverage, registry, TimeProvider.System, loggerFactory);

            OrderBookImportReport orders = await import.RunAsync(
                archive,
                new OrderBookScope(
                    TimeRange.Between(options.From, options.To),
                    [.. options.Regions.Select(RegionId.From)]),
                DiffOptions.Default,
                FeatureOptions.Default,
                StaticDataVersion.From(options.StaticData),
                cancellation.Token).ConfigureAwait(false);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"источник:            {orders.Source}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"снимков публикуется: {orders.SnapshotsPublished}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"снимков свёрнуто:    {orders.SnapshotsRead}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"продолжено с:        {orders.ResumedFrom:yyyy-MM-dd HH:mm:ss}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"наблюдений записано: {orders.ObservationsWritten}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"наблюдений уже было: {orders.ObservationsAlreadyPresent}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"событий записано:    {orders.EventsWritten}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"пропусков источника: {orders.SourceGaps}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"регионов:            {orders.Regions}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"суток:               {orders.Days}"));

            return 0;
        }

    case "history-stats":
        {
            HistoryStats stats = await HistoryStats.MeasureAsync(
                layout, TimeRange.Between(options.From, options.To), cancellation.Token).ConfigureAwait(false);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"строк:               {stats.Rows}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"торгуемых типов:     {stats.Types}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"активных регионов:   {stats.Regions}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"суток:               {stats.Days}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"первые сутки:        {stats.Earliest:yyyy-MM-dd}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"последние сутки:     {stats.Latest:yyyy-MM-dd}"));

            return 0;
        }

    default:
        Console.Error.WriteLine(CommandLine.Usage);

        return 2;
}
