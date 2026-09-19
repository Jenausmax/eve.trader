using EveTrader.Domain.Facts;

namespace EveTrader.Application.History;

/// <summary>
/// Что именно просят у источника. Фильтр по регионам и типам назван явно —
/// это внешнее ограничение проекта, а не пожелание: полная история по всем типам во
/// всех регионах измеряется сотнями тысяч запросов.
/// </summary>
/// <param name="Within">Интервал рыночных дат.</param>
/// <param name="Regions">Регионы; пустой набор — все, какие даёт источник.</param>
/// <param name="Types">Типы; пустой набор — все, какие даёт источник.</param>
/// <param name="AlreadyLoaded">
/// Сутки, которые у нас уже есть, и момент их загрузки.
///
/// Знание посуточное, а не одним порогом на прогон, и это принципиально. Источник
/// правит отдельные сутки задним числом, поэтому «загружено ли» — свойство суток, а не
/// прогона. Отсюда обе выгоды сразу: условный запрос по каждым суткам не тянет
/// неизменившийся файл вовсе, а строки, которые источник узнал раньше нашей загрузки
/// этих суток, не переписываются.
/// </param>
public sealed record MarketHistoryScope(
    TimeRange Within,
    IReadOnlyList<RegionId> Regions,
    IReadOnlyList<int> Types,
    IReadOnlyDictionary<DateOnly, DateTimeOffset> AlreadyLoaded)
{
    /// <summary>Охват без ничего загруженного — первый прогон.</summary>
    public static MarketHistoryScope Fresh(TimeRange within) =>
        new(within, [], [], new Dictionary<DateOnly, DateTimeOffset>());

    /// <summary>Когда эти сутки загружались; <see langword="null" />, если ещё нет.</summary>
    public DateTimeOffset? LoadedAt(DateOnly day) =>
        AlreadyLoaded.TryGetValue(day, out DateTimeOffset at) ? at : null;
}
