using EveTrader.Application.BackgroundServices;

namespace EveTrader.Application.Workers;

/// <summary>Счётчики цикла досинхронизации истории.</summary>
public sealed record DailyHistoryCycle(
    [property: Counter("days_written")] int DaysWritten,
    [property: Counter("days_unchanged")] int DaysUnchanged,
    [property: Counter("rows")] int Rows) : ICycleCounters;
