using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Scope;

/// <summary>
/// Политика охвата: какие регионы наблюдаются живьём и с каким темпом.
///
/// Набор регионов политика называет не сама — она объявляет форму, а конкретные регионы
/// приходят от источника и отбираются по пригодности. Зашитый список устаревает молча:
/// источник добавляет пространства, регионы пустеют.
/// </summary>
/// <param name="Kind">Форма охвата.</param>
/// <param name="Named">Регионы для форм <see cref="ScopeKind.Regions" /> и <see cref="ScopeKind.Single" />.</param>
/// <param name="DefaultInterval">Темп по умолчанию.</param>
/// <param name="PerRegionInterval">Темп, заданный отдельно на регион.</param>
public sealed record ScopePolicy(
    ScopeKind Kind,
    IReadOnlyList<RegionId> Named,
    TimeSpan DefaultInterval,
    IReadOnlyDictionary<RegionId, TimeSpan> PerRegionInterval)
{
    /// <summary>Торговые хабы на минимальном темпе — то, с чего начинается сбор.</summary>
    public static ScopePolicy Hubs(TimeSpan interval) =>
        new(ScopeKind.Hubs, [], interval, new Dictionary<RegionId, TimeSpan>());

    public static ScopePolicy Single(RegionId region, TimeSpan interval) =>
        new(ScopeKind.Single, [region], interval, new Dictionary<RegionId, TimeSpan>());

    public static ScopePolicy AllViable(TimeSpan interval) =>
        new(ScopeKind.AllViable, [], interval, new Dictionary<RegionId, TimeSpan>());

    public static ScopePolicy Of(IReadOnlyList<RegionId> regions, TimeSpan interval) =>
        new(ScopeKind.Regions, regions, interval, new Dictionary<RegionId, TimeSpan>());

    /// <summary>Темп конкретного региона.</summary>
    public TimeSpan IntervalFor(RegionId region) =>
        PerRegionInterval.TryGetValue(region, out TimeSpan interval) ? interval : DefaultInterval;

    /// <summary>
    /// Регионы под наблюдением. <paramref name="viable" /> — те, что источник даёт и в
    /// которых хотя бы раз был непустой стакан; <paramref name="hubs" /> — объявленные
    /// торговые хабы.
    /// </summary>
    public IReadOnlyList<RegionId> Resolve(
        IReadOnlyCollection<RegionId> viable,
        IReadOnlyCollection<RegionId> hubs)
    {
        ArgumentNullException.ThrowIfNull(viable);
        ArgumentNullException.ThrowIfNull(hubs);

        IEnumerable<RegionId> chosen = Kind switch
        {
            ScopeKind.Hubs => hubs.Where(viable.Contains),
            ScopeKind.Regions or ScopeKind.Single => Named.Where(viable.Contains),
            ScopeKind.AllViable => viable,
            _ => throw new InvalidOperationException($"Неизвестная форма охвата: {Kind}"),
        };

        return [.. chosen.Distinct().Order()];
    }
}
