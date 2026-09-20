using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Facts;
using EveTrader.Application.Live;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Workers;

/// <summary>
/// Обслуживание озера: уборка неподтверждённого и окно сырых страниц.
///
/// Третий воркер, а не ветка в двух предыдущих. Его работа не зависит ни от темпа
/// наблюдения, ни от темпа истории, и счётчики у неё свои: «сколько убрано» и «сколько
/// наблюдений записано» отвечают на разные вопросы, и склеенные в один воркер они не
/// дали бы ответа ни на один.
///
/// Компакция выполняется вне пиков — отсюда и разреженный темп: писатель в озеро один,
/// и конкурировать с ним за диск в момент наблюдения незачем.
/// </summary>
public sealed class CompactionWorker(
    IServiceProvider services,
    IFactMaintenance maintenance,
    IRawPageArchive rawPages,
    CompactionOptions options,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    ILogger<CompactionWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    protected override string WorkerName => "Compaction";

    protected override WorkerSchedule Schedule { get; } = new IntervalSchedule(options.Interval);

    protected override async Task<object?> ExecuteCycleAsync(
        WorkerContext context,
        CancellationToken cancellationToken)
    {
        var swept = await maintenance.SweepUnconfirmedAsync(cancellationToken).ConfigureAwait(false);

        var pages = await rawPages
            .SweepAsync(Clock.GetUtcNow() - options.RawPageWindow, cancellationToken)
            .ConfigureAwait(false);

        if (swept > 0 || pages > 0)
        {
            logger.LogInformation(
                "Компакция: убрано {Swept} неподтверждённых файлов, {Pages} сырых страниц", swept, pages);
        }

        return new CompactionCycle(swept, pages);
    }
}
