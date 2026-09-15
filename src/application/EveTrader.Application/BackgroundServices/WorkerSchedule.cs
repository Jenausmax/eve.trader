namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Форма расписания воркера. Выбор формы — решение из
/// <c>.agents/rules/csharp/background-workers.md</c> §«Schedule — выбор формы».
/// </summary>
public abstract class WorkerSchedule
{
    /// <summary>
    /// Пауза до следующего цикла. <see cref="Timeout.InfiniteTimeSpan" /> означает,
    /// что продолжения не будет и цикл завершён.
    /// </summary>
    public abstract TimeSpan NextDelay(WorkerCycleOutcome outcome);

    /// <summary>Описание для стартового лога: «every 00:05:00», «once on startup».</summary>
    public abstract string Describe();
}
