using EveTrader.Domain.Book;
using Shouldly;

namespace EveTrader.Domain.Unit.Book;

/// <summary>Сценарии спеки <c>market-observation/book-features</c>.</summary>
public sealed class BookFeaturesShould
{
    private const long Jita = 60003760;

    private static RegionObserver Observer(FeatureOptions? features = null) =>
        new(Observations.TheForge, DiffOptions.Default, features ?? FeatureOptions.Default);

    private static OrderSnapshot Buy(long id, decimal price, long volume) =>
        Observations.Order(id, price, remain: volume, total: volume, isBuy: true);

    private static OrderSnapshot Sell(long id, decimal price, long volume) =>
        Observations.Order(id, price, remain: volume, total: volume);

    [Fact]
    public void ComputeBothSidesSpreadAndDepthForATwoSidedBook()
    {
        ObservationOutcome outcome = Observer().Observe(
            [Buy(1, 90, 10), Buy(2, 89, 20), Sell(3, 100, 5), Sell(4, 104, 50)],
            Observations.Meta(0));

        BookFeatures features = outcome.Features.ShouldHaveSingleItem();

        features.BestBid!.Value.ToIsk().ShouldBe(90m);
        features.BestAsk!.Value.ToIsk().ShouldBe(100m);
        features.SpreadCents.ShouldBe(1000);
        features.BuyOrders.ShouldBe(2);
        features.SellOrders.ShouldBe(2);
        features.IsTwoSided.ShouldBeTrue();

        // Порог 1 % от лучшей покупки 90 — это 89.1, второй ордер по 89 не попадает.
        features.BuyDepth[0].ShouldBe(10);

        // Порог 5 % — это 85.5, попадают оба.
        features.BuyDepth[1].ShouldBe(30);

        // Продажа: 1 % от 100 — это 101, второй ордер по 104 не попадает; 5 % — 105, попадает.
        features.SellDepth[0].ShouldBe(5);
        features.SellDepth[1].ShouldBe(55);
    }

    [Fact]
    public void RecordAMissingSideAsAbsenceNotAsZero()
    {
        ObservationOutcome outcome = Observer().Observe(
            [Sell(3, 100, 5)],
            Observations.Meta(0));

        BookFeatures features = outcome.Features.ShouldHaveSingleItem();

        features.BestAsk!.Value.ToIsk().ShouldBe(100m);
        features.SellOrders.ShouldBe(1);

        // Ноль — это цена. Отсутствие стороны обязано быть отсутствием.
        features.BestBid.ShouldBeNull();
        features.SpreadCents.ShouldBeNull();
        features.BuyOrders.ShouldBe(0);
        features.BuyDepth.ShouldBeEmpty();
        features.IsTwoSided.ShouldBeFalse();
    }

    [Fact]
    public void MaterializeFeaturesOnlyForPairsInScope()
    {
        FeatureOptions hubsOnly = FeatureOptions.Default with
        {
            IncludePair = static (_, locationId) => locationId == Jita,
        };

        ObservationOutcome outcome = Observer(hubsOnly).Observe(
            [
                Observations.Order(1, price: 100, locationId: Jita),
                Observations.Order(2, price: 200, locationId: 60008494),
            ],
            Observations.Meta(0));

        outcome.Features.ShouldHaveSingleItem().LocationId.ShouldBe(Jita);

        // Наблюдение при этом полное: сужение охвата признаков данных не теряет.
        outcome.Events.Count.ShouldBe(2);
    }

    [Fact]
    public void NotLetFeatureScopeAffectEvents()
    {
        RegionObserver narrow = Observer(FeatureOptions.None);
        _ = narrow.Observe([Observations.Order(1, price: 100)], Observations.Meta(0));

        ObservationOutcome outcome = narrow.Observe(
            [Observations.Order(1, price: 120, issuedMinute: 4)],
            Observations.Meta(5));

        outcome.Features.ShouldBeEmpty();
        outcome.Events.ShouldHaveSingleItem().Kind.ShouldBe(OrderEventKind.Repriced);
    }

    [Fact]
    public void CarryTheObservationThatFeaturesCameFrom()
    {
        ObservationMeta meta = Observations.Meta(0);

        BookFeatures features = Observer()
            .Observe([Observations.Order(1, price: 100)], meta)
            .Features.ShouldHaveSingleItem();

        features.Observation.ShouldBe(meta.Observation);
        features.ObservedAt.ShouldBe(meta.Collected.To);
    }

    [Fact]
    public void FlagFeaturesComputedFromAPartialObservation()
    {
        RegionObserver observer = Observer();
        _ = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(0));

        ObservationOutcome partial = observer.Observe(
            [Observations.Order(1, price: 100)],
            Observations.Meta(5, complete: false));

        partial.Features.ShouldHaveSingleItem().Incomplete.ShouldBeTrue();
    }

    [Fact]
    public void KeepIncompleteFeaturesOutOfATrainingSelection()
    {
        RegionObserver observer = Observer();
        ObservationOutcome full = observer.Observe([Observations.Order(1, price: 100)], Observations.Meta(0));
        ObservationOutcome partial = observer.Observe(
            [Observations.Order(1, price: 100)], Observations.Meta(5, complete: false));

        List<BookFeatures> trainable = [.. full.Features.Concat(partial.Features).Where(static f => !f.Incomplete)];

        trainable.ShouldHaveSingleItem().Incomplete.ShouldBeFalse();
    }

    [Fact]
    public void SeparateFeaturesByTypeAndLocation()
    {
        ObservationOutcome outcome = Observer().Observe(
            [
                Observations.Order(1, price: 100, typeId: 34, locationId: Jita),
                Observations.Order(2, price: 200, typeId: 35, locationId: Jita),
                Observations.Order(3, price: 300, typeId: 34, locationId: 60008494),
            ],
            Observations.Meta(0));

        outcome.Features.Count.ShouldBe(3);
        outcome.Features.Select(static f => (f.TypeId, f.LocationId)).Distinct().Count().ShouldBe(3);
    }
}
