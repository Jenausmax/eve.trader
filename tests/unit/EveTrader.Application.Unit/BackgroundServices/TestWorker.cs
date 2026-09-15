using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveTrader.Application.Unit.BackgroundServices;

/// <summary>
/// Воркер под тесты базы: работа цикла задаётся делегатом, расписание — снаружи.
/// </summary>
internal sealed class TestWorker(
    IServiceProvider services,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    WorkerSchedule schedule,
    Func<WorkerContext, int, object?> cycle)
    : ScheduledWorkerBase(services, clock, diagnostics, NullLogger.Instance)
{
    private int cycleNumber;

    public int CyclesRun => cycleNumber;

    protected override string WorkerName => "Test";

    protected override WorkerSchedule Schedule => schedule;

    protected override Task<object?> ExecuteCycleAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref cycleNumber);

        return Task.FromResult(cycle(context, current));
    }
}
