using EveTrader.Domain.Book;
using Shouldly;

namespace EveTrader.Domain.Unit.Book;

/// <summary>
/// Сценарии спеки <c>market-observation/order-events</c>: классификация NPC, окно
/// подтверждения исчезновения, базовая линия, частичное наблюдение и запрет домысливать
/// причину.
/// </summary>
public sealed class OrderLifecycleShould
{
    private static RegionObserver Observer(DiffOptions? options = null) =>
        new(Observations.TheForge, options ?? DiffOptions.Default, FeatureOptions.Default);

    [Fact]
    public void CallNpcRestockWhatWouldOtherwiseLookLikeARepricing()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.NpcOrder(42, price: 1000)], Observations.Meta(0));

        // У NPC при пополнении склада issued двигается, а цена остаётся прежней.
        ObservationOutcome outcome = observer.Observe(
            [Observations.NpcOrder(42, price: 1000, issuedMinute: 4)],
            Observations.Meta(5));

        OrderEvent restock = outcome.Events.ShouldHaveSingleItem();
        restock.Kind.ShouldBe(OrderEventKind.NpcRestock);
        restock.IsNpc.ShouldBeTrue();
    }

    [Fact]
    public void KeepRestocksOutOfTheCompetitorSelection()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(
            [Observations.NpcOrder(42, price: 1000), Observations.Order(7, price: 95)],
            Observations.Meta(0));

        ObservationOutcome outcome = observer.Observe(
            [
                Observations.NpcOrder(42, price: 1000, issuedMinute: 4),
                Observations.Order(7, price: 93, issuedMinute: 4),
            ],
            Observations.Meta(5));

        List<OrderEvent> reprices = [.. outcome.Events.Where(static e => e.Kind == OrderEventKind.Repriced)];

        reprices.ShouldHaveSingleItem().OrderId.ShouldBe(7);
        reprices.ShouldAllBe(static e => !e.IsNpc);
    }

    [Fact]
    public void StillCallItARepriceWhenAnNpcOrderActuallyChangesPrice()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.NpcOrder(42, price: 1000)], Observations.Meta(0));

        ObservationOutcome outcome = observer.Observe(
            [Observations.NpcOrder(42, price: 1200, issuedMinute: 4)],
            Observations.Meta(5));

        outcome.Events.ShouldHaveSingleItem().Kind.ShouldBe(OrderEventKind.Repriced);
    }

    [Fact]
    public void CallItARepriceWhenANonNpcOrderIsReissuedAtTheSamePrice()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.Order(7, price: 95)], Observations.Meta(0));

        // Владелец переставил ордер на ту же цену: issued двинулся, цена та же,
        // отпечатка NPC нет. Таблица видов событий этот случай не называет — а в игре
        // он возможен, и выбрасывать действие владельца нельзя.
        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(7, price: 95, issuedMinute: 4)],
            Observations.Meta(5));

        OrderEvent moved = outcome.Events.ShouldHaveSingleItem();

        moved.Kind.ShouldBe(OrderEventKind.Repriced);
        moved.IsNpc.ShouldBeFalse();
        moved.Price.ShouldBe(moved.PreviousPrice);
    }

    [Fact]
    public void ConfirmDisappearanceOnlyAfterTheWindowCloses()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        // Первое отсутствие — ещё не факт, а состояние процесса.
        ObservationOutcome first = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(5));

        first.Events.ShouldBeEmpty();
        first.PendingDisappearances.ShouldBe(1);

        ObservationOutcome second = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(10));

        OrderEvent gone = second.Events.ShouldHaveSingleItem();
        gone.Kind.ShouldBe(OrderEventKind.Disappeared);
        gone.OrderId.ShouldBe(2);

        // Время события — между последним наблюдением ордера и первым отсутствием.
        gone.EventTime.Kind.ShouldBe(Domain.Facts.EventTimeKind.Interval);
        gone.EventTime.From.ShouldBe(Observations.At(0).AddSeconds(20));
        gone.EventTime.To.ShouldBe(Observations.At(5));

        // Время наблюдения — момент подтверждения, а не первого отсутствия.
        gone.ObservedAt.ShouldBe(Observations.At(10).AddSeconds(20));
    }

    [Fact]
    public void CountASourceGapWhenTheOrderComesBackBeforeConfirmation()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        _ = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(5));

        ObservationOutcome back = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(10));

        back.Events.ShouldNotContain(static e => e.Kind == OrderEventKind.Disappeared);
        back.SourceGaps.ShouldBe(1);
        back.PendingDisappearances.ShouldBe(0);
    }

    [Fact]
    public void RespectALongerConfirmationWindow()
    {
        RegionObserver observer = Observer(DiffOptions.Default with { DisappearanceWindow = 3 });
        _ = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        for (var minute = 5; minute <= 10; minute += 5)
        {
            observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(minute))
                .Events.ShouldBeEmpty();
        }

        observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(15))
            .Events.ShouldHaveSingleItem().Kind.ShouldBe(OrderEventKind.Disappeared);
    }

    [Fact]
    public void RecordEverythingAsBaselineWhenTheRegionComesBackIntoScope()
    {
        RegionObserver observer = Observer();

        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        outcome.Events.Count.ShouldBe(2);
        outcome.Events.ShouldAllBe(static e => e.Kind == OrderEventKind.Baseline);
        outcome.Events.ShouldNotContain(static e => e.Kind == OrderEventKind.Appeared);
    }

    [Fact]
    public void TreatAnExplicitBaselineAsCalibrationNotAsAppearance()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(0));

        // Регион выключали и включили снова — наблюдение объявлено базовой линией.
        ObservationOutcome outcome = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(9, price: 80)],
            Observations.Meta(10080, baseline: true));

        outcome.Events.Count.ShouldBe(2);
        outcome.Events.ShouldAllBe(static e => e.Kind == OrderEventKind.Baseline);
    }

    [Fact]
    public void NeverInferDisappearanceFromAPartialObservation()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        // Получена часть страниц: отсутствие ордера не значит ничего.
        ObservationOutcome partial = observer.Observe(
            [Observations.Order(1, price: 100)],
            Observations.Meta(5, complete: false));

        partial.Events.ShouldNotContain(static e => e.Kind == OrderEventKind.Disappeared);
        partial.PendingDisappearances.ShouldBe(0, "счётчик окна не двигается");
    }

    [Fact]
    public void StillMarkWhatAPartialObservationDidSee()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        ObservationOutcome partial = observer.Observe(
            [Observations.Order(1, price: 111, issuedMinute: 4)],
            Observations.Meta(5, complete: false));

        partial.Events.ShouldHaveSingleItem().Kind.ShouldBe(OrderEventKind.Repriced);
    }

    [Fact]
    public void NotLetAPartialObservationBecomeTheBaseline()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(0));

        _ = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(5, complete: false));

        // Второй ордер никуда не девался — частичное наблюдение его просто не спрашивало.
        ObservationOutcome full = observer.Observe(
            [Observations.Order(1, price: 100), Observations.Order(2, price: 90)],
            Observations.Meta(10));

        full.Events.ShouldBeEmpty();
        full.SourceGaps.ShouldBe(0);
    }

    [Fact]
    public void LeaveTheCauseOfDisappearanceUnstatedButExpiryComputable()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.Order(1, price: 100, issuedMinute: 0, duration: 90)], Observations.Meta(0));

        _ = observer.Observe([], Observations.Meta(5));
        ObservationOutcome outcome = observer.Observe([], Observations.Meta(10));

        OrderEvent gone = outcome.Events.ShouldHaveSingleItem();

        // Причины в событии нет — её и не может быть: выкуп, отмена и истечение
        // источником не различаются. Истечение при этом вычислимо.
        gone.Kind.ShouldBe(OrderEventKind.Disappeared);
        gone.ExpiresAt.ShouldBe(Observations.At(0).AddDays(90));
        gone.ExpiresAt.ShouldBeGreaterThan(gone.EventTime.To, "этот ордер исчез задолго до истечения");
    }
}
