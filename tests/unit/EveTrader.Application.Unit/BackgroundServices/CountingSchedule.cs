using EveTrader.Application.BackgroundServices;

namespace EveTrader.Application.Unit.BackgroundServices;

/// <summary>
/// Расписание на N циклов без пауз, затем остановка. Без него петля базы крутится
/// вечно и тест не заканчивается.
/// </summary>
internal sealed class CountingSchedule(int cycles) : WorkerSchedule
{
    private int completed;

    public List<WorkerCycleOutcome> Outcomes { get; } = [];

    public override TimeSpan NextDelay(WorkerCycleOutcome outcome)
    {
        Outcomes.Add(outcome);
        completed++;

        return completed >= cycles ? Timeout.InfiniteTimeSpan : TimeSpan.Zero;
    }

    public override string Describe() => $"{cycles} cycle(s) then stop";
}
