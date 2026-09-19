using EveTrader.Domain.Book;
using Shouldly;

namespace EveTrader.Domain.Unit.Book;

/// <summary>
/// Сценарии спеки <c>market-observation/order-events</c> §«Виды событий жизни ордера»
/// и §«Время события отличается от времени наблюдения».
/// </summary>
public sealed class RegionObserverShould
{
    private static RegionObserver Observer(DiffOptions? options = null) =>
        new(Observations.TheForge, options ?? DiffOptions.Default, FeatureOptions.Default);

    /// <summary>Первое наблюдение всегда базовая линия — отдаём его и начинаем считать.</summary>
    private static RegionObserver Primed(params OrderSnapshot[] orders)
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(orders, Observations.Meta(0));

        return observer;
    }

    [Fact]
    public void MarkRepriceWhenIssuedAndPriceBothMoved()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 120, issuedMinute: 4)],
            Observations.Meta(5));

        OrderEvent moved = outcome.Events.ShouldHaveSingleItem();
        moved.Kind.ShouldBe(OrderEventKind.Repriced);
        moved.PreviousPrice.ToIsk().ShouldBe(100m);
        moved.Price.ToIsk().ShouldBe(120m);
    }

    [Fact]
    public void MarkObservedFillWhenRemainderShrankAndIssuedDidNot()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100, remain: 100));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100, remain: 70)],
            Observations.Meta(5));

        OrderEvent filled = outcome.Events.ShouldHaveSingleItem();
        filled.Kind.ShouldBe(OrderEventKind.ObservedFill);
        filled.FilledVolume.ShouldBe(30);
        filled.VolumeRemain.ShouldBe(70);
    }

    [Fact]
    public void MarkAppearanceWhenTheIdWasAbsentFromThePreviousCompleteObservation()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90, issuedMinute: 4)],
            Observations.Meta(5));

        OrderEvent appeared = outcome.Events.ShouldHaveSingleItem();
        appeared.Kind.ShouldBe(OrderEventKind.Appeared);
        appeared.OrderId.ShouldBe(2);
        appeared.Price.ToIsk().ShouldBe(90m);
        appeared.VolumeRemain.ShouldBe(100);
    }

    [Fact]
    public void EmitNothingForAnOrderThatDidNotChange()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100));

        observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(5))
            .Events.ShouldBeEmpty();
    }

    [Fact]
    public void GiveRepriceAnExactMomentAndKeepObservationTimeApart()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 120, issuedMinute: 4)],
            Observations.Meta(5));

        OrderEvent moved = outcome.Events.ShouldHaveSingleItem();

        moved.EventTime.Kind.ShouldBe(Domain.Facts.EventTimeKind.Instant);
        moved.EventTime.Instant.ShouldBe(Observations.At(4));
        moved.ObservedAt.ShouldBe(Observations.At(5).AddSeconds(20));
    }

    [Fact]
    public void GiveFillAnIntervalAndNoExactMoment()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100, remain: 100));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100, remain: 70)],
            Observations.Meta(5));

        OrderEvent filled = outcome.Events.ShouldHaveSingleItem();

        filled.EventTime.Kind.ShouldBe(Domain.Facts.EventTimeKind.Interval);
        filled.EventTime.Instant.ShouldBeNull();
        filled.EventTime.From.ShouldBe(Observations.At(0));
        filled.EventTime.To.ShouldBe(Observations.At(5).AddSeconds(20));
    }

    [Fact]
    public void TakeIssuedAsCreationMomentWhenTheStepIsFineEnough()
    {
        RegionObserver observer = Primed(Observations.Order(1, price: 100));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90, issuedMinute: 3)],
            Observations.Meta(5));

        OrderEvent appeared = outcome.Events.ShouldHaveSingleItem();

        appeared.EventTime.Kind.ShouldBe(Domain.Facts.EventTimeKind.Instant);
        appeared.EventTime.Instant.ShouldBe(Observations.At(3));
        appeared.UndersampledStep.ShouldBeFalse();
    }

    [Fact]
    public void RefuseIssuedAsCreationMomentWhenTheStepIsTooCoarse()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(0, stepMinutes: 30));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90, issuedMinute: 12)],
            Observations.Meta(30, stepMinutes: 30));

        OrderEvent appeared = outcome.Events.ShouldHaveSingleItem();

        // Момента создания мы не знаем: за полчаса ордер могли создать и переставить.
        appeared.EventTime.Kind.ShouldBe(Domain.Facts.EventTimeKind.Interval);
        appeared.UndersampledStep.ShouldBeTrue();

        // Сырое issued при этом сохранено — оно момент последней правки.
        appeared.IssuedUnix.ShouldBe(Observations.At(12).ToUnixTimeSeconds());
    }

    [Fact]
    public void MarkRepriceUndersampledWhenTheStepIsTooCoarse()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(0, stepMinutes: 30));

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 120, issuedMinute: 25)],
            Observations.Meta(30, stepMinutes: 30));

        OrderEvent moved = outcome.Events.ShouldHaveSingleItem();

        // Момент последней правки точен, но между наблюдениями их могло быть шесть.
        moved.EventTime.Instant.ShouldBe(Observations.At(25));
        moved.UndersampledStep.ShouldBeTrue();
    }
}
