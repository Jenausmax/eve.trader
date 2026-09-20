using EveTrader.Application.BackgroundServices;

namespace EveTrader.Application.Live;

/// <summary>Счётчики одного цикла сбора.</summary>
/// <param name="Observed">Полных наблюдений.</param>
/// <param name="Unchanged">Ответов «не изменилось».</param>
/// <param name="Partial">Частичных наблюдений.</param>
/// <param name="Failed">Отказов и пропусков по бюджету.</param>
/// <param name="Events">Порождённых событий.</param>
public sealed record CollectionCycle(
    [property: Counter("observed")] int Observed,
    [property: Counter("unchanged")] int Unchanged,
    [property: Counter("partial")] int Partial,
    [property: Counter("failed")] int Failed,
    [property: Counter("events")] int Events) : ICycleCounters;
