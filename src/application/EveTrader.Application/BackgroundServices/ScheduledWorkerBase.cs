using EveTrader.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// База периодических воркеров. Наследник описывает только работу одного цикла;
/// петля, DI-scope на цикл, эмиссия счётчиков, политика ошибок и отмена — здесь.
///
/// Требование <c>.agents/rules/csharp/background-workers.md</c>: все периодические
/// воркеры наследуют эту базу, а не <see cref="BackgroundService" /> напрямую.
///
/// Источник телеметрии допускает <see langword="null" /> — хост без телеметрии тикает
/// так же, просто молча.
/// </summary>
public abstract class ScheduledWorkerBase(
    IServiceProvider services,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    ILogger logger) : BackgroundService
{
    /// <summary>
    /// Часы. Наследникам они нужны так же, как базе, а второй <see cref="TimeProvider" />
    /// полем разошёлся бы с этим при подмене в тесте — и разошёлся бы молча.
    /// </summary>
    protected TimeProvider Clock => clock;

    /// <summary>Имя воркера в логах и в именах счётчиков.</summary>
    protected abstract string WorkerName { get; }

    /// <summary>Форма расписания.</summary>
    protected abstract WorkerSchedule Schedule { get; }

    /// <summary>
    /// Работа одного цикла. Возвращает результат со счётчиками
    /// (<see cref="ICycleCounters" />) либо <see langword="null" />, если считать нечего.
    /// </summary>
    protected abstract Task<object?> ExecuteCycleAsync(WorkerContext context, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerName} started, schedule: {Schedule}", WorkerName, Schedule.Describe());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                object? counters = null;
                var failed = false;

                await using (AsyncServiceScope scope = services.CreateAsyncScope())
                {
                    IOperationScope? operation = diagnostics?.Operation($"worker.{WorkerName}.cycle");

                    try
                    {
                        counters = await ExecuteCycleAsync(new WorkerContext(scope.ServiceProvider), stoppingToken)
                            .ConfigureAwait(false);
                        operation?.Succeeded();
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        operation?.Dispose();
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // База не роняет хост: цикл упал — логируем, считаем отказ и идём дальше.
                        failed = true;
                        logger.LogError(exception, "Worker {WorkerName} cycle failed", WorkerName);
                        operation?.Failed(exception);
                    }
                    finally
                    {
                        operation?.Dispose();
                    }
                }

                var didWork = WorkerCounters.Emit(diagnostics, WorkerName, counters);
                WorkerCounters.EmitOutcome(diagnostics, WorkerName, failed);

                TimeSpan delay = Schedule.NextDelay(new WorkerCycleOutcome(failed, didWork));
                if (delay == Timeout.InfiniteTimeSpan)
                {
                    break;
                }

                await Task.Delay(delay, clock, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Остановка хоста — штатный выход, а не отказ.
        }
        finally
        {
            logger.LogInformation("Worker {WorkerName} stopped", WorkerName);
        }
    }
}
