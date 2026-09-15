namespace EveTrader.Application.BackgroundServices;

/// <summary>Один прогон на старте хоста, продолжения нет.</summary>
public sealed class StartupSchedule : WorkerSchedule
{
    public static StartupSchedule Instance { get; } = new();

    private StartupSchedule()
    {
    }

    public override TimeSpan NextDelay(WorkerCycleOutcome outcome) => Timeout.InfiniteTimeSpan;

    public override string Describe() => "once on startup";
}
