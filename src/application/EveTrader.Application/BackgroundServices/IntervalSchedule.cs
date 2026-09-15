using System.Globalization;

namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Периодическая работа без привязки к стенным часам: раз в N времени, независимо
/// от того, была работа или нет.
/// </summary>
public sealed class IntervalSchedule : WorkerSchedule
{
    private readonly TimeSpan interval;

    public IntervalSchedule(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        this.interval = interval;
    }

    public override TimeSpan NextDelay(WorkerCycleOutcome outcome) => interval;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"every {interval}");
}
