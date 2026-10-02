namespace EveTrader.Domain.Series;

/// <summary>
/// Вид производного признака.
///
/// Все пять выводятся из событий жизни ордера, а не из дневного агрегата: агрегат не
/// отличает продажу от отмены, а станционная торговля живёт ровно на этом различии.
/// </summary>
public enum SeriesKind
{
    /// <summary>Наблюдённый исполненный объём за окно, в единицах товара.</summary>
    ObservedTurnover = 0,

    /// <summary>Перестановок цены за окно, без пополнений склада NPC.</summary>
    RelistPressure = 1,

    /// <summary>Доля исчезновений, которым предшествовало наблюдённое исполнение.</summary>
    FilledDisappearanceShare = 2,

    /// <summary>Средняя длительность удержания лучшей цены за окно, в секундах.</summary>
    BestPriceHold = 3,

    /// <summary>Ордеров в пределах полосы от лучшей цены, усреднённо за окно.</summary>
    CompetitorDepth = 4,
}
