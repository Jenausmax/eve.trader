using System.Reflection;
using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace EveTrader.Application.Unit.BackgroundServices;

public sealed class ScheduledWorkerBaseShould
{
    [Fact]
    public async Task CreateFreshScopePerCycle()
    {
        var seen = new List<Guid>();
        TestWorker worker = BuildWorker(
            new CountingSchedule(cycles: 3),
            cycle: (context, _) =>
            {
                seen.Add(context.GetService<ScopeMarker>().Id);

                return null;
            });

        await RunAsync(worker, TestContext.Current.CancellationToken).ConfigureAwait(true);

        seen.Count.ShouldBe(3);
        seen.Distinct().Count().ShouldBe(3);
    }

    /// <summary>
    /// Счётчики проверяются через подписчика на инструменты, а не через подменённый
    /// источник: подмена доказывает, что метод позвали, а вопрос стоит в том, доехал ли
    /// замер до экспорта.
    /// </summary>
    [Fact]
    public async Task EmitCountersAndSuccessOutcomeWhenCycleSucceeds()
    {
        using var telemetry = new MeterDiagnosticSource(Isolated());
        using var export = new MetricExport(telemetry.ModuleName);

        TestWorker worker = BuildWorker(
            new CountingSchedule(cycles: 1),
            cycle: static (_, _) => new TestCounters(Processed: 7, Failed: 2),
            diagnostics: telemetry);

        await RunAsync(worker, TestContext.Current.CancellationToken).ConfigureAwait(true);

        export.TotalOf("worker.Test.processed").ShouldBe(7d);
        export.TotalOf("worker.Test.failed").ShouldBe(2d);
        export.TotalOf("worker.Test.outcome.succeeded").ShouldBe(1d);
        export.CountOf("worker.Test.outcome.failed").ShouldBe(0L);
    }

    [Fact]
    public async Task EmitFailureOutcomeWhenCycleThrows()
    {
        using var telemetry = new MeterDiagnosticSource(Isolated());
        using var export = new MetricExport(telemetry.ModuleName);

        TestWorker worker = BuildWorker(
            new CountingSchedule(cycles: 1),
            cycle: static (_, _) => throw new InvalidOperationException("цикл упал"),
            diagnostics: telemetry);

        await RunAsync(worker, TestContext.Current.CancellationToken).ConfigureAwait(true);

        export.TotalOf("worker.Test.outcome.failed").ShouldBe(1d);
        export.CountOf("worker.Test.outcome.succeeded").ShouldBe(0L);
    }

    /// <summary>
    /// Своё имя счётчика на тест: инструменты процессные, и тесты, идущие
    /// одновременно, складывали бы замеры друг другу.
    /// </summary>
    private static string Isolated() => $"EveTrader.Test.{Guid.NewGuid():N}";

    [Fact]
    public async Task ContinueToNextCycleAfterException()
    {
        var schedule = new CountingSchedule(cycles: 3);
        TestWorker worker = BuildWorker(
            schedule,
            cycle: static (_, number) => number == 1
                ? throw new InvalidOperationException("первый цикл падает")
                : null);

        await RunAsync(worker, TestContext.Current.CancellationToken).ConfigureAwait(true);

        worker.CyclesRun.ShouldBe(3);
        schedule.Outcomes[0].Failed.ShouldBeTrue();
        schedule.Outcomes[1].Failed.ShouldBeFalse();
        schedule.Outcomes[2].Failed.ShouldBeFalse();
    }

    [Fact]
    public async Task ReportWorkDoneOnlyWhenCounterIsPositive()
    {
        var schedule = new CountingSchedule(cycles: 2);
        TestWorker worker = BuildWorker(
            schedule,
            cycle: static (_, number) => new TestCounters(Processed: number == 1 ? 5 : 0, Failed: 0));

        await RunAsync(worker, TestContext.Current.CancellationToken).ConfigureAwait(true);

        schedule.Outcomes[0].DidWork.ShouldBeTrue();
        schedule.Outcomes[1].DidWork.ShouldBeFalse();
    }

    [Fact]
    public async Task StopWhenCancelled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        TestWorker worker = BuildWorker(
            new CountingSchedule(cycles: 100),
            cycle: (_, number) =>
            {
                if (number == 2)
                {
                    cancellation.Cancel();
                }

                return null;
            });

        await RunAsync(worker, cancellation.Token).ConfigureAwait(true);

        worker.CyclesRun.ShouldBe(2);
    }

    private static TestWorker BuildWorker(
        WorkerSchedule schedule,
        Func<WorkerContext, int, object?> cycle,
        IDiagnosticSource? diagnostics = null)
    {
        ServiceProvider services = new ServiceCollection()
            .AddScoped<ScopeMarker>()
            .BuildServiceProvider();

        return new TestWorker(services, TimeProvider.System, diagnostics, schedule, cycle);
    }

    /// <summary>
    /// <c>ExecuteAsync</c> защищённый — правило background-workers.md §6 предписывает
    /// звать его из тестов рефлексией, минуя жизненный цикл хоста.
    /// </summary>
    private static async Task RunAsync(TestWorker worker, CancellationToken cancellationToken)
    {
        MethodInfo execute = typeof(ScheduledWorkerBase)
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            .ShouldNotBeNull();

        await ((Task)execute.Invoke(worker, [cancellationToken])!).ConfigureAwait(true);
    }

    private sealed record TestCounters(
        [property: Counter("processed")] int Processed,
        [property: Counter("failed")] int Failed) : ICycleCounters;
}
