using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Integration.Workers;

/// <summary>
/// Воркер, отрабатывающий заданное число циклов и сообщающий об этом наружу.
/// Без явного сигнала тест не знает, когда хост уже сделал работу, и превращается
/// в гонку с таймаутом.
/// </summary>
internal sealed class TickingWorker(
    IServiceProvider services,
    TimeProvider clock,
    IDiagnosticSource diagnostics,
    ILogger<TickingWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    private const int TargetCycles = 3;

    private readonly TaskCompletionSource completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int cycles;

    public Task Completed => completed.Task;

    protected override string WorkerName => "Ticking";

    protected override WorkerSchedule Schedule => new IntervalSchedule(TimeSpan.FromMilliseconds(1));

    protected override Task<object?> ExecuteCycleAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref cycles);

        if (current >= TargetCycles)
        {
            _ = completed.TrySetResult();
        }

        return Task.FromResult<object?>(new TickCounters(Ticked: 1));
    }

    private sealed record TickCounters([property: Counter("ticked")] int Ticked) : ICycleCounters;
}
