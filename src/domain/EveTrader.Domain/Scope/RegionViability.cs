using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Scope;

/// <summary>
/// Пригодность регионов, определяемая эмпирически: пригоден тот, в котором хотя бы одно
/// наблюдение дало непустой стакан.
///
/// Список в конфигурации тут не годится — он устаревает молча. Источник добавляет
/// пространства, регионы пустеют, и узнать об этом можно только наблюдением.
/// </summary>
public sealed class RegionViability
{
    private readonly Dictionary<RegionId, int> ordersSeen = [];

    private readonly HashSet<RegionId> known = [];

    /// <summary>Регионы, которые источник вообще называет.</summary>
    public IReadOnlyCollection<RegionId> Known => known;

    /// <summary>Регионы, где хотя бы раз был непустой стакан.</summary>
    public IReadOnlyCollection<RegionId> Viable =>
        [.. ordersSeen.Where(static pair => pair.Value > 0).Select(static pair => pair.Key).Order()];

    /// <summary>
    /// Регионы, которые источник называет, но стакан в которых систематически пуст.
    /// Исключение из расписания записывается, а не происходит молча.
    /// </summary>
    public IReadOnlyCollection<RegionId> Barren =>
        [.. known.Where(region => !ordersSeen.TryGetValue(region, out var seen) || seen == 0).Order()];

    /// <summary>Регион, названный источником. Пригодным он от этого ещё не становится.</summary>
    public void Announced(RegionId region)
    {
        _ = known.Add(region);
        _ = ordersSeen.TryAdd(region, 0);
    }

    public void Observed(RegionId region, int orderCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orderCount);

        Announced(region);
        ordersSeen[region] = Math.Max(ordersSeen[region], orderCount);
    }

    public bool IsViable(RegionId region) => ordersSeen.TryGetValue(region, out var seen) && seen > 0;
}
