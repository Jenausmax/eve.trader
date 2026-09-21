using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Осмотр фактов набора за интервал.
///
/// Читается через порт плоских строк, а не запросом к хранилищу: оператор смотрит ровно
/// то, что увидит домен, и расхождение между «как в консоли» и «как в бою» тут
/// невозможно по построению.
/// </summary>
internal static class FactsCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (FactSetNames.Parse(options.Set) is not { } set)
        {
            Output.Text($"Неизвестный набор '{options.Set}'. Известные: {FactSetNames.All}");

            return 2;
        }

        Output.Line("набор:", $"{set}");
        Output.Line("интервал:", $"{options.From:yyyy-MM-dd HH:mm} — {options.To:yyyy-MM-dd HH:mm}");
        Output.Line("на момент:", $"{(options.KnownSince is { } asOf ? asOf.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "последняя известная версия")}");
        Output.Text(string.Empty);

        var shown = 0;
        var total = 0L;

        await foreach (FactRow row in context.Rows
            .ReadAsync(set, TimeRange.Between(options.From, options.To), options.KnownSince, cancellationToken)
            .ConfigureAwait(false))
        {
            total++;

            if (shown >= options.Limit)
            {
                continue;
            }

            shown++;
            Output.Text(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Region.Value} {row.Envelope.EventTime.From:yyyy-MM-dd HH:mm:ss} {row.Envelope.FactKey} {Values(row)}"));
        }

        Output.Text(string.Empty);
        Output.Line("строк показано:", $"{shown}");
        Output.Line("строк всего:", $"{total}");

        return 0;
    }

    public static string Values(FactRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return string.Join(
            ' ',
            row.Values
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));
    }
}
