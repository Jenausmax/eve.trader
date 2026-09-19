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
/// <param name="KnownSince">
/// Брать только то, что источник узнал позже этого момента. Так досинхронизация не
/// переписывает уже имеющееся: строка, известная источнику раньше, у нас уже есть.
/// </param>
public sealed record MarketHistoryScope(
    TimeRange Within,
    IReadOnlyList<RegionId> Regions,
    IReadOnlyList<int> Types,
    DateTimeOffset? KnownSince);
