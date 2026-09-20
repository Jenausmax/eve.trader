using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Live;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Workers;

/// <summary>
/// Диспетчер наблюдения: каждый цикл берёт регионы с истёкшим сроком годности.
///
/// Один на все регионы, а не по воркеру на регион. Воркер на регион дал бы изоляцию
/// отказов, но превратил бы общий бюджет ошибок в состояние, разделяемое между десятками
/// воркеров, а «один писатель в озеро» — в очередь.
///
/// Расписание — <see cref="AdaptivePollSchedule" />: пока есть созревшие регионы, цикл
/// идёт без пауз; когда их нет, ждёт. Сетки нет, потому что общего среза мира нет —
/// у регионов разный темп, и срок годности смещается на секунды.
/// </summary>
public sealed class MarketObservationWorker(
    IServiceProvider services,
    LiveCollector collector,
    ObservationOptions options,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    ILogger<MarketObservationWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    protected override string WorkerName => "MarketObservation";

    protected override WorkerSchedule Schedule { get; } = new AdaptivePollSchedule(
        idle: options.IdleDelay,
        busy: TimeSpan.Zero,
        error: options.ErrorDelay);

    protected override async Task<object?> ExecuteCycleAsync(
        WorkerContext context,
        CancellationToken cancellationToken)
    {
        CollectionCycle cycle = await collector.RunCycleAsync(
            options.Diff, options.Features, StaticDataVersion.From(options.StaticData), cancellationToken)
            .ConfigureAwait(false);

        return cycle;
    }
}
