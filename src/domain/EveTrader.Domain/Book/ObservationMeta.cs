using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Что известно о самом наблюдении. Времени «сейчас» домен не знает — границы интервала
/// сбора приходят снаружи вместе с ордерами.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Observation">Наблюдение; оно подтверждает и события, и признаки.</param>
/// <param name="Collected">Границы интервала сбора: атомарного среза источник не даёт.</param>
/// <param name="IsComplete">Получены все страницы.</param>
/// <param name="Step">Объявленный источником шаг между наблюдениями.</param>
/// <param name="IsBaseline">Первое наблюдение региона после включения в охват.</param>
public sealed record ObservationMeta(
    RegionId Region,
    ObservationId Observation,
    TimeRange Collected,
    bool IsComplete,
    TimeSpan Step,
    bool IsBaseline);
