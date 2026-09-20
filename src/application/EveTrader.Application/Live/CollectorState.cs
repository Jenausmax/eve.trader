using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Live;

/// <summary>
/// Состояние сбора между циклами: наблюдатели по регионам, валидаторы кэша и отметки о
/// том, с какого момента регион в охвате.
///
/// Наблюдатели живут между циклами, и это не оптимизация: исчезновение выводится не из
/// пары снимков, а из нескольких подряд, и наблюдатель, созданный заново на каждый цикл,
/// не подтвердил бы ни одного.
/// </summary>
public sealed class CollectorState(DiffOptions diff, FeatureOptions features)
{
    private readonly Dictionary<RegionId, RegionObserver> observers = [];

    private readonly Dictionary<RegionId, DateTimeOffset> baselineSince = [];

    /// <summary>Валидаторы кэша по регионам: с ними источник вправе ответить «не изменилось».</summary>
    public Dictionary<RegionId, string?> Validators { get; } = [];

    public RegionObserver ObserverFor(RegionId region)
    {
        if (!observers.TryGetValue(region, out RegionObserver? observer))
        {
            observer = new RegionObserver(region, diff, features);
            observers[region] = observer;
        }

        return observer;
    }

    /// <summary>
    /// Первое ли это наблюдение региона с момента его входа в охват.
    ///
    /// Такое наблюдение — базовая линия: регион, вернувшийся после паузы, наблюдается
    /// впервые, и его стакан не есть массовое появление ордеров.
    /// </summary>
    public bool IsFirstSinceScopeChange(RegionId region, DateTimeOffset? inScopeSince)
    {
        if (inScopeSince is not { } entered)
        {
            return false;
        }

        if (baselineSince.TryGetValue(region, out DateTimeOffset recorded) && recorded == entered)
        {
            return false;
        }

        baselineSince[region] = entered;

        return true;
    }
}
