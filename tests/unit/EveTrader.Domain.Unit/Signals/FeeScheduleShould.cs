using System.Reflection;
using EveTrader.Domain.Book;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Domain.Unit.Signals;

/// <summary>
/// Комиссии станционной торговли.
///
/// Главное здесь — не арифметика, а отсутствие умолчаний. Ставки зависят от навыков и
/// репутации игрока; захардкоженное значение выглядело бы удобством, а работало бы как
/// систематическое завышение маржи, потому что разница цен и суммарная комиссия на
/// станционной торговле — величины одного порядка.
/// </summary>
public sealed class FeeScheduleShould
{
    /// <summary>Ставки игрока со средней прокачкой: брокер 2.5 %, налог 3.6 %, перестановка 0.5 %.</summary>
    private static readonly FeeSchedule Trader = FeeSchedule.Of(0.025m, 0.036m, 0.005m);

    [Fact]
    public void TakeBrokerFeeOnBothSidesAndSalesTaxOnTheSale()
    {
        // Покупка по 100: 100 + 2.5 % = 102.50 затрат.
        Trader.BuyCost(IskPrice.FromIsk(100m), relists: 0).ToIsk().ShouldBe(102.50m);

        // Продажа по 110: 110 − 2.5 % − 3.6 % = 103.29 выручки.
        Trader.SellProceeds(IskPrice.FromIsk(110m), relists: 0).ToIsk().ShouldBe(103.29m);

        Trader.NetMargin(IskPrice.FromIsk(100m), IskPrice.FromIsk(110m), 0, 0)
            .ToIsk().ShouldBe(0.79m);
    }

    [Fact]
    public void TurnAProfitableLookingSpreadIntoALoss()
    {
        // Валовая разница 5 ISK на сотне выглядит пятью процентами прибыли. После
        // комиссий остаётся минус: ровно тот случай, ради которого ставки и заводятся.
        IskAmount gross = IskAmount.FromIsk(105m) - IskAmount.FromIsk(100m);
        gross.IsPositive.ShouldBeTrue();

        IskAmount net = Trader.NetMargin(IskPrice.FromIsk(100m), IskPrice.FromIsk(105m), 0, 0);

        net.IsPositive.ShouldBeFalse();
        net.ToIsk().ShouldBe(-3.90m);
    }

    [Fact]
    public void ChargeEveryRelistOnBothSides()
    {
        IskAmount once = Trader.NetMargin(IskPrice.FromIsk(100m), IskPrice.FromIsk(110m), 0, 0);
        IskAmount after = Trader.NetMargin(IskPrice.FromIsk(100m), IskPrice.FromIsk(110m), 3, 4);

        // Три перестановки покупки по 0.5 % от 100 и четыре продажи по 0.5 % от 110.
        (once - after).ToIsk().ShouldBe((3 * 0.5m) + (4 * 0.55m));
    }

    [Fact]
    public void RefuseARateOutsideItsRange()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(static () => FeeSchedule.Of(1.5m, 0.036m, 0.005m));
        _ = Should.Throw<ArgumentOutOfRangeException>(static () => FeeSchedule.Of(0.025m, -0.01m, 0.005m));
    }

    [Fact]
    public void RefuseANegativeRelistCount() =>
        Should.Throw<ArgumentOutOfRangeException>(
            static () => Trader.BuyCost(IskPrice.FromIsk(100m), relists: -1));

    [Fact]
    public void OfferNoDefaultSchedule()
    {
        // Проверяется рефлексией, а не глазами: умолчание, добавленное «для удобства»
        // через полгода, иначе проедет молча и завысит маржу во всех прогонах разом.
        PropertyInfo[] defaults = [.. typeof(FeeSchedule)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(static property => property.PropertyType == typeof(FeeSchedule))];

        defaults.ShouldBeEmpty();
    }
}
