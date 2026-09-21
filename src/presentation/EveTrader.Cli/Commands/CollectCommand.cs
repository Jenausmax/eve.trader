using EveTrader.Application.Live;
using EveTrader.Application.Workers;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using EveTrader.Infrastructure.Esi;
using EveTrader.Infrastructure.Esi.Orders;
using Microsoft.Extensions.Logging;

namespace EveTrader.Cli.Commands;

/// <summary>
/// Живой сбор с публичного ESI.
///
/// Число циклов задаётся и по умолчанию равно одному. Бесконечный режим остаётся
/// (<c>--cycles 0</c>), но включается явно: суточный прогон предсказуемее, когда у него
/// есть объявленная граница, а команда без границы не годится ни для проверки, ни для
/// прогона под присмотром.
/// </summary>
internal static class CollectCommand
{
    public static async Task<int> RunAsync(
        CliContext context,
        CommandLine options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var esi = new EsiOptions();

        using var client = new HttpClient { BaseAddress = esi.BaseAddress };
        client.DefaultRequestHeaders.Add("User-Agent", esi.UserAgent);
        client.Timeout = TimeSpan.FromMinutes(2);

        var settings = new ObservationOptions { StaticData = options.StaticData };
        IReadOnlyList<RegionId> watched = options.Regions.Count == 0
            ? settings.Hubs
            : [.. options.Regions.Select(RegionId.From)];

        var rawPages = new RawPageArchive(options.RawPages ?? Path.Combine(context.Root, "raw-pages"));
        var budget = new ErrorBudget(TimeProvider.System);

        var poller = new EsiRegionBookPoller(
            client, esi, budget, TimeProvider.System, rawPages, context.Diagnostics,
            context.Loggers.CreateLogger<EsiRegionBookPoller>());

        // Пять минут — минимальный такт, который имеет смысл: игра допускает правку
        // ордера раз в пять минут, а срок годности ответа источник объявляет сам, и
        // расписание всё равно не спросит раньше.
        var scope = new ScopeHistory();
        scope.Record(new PolicyChange(
            options.Regions.Count == 0
                ? ScopePolicy.Hubs(TimeSpan.FromMinutes(5))
                : ScopePolicy.Of(watched, TimeSpan.FromMinutes(5)),
            DateTimeOffset.UtcNow));

        RegionViability viability = ViabilitySeed.For(watched);
        var schedule = new ObservationSchedule();

        var collector = new LiveCollector(
            poller, context.Intake, scope, viability, schedule, watched, TimeProvider.System);

        Output.Line("регионов в охвате:", $"{watched.Count}");
        Output.Line("циклов:", $"{(options.Cycles == 0 ? "без предела" : options.Cycles.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
        Output.Text(string.Empty);

        var observed = 0;
        var unchanged = 0;
        var partial = 0;
        var failed = 0;
        var events = 0;
        var cycles = 0;

        while (!cancellationToken.IsCancellationRequested
            && (options.Cycles == 0 || cycles < options.Cycles))
        {
            cycles++;

            CollectionCycle cycle = await collector.RunCycleAsync(
                settings.Diff, settings.Features, StaticDataVersion.From(options.StaticData), cancellationToken)
                .ConfigureAwait(false);

            observed += cycle.Observed;
            unchanged += cycle.Unchanged;
            partial += cycle.Partial;
            failed += cycle.Failed;
            events += cycle.Events;

            Output.Text(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"цикл {cycles}: наблюдений {cycle.Observed}, «не изменилось» {cycle.Unchanged}, частичных {cycle.Partial}, отказов {cycle.Failed}, событий {cycle.Events}"));

            // Пауза только когда работы не было: созревшие регионы разгребаются подряд,
            // а ждать нечего, пока срок годности следующего не истёк.
            if ((options.Cycles == 0 || cycles < options.Cycles)
                && cycle.Observed + cycle.Unchanged + cycle.Partial + cycle.Failed == 0)
            {
                await Task.Delay(settings.IdleDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        Output.Text(string.Empty);
        Output.Line("циклов выполнено:", $"{cycles}");
        Output.Line("наблюдений:", $"{observed}");
        Output.Line("«не изменилось»:", $"{unchanged}");
        Output.Line("частичных:", $"{partial}");
        Output.Line("отказов:", $"{failed}");
        Output.Line("событий:", $"{events}");
        Output.Line("остаток бюджета:", $"{budget.Remaining}");

        MetricsBlock.Print(context.Metrics);

        return 0;
    }
}
