using EveTrader.Cli;
using EveTrader.Cli.Commands;
using Microsoft.Extensions.Logging;

// Composition root. Команда собирает обвязку озера, исполняется и завершается: здесь
// нет процесса, который живёт дольше команды, — живой сбор тоже считает циклы и
// останавливается сам.
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

using var context = new CliContext(options.Lake, loggerFactory);

switch (options.Command)
{
    case "collect":
        return await CollectCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "import-history":
        return await ImportHistoryCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "import-orderbook":
        return await ImportOrderBookCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "replay":
        return await ReplayCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "accept":
        return await AcceptCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "coverage":
        return await CoverageCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "materialization":
        return await MaterializationCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "facts":
        return await FactsCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "resource-usage":
        return await ResourceUsageCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    case "history-stats":
        return await HistoryStatsCommand.RunAsync(context, options, cancellation.Token).ConfigureAwait(false);

    default:
        Console.Error.WriteLine(CommandLine.Usage);

        return 2;
}
