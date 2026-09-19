using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Признаки стакана по паре «тип и локация» на один момент наблюдения.
///
/// Отсутствие стороны записано как отсутствие, а не как нулевая цена: ноль — это цена,
/// и модель, обученная на нулях вместо пропусков, решит, что товар иногда отдают даром.
/// </summary>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="LocationId">Локация.</param>
/// <param name="BestBid">Лучшая цена покупки.</param>
/// <param name="BestAsk">Лучшая цена продажи.</param>
/// <param name="BuyOrders">Число ордеров на покупку.</param>
/// <param name="SellOrders">Число ордеров на продажу.</param>
/// <param name="BuyDepth">Доступный объём покупки по каждому порогу.</param>
/// <param name="SellDepth">Доступный объём продажи по каждому порогу.</param>
/// <param name="ObservedAt">Время наблюдения, из которого признаки получены.</param>
/// <param name="Observation">Наблюдение, подтверждающее признаки.</param>
/// <param name="Incomplete">
/// Признаки вычислены по частичному наблюдению. Гарантия совпадения при перестройке на
/// них не распространяется: состав стакана на этот момент по сырью не воспроизводим.
/// </param>
public sealed record BookFeatures(
    int TypeId,
    long LocationId,
    IskPrice? BestBid,
    IskPrice? BestAsk,
    int BuyOrders,
    int SellOrders,
    IReadOnlyList<long> BuyDepth,
    IReadOnlyList<long> SellDepth,
    DateTimeOffset ObservedAt,
    ObservationId Observation,
    bool Incomplete)
{
    /// <summary>
    /// Спред в сотых долях ISK; <see langword="null" />, если одной из сторон нет —
    /// спред между ценой и её отсутствием не определён.
    /// </summary>
    public long? SpreadCents =>
        BestAsk is { } ask && BestBid is { } bid ? ask.Cents - bid.Cents : null;

    /// <summary>Стакан двусторонний: есть и покупка, и продажа.</summary>
    public bool IsTwoSided => BestBid is not null && BestAsk is not null;
}
