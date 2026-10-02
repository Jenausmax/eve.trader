using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Domain.Unit.Signals;

/// <summary>
/// Набор параметров правила и его имя.
///
/// Имя выводится из значений намеренно. С именем, которое надо не забыть поменять, его
/// однажды не поменяют, и два разных правила съедутся в одну оценку качества под одним
/// именем — а оценка качества, считающая два правила за одно, хуже отсутствующей.
/// </summary>
public sealed class StationTradingParametersShould
{
    private static StationTradingParameters Set(string label = "jita", decimal margin = 0.05m) =>
        StationTradingParameters.Of(
            label,
            FeeSchedule.Of(0.025m, 0.036m, 0.005m),
            TimeSpan.FromHours(24),
            minNetMarginRate: margin,
            minObservedTurnover: 100,
            competitorBandBasisPoints: 100,
            maxCompetitors: 5,
            maxRelistPressure: 50,
            expectedBuyRelists: 2,
            expectedSellRelists: 2);

    [Fact]
    public void CarryTheOperatorLabelInItsName() =>
        Set().Name.Value.ShouldStartWith("jita-");

    [Fact]
    public void GiveTheSameNameToTheSameValues() =>
        Set().Name.ShouldBe(Set().Name);

    [Fact]
    public void GiveADifferentNameWhenAThresholdChanges() =>
        Set(margin: 0.05m).Name.ShouldNotBe(Set(margin: 0.06m).Name);

    [Fact]
    public void KeepTheFingerprintWhenOnlyTheLabelChanges()
    {
        // Метка — читаемая часть, отпечаток — существо. Два набора с одинаковыми
        // порогами под разными метками видны как одинаковые по существу.
        StationTradingParameters.Digest(Set("jita"))
            .ShouldBe(StationTradingParameters.Digest(Set("amarr")));

        Set("jita").Name.ShouldNotBe(Set("amarr").Name);
    }

    [Fact]
    public void ProduceAFingerprintThatSurvivesARestart() =>
        // Значение выписано литералом: встроенный хеш строки рандомизирован на процесс,
        // и подмена SHA-256 на него сломает сравнение прогонов между запусками молча.
        // Этот тест — единственное место, где такая подмена станет видна.
        StationTradingParameters.Digest(Set()).ShouldBe("946d015e");

    [Fact]
    public void RefuseAnEmptyLabel() =>
        Should.Throw<ArgumentException>(static () => Set(label: "  "));

    [Fact]
    public void RefuseANonPositiveWindow() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => StationTradingParameters.Of(
            "jita", FeeSchedule.Of(0m, 0m, 0m), TimeSpan.Zero, 0.05m, 100, 100, 5, 50, 2, 2));
}
