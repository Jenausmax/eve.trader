using EveTrader.Application.Live;
using Shouldly;

namespace EveTrader.Application.Integration.Live;

/// <summary>
/// Сценарии спеки <c>market-observation/intake</c>: время на страницу, условные запросы,
/// частичное наблюдение, бюджет ошибок.
/// </summary>
public sealed class RegionPollShould
{
    private const int Forge = 10000002;

    [Fact]
    public async Task GiveEveryPageItsOwnReceiptTime()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] =
        [
            LiveFixture.Page((1, 100m, 10, false), (2, 99m, 20, true)),
            LiveFixture.Page((3, 101m, 30, false)),
            LiveFixture.Page((4, 98m, 40, true)),
        ];

        RegionPoll poll = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        poll.Outcome.ShouldBe(RegionPollOutcome.Complete);
        poll.Orders.Count.ShouldBe(4);
        poll.PagesExpected.ShouldBe(3);
        poll.Pages.Count.ShouldBe(3);

        // Времена страниц различимы, и наблюдение несёт границы интервала сбора,
        // а не один момент.
        poll.Pages.Select(static p => p.Number).ShouldBe([1, 2, 3]);
        poll.Collected.From.ShouldBeLessThanOrEqualTo(poll.Collected.To);
    }

    [Fact]
    public async Task RecordAnObservationWithNoEventsWhenTheSourceSaysNotModified()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] = [LiveFixture.Page((1, 100m, 10, false))];
        fixture.Stub.ETags[Forge] = "\"abc\"";
        _ = fixture.Stub.NotModified.Add(Forge);

        RegionPoll poll = await fixture.Poller
            .PollAsync(LiveFixture.Forge, "\"abc\"", token)
            .ConfigureAwait(true);

        poll.Outcome.ShouldBe(RegionPollOutcome.NotModified);
        poll.Orders.ShouldBeEmpty();

        // «Не изменилось» — состоявшееся и полное наблюдение: источник прямо сказал,
        // что стакан тот же.
        poll.IsComplete.ShouldBeTrue();
        fixture.Stub.BodiesServed.ShouldBe(0, "тело в таком ответе не приходит");
    }

    [Fact]
    public async Task MarkAnObservationPartialWhenPaginationBreaks()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] =
        [
            LiveFixture.Page((1, 100m, 10, false)),
            LiveFixture.Page((2, 99m, 20, true)),
            LiveFixture.Page((3, 98m, 30, false)),
            LiveFixture.Page((4, 97m, 40, true)),
        ];
        fixture.Stub.FailFromPage[Forge] = 3;

        RegionPoll poll = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        poll.Outcome.ShouldBe(RegionPollOutcome.Partial);
        poll.Pages.Count.ShouldBe(2);
        poll.PagesExpected.ShouldBe(4);
        poll.PagesFraction.ShouldBe(0.5d);

        // Частичное наблюдение полным не считается — судить по нему об исчезновении
        // ордеров нельзя.
        poll.IsComplete.ShouldBeFalse();
        poll.Orders.Count.ShouldBe(2, "полученное сохраняется, а не выбрасывается");
    }

    [Fact]
    public async Task ReportFailureWhenNotASinglePageArrives()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] = [LiveFixture.Page((1, 100m, 10, false))];
        fixture.Stub.FailFromPage[Forge] = 1;

        RegionPoll poll = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        poll.Outcome.ShouldBe(RegionPollOutcome.Failed);
        poll.Orders.ShouldBeEmpty();
        poll.FailureReason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task StopAskingWhenTheSourceSaysTheBudgetIsLow()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] = [LiveFixture.Page((1, 100m, 10, false))];
        fixture.Stub.ErrorLimitRemain = 3;
        fixture.Stub.ErrorLimitReset = 45;

        // Первый опрос проходит и приносит остаток бюджета.
        _ = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        var served = fixture.Stub.BodiesServed;

        // Второй не отправляется вовсе.
        RegionPoll paused = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        paused.Outcome.ShouldBe(RegionPollOutcome.BudgetExhausted);
        paused.FailureReason.ShouldNotBeNull().ShouldContain("бюджет ошибок");
        fixture.Stub.BodiesServed.ShouldBe(served, "запрос не ушёл");

        // Окно сброшено — темп возобновляется прежний. Сдвигаем с запасом: срок сброса
        // источник объявил относительно момента ответа, а не начала теста.
        fixture.Clock.Now = LiveFixture.Start.AddMinutes(5);

        RegionPoll resumed = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        resumed.Outcome.ShouldBe(RegionPollOutcome.Complete);
    }

    [Fact]
    public async Task TakeTheExpiryFromTheSourceHeaders()
    {
        using var fixture = new LiveFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Pages[Forge] = [LiveFixture.Page((1, 100m, 10, false))];
        fixture.Stub.ExpiresAt = LiveFixture.Start.AddSeconds(307);

        RegionPoll poll = await fixture.Poller.PollAsync(LiveFixture.Forge, null, token).ConfigureAwait(true);

        // Граница поколения кэша смещается на секунды — расписание идёт за заголовком,
        // а не за расчётной сеткой.
        poll.ExpiresAt.ShouldBe(LiveFixture.Start.AddSeconds(307));
    }
}
