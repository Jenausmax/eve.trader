using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Scope;

/// <summary>
/// История политик охвата. Отвечает на вопрос «что наблюдалось на такой-то момент» —
/// без неё пробел в данных неотличим от «регион тогда не был в охвате».
///
/// Смена охвата накопленного не трогает: история политик и история наблюдений — разные
/// вещи, и сужение охвата не отменяет того, что было собрано раньше.
/// </summary>
public sealed class ScopeHistory
{
    private readonly List<PolicyChange> changes = [];

    public IReadOnlyList<PolicyChange> Changes => changes;

    public void Record(PolicyChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        changes.Add(change);
        changes.Sort(static (left, right) => left.EffectiveFrom.CompareTo(right.EffectiveFrom));
    }

    /// <summary>Политика, действовавшая на момент; <see langword="null" />, если охвата тогда не было.</summary>
    public ScopePolicy? At(DateTimeOffset instant) =>
        changes.LastOrDefault(change => change.EffectiveFrom <= instant)?.Policy;

    /// <summary>
    /// С какого момента регион находится в охвате непрерывно. <see langword="null" />,
    /// если его в охвате нет.
    ///
    /// Нужно для базовой линии: регион, вернувшийся в охват после паузы, наблюдается
    /// впервые, и его первое наблюдение — калибровка, а не появление ордеров.
    /// </summary>
    public DateTimeOffset? InScopeSince(
        RegionId region,
        DateTimeOffset instant,
        IReadOnlyCollection<RegionId> viable,
        IReadOnlyCollection<RegionId> hubs)
    {
        DateTimeOffset? since = null;

        foreach (PolicyChange change in changes.Where(change => change.EffectiveFrom <= instant))
        {
            var inScope = change.Policy.Resolve(viable, hubs).Contains(region);

            // Пауза обнуляет отсчёт: регион, выключенный и включённый снова, наблюдается
            // впервые с момента возврата.
            since = inScope ? since ?? change.EffectiveFrom : null;
        }

        return since;
    }
}
