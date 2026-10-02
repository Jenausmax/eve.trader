using EveTrader.Application.BackgroundServices;

namespace EveTrader.Application.Workers;

/// <summary>Счётчики цикла материализации рядов.</summary>
public sealed record SeriesCycle(
    [property: Counter("series_windows")] int Windows,
    [property: Counter("series_windows_refused")] int Refused,
    [property: Counter("series_points")] int Points) : ICycleCounters;
