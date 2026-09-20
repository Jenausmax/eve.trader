using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using Shouldly;

namespace EveTrader.Domain.Unit.Scope;

/// <summary>Сценарии §«Изменение охвата фиксируется во времени».</summary>
public sealed class ScopeHistoryShould
{
    private static readonly RegionId Forge = RegionId.From(10000002);
    private static readonly RegionId Domain = RegionId.From(10000043);
    private static readonly RegionId[] Hubs = [Forge, Domain];
    private static readonly RegionId[] Viable = [Forge, Domain];

    private static DateTimeOffset Day(int day) => new(2026, 1, day, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Pace = TimeSpan.FromMinutes(5);

    [Fact]
    public void RememberWhenARegionEnteredScope()
    {
        var history = new ScopeHistory();
        history.Record(new PolicyChange(ScopePolicy.Single(Forge, Pace), Day(1)));
        history.Record(new PolicyChange(ScopePolicy.Hubs(Pace), Day(5)));

        history.InScopeSince(Forge, Day(9), Viable, Hubs).ShouldBe(Day(1));

        // Второй хаб вошёл в охват позже — его первое наблюдение с этого момента.
        history.InScopeSince(Domain, Day(9), Viable, Hubs).ShouldBe(Day(5));
    }

    [Fact]
    public void ForgetTheCountWhenARegionLeftAndCameBack()
    {
        var history = new ScopeHistory();
        history.Record(new PolicyChange(ScopePolicy.Hubs(Pace), Day(1)));
        history.Record(new PolicyChange(ScopePolicy.Single(Forge, Pace), Day(3)));
        history.Record(new PolicyChange(ScopePolicy.Hubs(Pace), Day(7)));

        // Регион выключали и включили снова: он наблюдается впервые с момента возврата,
        // и его первое наблюдение обязано быть базовой линией.
        history.InScopeSince(Domain, Day(9), Viable, Hubs).ShouldBe(Day(7));

        // Тот, что не выключался, сохраняет исходный отсчёт.
        history.InScopeSince(Forge, Day(9), Viable, Hubs).ShouldBe(Day(1));
    }

    [Fact]
    public void ReportARegionOutOfScopeAsAbsentRatherThanNew()
    {
        var history = new ScopeHistory();
        history.Record(new PolicyChange(ScopePolicy.Hubs(Pace), Day(1)));
        history.Record(new PolicyChange(ScopePolicy.Single(Forge, Pace), Day(3)));

        history.InScopeSince(Domain, Day(5), Viable, Hubs).ShouldBeNull();
    }

    [Fact]
    public void ResolveThePolicyThatWasInForceAtAnInstant()
    {
        var history = new ScopeHistory();
        history.Record(new PolicyChange(ScopePolicy.Hubs(Pace), Day(1)));
        history.Record(new PolicyChange(ScopePolicy.AllViable(Pace), Day(5)));

        history.At(Day(3))!.Kind.ShouldBe(ScopeKind.Hubs);
        history.At(Day(6))!.Kind.ShouldBe(ScopeKind.AllViable);

        // До первой записи охвата не было вовсе — это не то же, что пустой охват.
        history.At(Day(1).AddSeconds(-1)).ShouldBeNull();
    }
}
