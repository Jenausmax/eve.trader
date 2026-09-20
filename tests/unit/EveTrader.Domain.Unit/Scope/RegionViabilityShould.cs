using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using Shouldly;

namespace EveTrader.Domain.Unit.Scope;

/// <summary>Сценарии §«Набор регионов определяется эмпирически».</summary>
public sealed class RegionViabilityShould
{
    private static readonly RegionId Forge = RegionId.From(10000002);
    private static readonly RegionId Fresh = RegionId.From(11000099);

    [Fact]
    public void AcceptANewSpaceWithoutTouchingConfiguration()
    {
        var viability = new RegionViability();

        // Источник назвал регион, которого раньше не было.
        viability.Announced(Fresh);

        viability.Known.ShouldContain(Fresh);

        // Названный — ещё не пригодный: пригодность даёт непустой стакан, а не анонс.
        viability.IsViable(Fresh).ShouldBeFalse();
        viability.Barren.ShouldContain(Fresh);

        viability.Observed(Fresh, orderCount: 120);

        viability.IsViable(Fresh).ShouldBeTrue();
        viability.Viable.ShouldContain(Fresh);
        viability.Barren.ShouldNotContain(Fresh);
    }

    [Fact]
    public void ExcludeARegionWhoseBookIsAlwaysEmptyAndSaySo()
    {
        var viability = new RegionViability();

        viability.Observed(Forge, orderCount: 5000);
        viability.Observed(Fresh, orderCount: 0);
        viability.Observed(Fresh, orderCount: 0);

        viability.Viable.ShouldBe([Forge]);

        // Исключение записано, а не молчаливо: пустой регион перечислим.
        viability.Barren.ShouldBe([Fresh]);
    }

    [Fact]
    public void NotForgetViabilityAfterALaterEmptyObservation()
    {
        var viability = new RegionViability();

        viability.Observed(Forge, orderCount: 5000);
        viability.Observed(Forge, orderCount: 0);

        // Один пустой ответ не делает регион непригодным — он делает его тихим.
        viability.IsViable(Forge).ShouldBeTrue();
    }
}
