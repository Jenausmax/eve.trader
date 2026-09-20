using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using Shouldly;

namespace EveTrader.Domain.Unit.Scope;

/// <summary>Сценарии §«Темп наблюдения задаётся на регион».</summary>
public sealed class ObservationScheduleShould
{
    private static readonly RegionId Forge = RegionId.From(10000002);
    private static readonly RegionId Quiet = RegionId.From(10000070);

    private static DateTimeOffset At(int seconds) =>
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    [Fact]
    public void HoldAHubUntilTheSourceDeclaredExpiryPasses()
    {
        var schedule = new ObservationSchedule();
        var policy = ScopePolicy.Hubs(TimeSpan.FromMinutes(5));

        schedule.Observed(Forge, At(0));
        schedule.SourceExpires(Forge, At(300));

        RegionDue due = schedule.DueFor(Forge, policy);

        due.DueAt.ShouldBe(At(300));
        schedule.Due([Forge], policy, At(299)).ShouldBeEmpty();
        _ = schedule.Due([Forge], policy, At(300)).ShouldHaveSingleItem();
    }

    [Fact]
    public void KeepASparsePaceEvenWhenTheSourceExpiresEarlier()
    {
        var schedule = new ObservationSchedule();
        ScopePolicy policy = ScopePolicy.AllViable(TimeSpan.FromMinutes(5)) with
        {
            PerRegionInterval = new Dictionary<RegionId, TimeSpan> { [Quiet] = TimeSpan.FromMinutes(60) },
        };

        schedule.Observed(Quiet, At(0));
        schedule.SourceExpires(Quiet, At(300));

        RegionDue due = schedule.DueFor(Quiet, policy);

        // Источник разрешает через пять минут, политика просит раз в час — берём реже.
        due.DueAt.ShouldBe(At(3600));
        due.HeldBySource.ShouldBeFalse();
    }

    [Fact]
    public void FollowTheDeclaredExpiryWhenTheCacheGenerationBoundaryDrifts()
    {
        var schedule = new ObservationSchedule();
        var policy = ScopePolicy.Hubs(TimeSpan.FromMinutes(5));

        schedule.Observed(Forge, At(0));
        schedule.SourceExpires(Forge, At(300));
        schedule.DueFor(Forge, policy).DueAt.ShouldBe(At(300));

        // Граница поколения кэша сместилась на семь секунд — расписание идёт за ней,
        // а не за расчётной сеткой.
        schedule.SourceExpires(Forge, At(307));
        schedule.DueFor(Forge, policy).DueAt.ShouldBe(At(307));
    }

    [Fact]
    public void WaitOutTheSourceWhenThePolicyAsksTooOften()
    {
        var schedule = new ObservationSchedule();
        var policy = ScopePolicy.Hubs(TimeSpan.FromMinutes(1));

        schedule.Observed(Forge, At(0));
        schedule.SourceExpires(Forge, At(300));

        RegionDue due = schedule.DueFor(Forge, policy);

        // Настройка просит раз в минуту, источник держит пять: выдерживаем источник
        // и помечаем, что срок держит он.
        due.DueAt.ShouldBe(At(300));
        due.HeldBySource.ShouldBeTrue();

        // О несогласованности настройки можно сказать отдельно.
        ObservationSchedule.IsPolicyTooEager(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)).ShouldBeTrue();
        ObservationSchedule.IsPolicyTooEager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Fact]
    public void OfferAnUnobservedRegionImmediately() =>
        new ObservationSchedule()
            .Due([Forge], ScopePolicy.Hubs(TimeSpan.FromMinutes(5)), At(0))
            .ShouldHaveSingleItem().Region.ShouldBe(Forge);

    [Fact]
    public void OrderDueRegionsByWhenTheyCameDue()
    {
        var schedule = new ObservationSchedule();
        var policy = ScopePolicy.AllViable(TimeSpan.FromMinutes(5));

        schedule.Observed(Forge, At(0));
        schedule.SourceExpires(Forge, At(400));
        schedule.Observed(Quiet, At(0));
        schedule.SourceExpires(Quiet, At(200));

        // Общего такта нет: диспетчер берёт регионы в порядке наступления срока.
        schedule.Due([Forge, Quiet], policy, At(500))
            .Select(static due => due.Region)
            .ShouldBe([Quiet, Forge]);
    }
}
