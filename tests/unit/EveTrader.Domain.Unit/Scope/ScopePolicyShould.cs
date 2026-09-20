using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using Shouldly;

namespace EveTrader.Domain.Unit.Scope;

/// <summary>Сценарии спеки <c>market-observation/scope-policy</c>.</summary>
public sealed class ScopePolicyShould
{
    private static readonly RegionId Forge = RegionId.From(10000002);
    private static readonly RegionId Domain = RegionId.From(10000043);
    private static readonly RegionId Heimatar = RegionId.From(10000030);
    private static readonly RegionId Quiet = RegionId.From(10000070);

    private static readonly RegionId[] Hubs = [Forge, Domain, Heimatar];

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void TakeOnlyHubsWhenThePolicySaysHubs()
    {
        IReadOnlyList<RegionId> chosen = ScopePolicy.Hubs(FiveMinutes)
            .Resolve([Forge, Domain, Heimatar, Quiet], Hubs);

        chosen.ShouldBe([Forge, Heimatar, Domain]);
        chosen.ShouldNotContain(Quiet);
    }

    [Fact]
    public void TakeOnlyOneRegionWhenThePolicySaysSingle() =>
        ScopePolicy.Single(Forge, FiveMinutes)
            .Resolve([Forge, Domain, Quiet], Hubs)
            .ShouldBe([Forge]);

    [Fact]
    public void TakeEveryViableRegionWhenThePolicySaysAll() =>
        ScopePolicy.AllViable(FiveMinutes)
            .Resolve([Forge, Domain, Quiet], Hubs)
            .ShouldBe([Forge, Domain, Quiet]);

    [Fact]
    public void NeverTakeARegionTheSourceDoesNotOffer()
    {
        // Хаб, которого источник не даёт, в расписание не попадает: охват определяется
        // пригодностью, а не списком.
        ScopePolicy.Hubs(FiveMinutes)
            .Resolve([Forge], Hubs)
            .ShouldBe([Forge]);
    }

    [Fact]
    public void AllowADifferentPaceForEachRegion()
    {
        ScopePolicy policy = ScopePolicy.AllViable(FiveMinutes) with
        {
            PerRegionInterval = new Dictionary<RegionId, TimeSpan> { [Quiet] = TimeSpan.FromMinutes(60) },
        };

        policy.IntervalFor(Forge).ShouldBe(FiveMinutes);
        policy.IntervalFor(Quiet).ShouldBe(TimeSpan.FromMinutes(60));
    }
}
