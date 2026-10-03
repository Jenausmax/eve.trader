using EveTrader.Domain.Book;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Обоснование: значения признаков, при которых правило решало, и пороги, с которыми
/// оно их сравнивало.
///
/// Оператор, который не может проверить, почему ему это показали, не может и отличить
/// находку от ошибки в пороге. Поэтому пороги и ставки лежат здесь же, а не только в
/// имени набора: ошибка в ставке видна сразу, а не через месяц торговли.
/// </summary>
/// <param name="BestBid">Лучшая цена покупки — по ней ставится ордер на покупку.</param>
/// <param name="BestAsk">Лучшая цена продажи — по ней ставится ордер на продажу.</param>
/// <param name="QuoteObservedAt">Когда снят снимок, давший цены.</param>
/// <param name="NetMargin">Маржа на единицу после комиссий.</param>
/// <param name="NetMarginRate">Маржа после комиссий как доля от затрат на покупку.</param>
/// <param name="BuyTurnover">Наблюдённый оборот стороны покупки за окно, в единицах.</param>
/// <param name="SellTurnover">Наблюдённый оборот стороны продажи за окно, в единицах.</param>
/// <param name="BuyCompetitors">Ордеров покупки в полосе, в среднем за окно.</param>
/// <param name="SellCompetitors">Ордеров продажи в полосе, в среднем за окно.</param>
/// <param name="BuyRelists">Перестановок цены на стороне покупки за окно.</param>
/// <param name="SellRelists">Перестановок цены на стороне продажи за окно.</param>
/// <param name="FilledShare">
/// Доля исчезновений с предшествующим исполнением — размер слепой зоны наблюдения; не
/// условие, а предупреждение. <see langword="null" />, если исчезновений не было.
/// </param>
/// <param name="Parameters">Пороги и ставки набора, которым принято решение.</param>
/// <param name="Series">Ключи рядов, из которых взяты значения.</param>
public sealed record SignalJustification(
    IskPrice BestBid,
    IskPrice BestAsk,
    DateTimeOffset QuoteObservedAt,
    IskAmount NetMargin,
    decimal NetMarginRate,
    double BuyTurnover,
    double SellTurnover,
    double BuyCompetitors,
    double SellCompetitors,
    double BuyRelists,
    double SellRelists,
    double? FilledShare,
    StationTradingParameters Parameters,
    IReadOnlyList<string> Series);
