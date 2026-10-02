using EveTrader.Domain.Book;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Ставки, которые игра удерживает со станционной торговли.
///
/// Значений по умолчанию здесь нет и быть не может: ставки зависят от навыков игрока и
/// его репутации на станции, одинаковых для всех не существует. Умолчание выглядело бы
/// удобством, а работало бы как тихое завышение маржи — на станционной торговле разница
/// цен и суммарная комиссия величины одного порядка.
/// </summary>
public sealed record FeeSchedule
{
    private FeeSchedule(decimal brokerFeeRate, decimal salesTaxRate, decimal relistFeeRate)
    {
        BrokerFeeRate = brokerFeeRate;
        SalesTaxRate = salesTaxRate;
        RelistFeeRate = relistFeeRate;
    }

    /// <summary>Доля от стоимости ордера, удерживаемая при его размещении. Платится с обеих сторон.</summary>
    public decimal BrokerFeeRate { get; }

    /// <summary>Доля от выручки, удерживаемая при исполнении ордера на продажу.</summary>
    public decimal SalesTaxRate { get; }

    /// <summary>Доля от стоимости ордера, удерживаемая за перестановку цены.</summary>
    public decimal RelistFeeRate { get; }

    public static FeeSchedule Of(decimal brokerFeeRate, decimal salesTaxRate, decimal relistFeeRate) =>
        new(Rate(brokerFeeRate, nameof(brokerFeeRate)),
            Rate(salesTaxRate, nameof(salesTaxRate)),
            Rate(relistFeeRate, nameof(relistFeeRate)));

    /// <summary>
    /// Затраты на покупку единицы: цена плюс брокерская комиссия за размещение ордера и
    /// за каждую перестановку.
    /// </summary>
    public IskAmount BuyCost(IskPrice buy, int relists) =>
        IskAmount.FromIsk(buy.ToIsk() * (1m + BrokerFeeRate + (Relists(relists) * RelistFeeRate)));

    /// <summary>
    /// Выручка с продажи единицы: цена минус брокерская комиссия за размещение, налог с
    /// продажи и комиссия за каждую перестановку.
    /// </summary>
    public IskAmount SellProceeds(IskPrice sell, int relists) =>
        IskAmount.FromIsk(sell.ToIsk() * (1m - BrokerFeeRate - SalesTaxRate - (Relists(relists) * RelistFeeRate)));

    /// <summary>
    /// Маржа на единицу после всех удержаний. Отрицательная означает, что комиссии съели
    /// разницу цен, — обычный исход на паре, которая выглядит прибыльной по валовой
    /// разнице.
    /// </summary>
    public IskAmount NetMargin(IskPrice buy, IskPrice sell, int buyRelists, int sellRelists) =>
        SellProceeds(sell, sellRelists) - BuyCost(buy, buyRelists);

    public static decimal Rate(decimal rate, string name) =>
        rate is >= 0m and <= 1m
            ? rate
            : throw new ArgumentOutOfRangeException(name, rate, "Ставка — доля от нуля до единицы");

    public static int Relists(int relists) =>
        relists >= 0
            ? relists
            : throw new ArgumentOutOfRangeException(nameof(relists), relists, "Число перестановок неотрицательно");
}
