using EveTrader.Domain.Facts;

namespace EveTrader.Application.Live;

/// <summary>
/// Состояние сбора между циклами: валидаторы кэша и отметки о том, с какого момента
/// регион в охвате.
///
/// Наблюдатели здесь не живут — они у приёма, общие на все источники. Здесь только то,
/// что принадлежит добыче и обязано пережить цикл: без валидатора условный запрос
/// перестал бы работать после первого же тика.
/// </summary>
public sealed class CollectorState
{
    private readonly Dictionary<RegionId, DateTimeOffset> baselineSince = [];

    /// <summary>Валидаторы кэша по регионам: с ними источник вправе ответить «не изменилось».</summary>
    public Dictionary<RegionId, string?> Validators { get; } = [];

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
