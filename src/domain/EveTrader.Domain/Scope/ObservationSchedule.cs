using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Scope;

/// <summary>
/// Расписание наблюдения регионов.
///
/// Ведётся по заголовкам ответа, а не по сетке: общего среза мира не существует, у
/// регионов разный темп, и граница поколения кэша смещается на секунды. Расчётная сетка
/// поэтому либо опаздывает, либо стучится раньше срока — и второе источник считает
/// невежливостью.
/// </summary>
public sealed class ObservationSchedule
{
    private readonly Dictionary<RegionId, DateTimeOffset> sourceExpiry = [];

    private readonly Dictionary<RegionId, DateTimeOffset> lastObserved = [];

    /// <summary>Срок годности, объявленный источником в ответе по региону.</summary>
    public void SourceExpires(RegionId region, DateTimeOffset expiresAt) =>
        sourceExpiry[region] = expiresAt;

    public void Observed(RegionId region, DateTimeOffset observedAt) =>
        lastObserved[region] = observedAt;

    /// <summary>
    /// Когда регион можно наблюдать снова.
    ///
    /// Срок — максимум из объявленного источником срока годности и интервала политики:
    /// политика вправе наблюдать реже, но не чаще, чем разрешает источник. Регион, не
    /// наблюдавшийся ни разу, доступен немедленно.
    /// </summary>
    public RegionDue DueFor(RegionId region, ScopePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        DateTimeOffset byPolicy = lastObserved.TryGetValue(region, out DateTimeOffset last)
            ? last + policy.IntervalFor(region)
            : DateTimeOffset.MinValue;

        return !sourceExpiry.TryGetValue(region, out DateTimeOffset expiry)
            ? new RegionDue(region, byPolicy, HeldBySource: false)
            : expiry > byPolicy
            ? new RegionDue(region, expiry, HeldBySource: true)
            : new RegionDue(region, byPolicy, HeldBySource: false);
    }

    /// <summary>
    /// Регионы, которые пора наблюдать, в порядке наступления срока. Диспетчер берёт
    /// именно их: общего такта нет, и «все разом» не бывает.
    /// </summary>
    public IReadOnlyList<RegionDue> Due(
        IReadOnlyCollection<RegionId> regions,
        ScopePolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(regions);

        return [.. regions
            .Select(region => DueFor(region, policy))
            .Where(due => due.DueAt <= now)
            .OrderBy(due => due.DueAt)
            .ThenBy(due => due.Region.Value)];
    }

    /// <summary>
    /// Просит ли политика чаще, чем разрешает источник. Настройка не нарушается — срок
    /// выдерживается, — но о несогласованности надо сказать, иначе она тихо живёт годами.
    /// </summary>
    public static bool IsPolicyTooEager(TimeSpan policyInterval, TimeSpan sourceCache) =>
        policyInterval < sourceCache;
}
