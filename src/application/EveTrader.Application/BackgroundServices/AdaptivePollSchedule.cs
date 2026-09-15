using System.Globalization;

namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Опрос с тремя темпами: разгребать пока есть работа, ждать когда пусто, ждать
/// дольше после отказа. Форма для наблюдения регионов — их срок годности истекает
/// вразнобой, и общего такта не существует.
/// </summary>
public sealed class AdaptivePollSchedule : WorkerSchedule
{
    private readonly TimeSpan idle;
    private readonly TimeSpan busy;
    private readonly TimeSpan error;

    public AdaptivePollSchedule(TimeSpan idle, TimeSpan busy, TimeSpan error)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(idle, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(busy, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(error, TimeSpan.Zero);

        this.idle = idle;
        this.busy = busy;
        this.error = error;
    }

    public override TimeSpan NextDelay(WorkerCycleOutcome outcome) => outcome.Failed ? error : outcome.DidWork ? busy : idle;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"adaptive poll (idle {idle}, busy {busy}, error {error})");
}
