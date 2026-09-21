using EveTrader.Application.Reporting;
using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Esi.Orders;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Расход ресурсов за интервал: сколько спросили у источника и сколько получили.
///
/// Отвечает на вопрос спеки «сколько израсходовано за прошедшие сутки». Метрики OTel на
/// него ответить не могут: они живут в процессе сбора, а вопрос задаётся другому
/// процессу и после перезапуска.
/// </summary>
internal static class ResourceUsageCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var rawPages = new RawPageArchive(
            options.RawPages ?? Path.Combine(context.Root, "raw-pages"));

        var report = new ResourceUsageReport(context.Reports, rawPages);

        ResourceUsage usage = await report
            .ForAsync(TimeRange.Between(options.From, options.To), cancellationToken)
            .ConfigureAwait(false);

        Output.Line("интервал:", $"{options.From:yyyy-MM-dd HH:mm} — {options.To:yyyy-MM-dd HH:mm}");
        Output.Line("попыток наблюдения:", $"{usage.Observations}");
        Output.Line("запросов:", $"{usage.Requests}");
        Output.Line("«не изменилось»:", $"{usage.NotModified}");
        Output.Line("доля «не изменилось»:", $"{usage.NotModifiedFraction:P1}");
        Output.Line("отказов:", $"{usage.Failed}");
        Output.Line("трафик:", $"{Output.Bytes(usage.BytesReceived)}");
        Output.Line("страниц в окне:", $"{usage.RawPages}");
        Output.Line(
            "трафик известен с:",
            $"{(usage.TrafficKnownFrom is { } from ? from.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "окно пусто")}");

        if (!usage.TrafficCoversWholeInterval)
        {
            // Молчать об этом нельзя: ноль байт за интервал вне окна выглядит как
            // «трафика не было», а означает «байты уже убраны».
            Output.Text("окно сырых страниц не покрывает интервал целиком — трафик занижен");
        }

        return 0;
    }
}
