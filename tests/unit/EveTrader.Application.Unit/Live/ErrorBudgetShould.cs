using EveTrader.Application.Live;
using Shouldly;

namespace EveTrader.Application.Unit.Live;

/// <summary>Сценарии спеки <c>market-observation/intake</c> §«Бюджет ошибок источника соблюдается».</summary>
public sealed class ErrorBudgetShould
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void StopSendingWhenTheBudgetRunsLowAndSayWhy()
    {
        var clock = new Clock(Start);
        var budget = new ErrorBudget(clock, pauseBelow: 20);

        budget.Reported(remainingErrors: 5, windowResetsAt: Start.AddSeconds(45));

        BudgetVerdict verdict = budget.Check();

        verdict.IsAllowed.ShouldBeFalse();
        verdict.ResumeAt.ShouldBe(Start.AddSeconds(45));

        // Пауза обязана быть видна в данных: иначе пробел выглядит рыночным фактом.
        verdict.Reason.ShouldContain("бюджет ошибок");
    }

    [Fact]
    public void KeepSendingWhileTheBudgetIsComfortable()
    {
        var budget = new ErrorBudget(new Clock(Start), pauseBelow: 20);

        budget.Reported(remainingErrors: 80, windowResetsAt: Start.AddSeconds(45));

        budget.Check().IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void ResumeAtTheSamePaceOnceTheWindowResets()
    {
        var clock = new Clock(Start);
        var budget = new ErrorBudget(clock, pauseBelow: 20);

        budget.Reported(remainingErrors: 1, windowResetsAt: Start.AddSeconds(45));
        budget.Check().IsAllowed.ShouldBeFalse();

        clock.Now = Start.AddSeconds(45);

        budget.Check().IsAllowed.ShouldBeTrue();
        budget.Remaining.ShouldBe(int.MaxValue, "окно сброшено — бюджет снова полон");
    }

    [Fact]
    public void AllowEverythingBeforeTheSourceHasSaidAnything() =>
        new ErrorBudget(new Clock(Start)).Check().IsAllowed.ShouldBeTrue();

    [Fact]
    public void ShareOneBudgetAcrossEveryRegion()
    {
        var clock = new Clock(Start);
        var budget = new ErrorBudget(clock, pauseBelow: 20);

        // Источник считает ошибки по клиенту, а не по нашим регионам: ответ, пришедший
        // по одному региону, закрывает запросы по всем.
        budget.Reported(remainingErrors: 3, windowResetsAt: Start.AddSeconds(30));

        budget.Check().IsAllowed.ShouldBeFalse();
        budget.Check().IsAllowed.ShouldBeFalse();
        budget.Remaining.ShouldBe(3);
    }
}
