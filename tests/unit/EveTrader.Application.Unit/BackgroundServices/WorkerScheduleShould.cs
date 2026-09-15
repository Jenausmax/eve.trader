using EveTrader.Application.BackgroundServices;
using Shouldly;

namespace EveTrader.Application.Unit.BackgroundServices;

public sealed class WorkerScheduleShould
{
    [Fact]
    public void KeepIntervalRegardlessOfOutcome()
    {
        var schedule = new IntervalSchedule(TimeSpan.FromMinutes(5));

        schedule.NextDelay(new WorkerCycleOutcome(Failed: false, DidWork: true)).ShouldBe(TimeSpan.FromMinutes(5));
        schedule.NextDelay(new WorkerCycleOutcome(Failed: true, DidWork: false)).ShouldBe(TimeSpan.FromMinutes(5));
        schedule.Describe().ShouldBe("every 00:05:00");
    }

    [Fact]
    public void PickAdaptiveDelayByOutcome()
    {
        var schedule = new AdaptivePollSchedule(
            idle: TimeSpan.FromSeconds(30),
            busy: TimeSpan.Zero,
            error: TimeSpan.FromMinutes(1));

        schedule.NextDelay(new WorkerCycleOutcome(Failed: false, DidWork: true)).ShouldBe(TimeSpan.Zero);
        schedule.NextDelay(new WorkerCycleOutcome(Failed: false, DidWork: false)).ShouldBe(TimeSpan.FromSeconds(30));
        schedule.NextDelay(new WorkerCycleOutcome(Failed: true, DidWork: true)).ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void StopAfterSingleRunOnStartup()
    {
        StartupSchedule.Instance
            .NextDelay(new WorkerCycleOutcome(Failed: false, DidWork: true))
            .ShouldBe(Timeout.InfiniteTimeSpan);

        StartupSchedule.Instance.Describe().ShouldBe("once on startup");
    }

    [Fact]
    public void RejectNonPositiveInterval() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => new IntervalSchedule(TimeSpan.Zero));
}
