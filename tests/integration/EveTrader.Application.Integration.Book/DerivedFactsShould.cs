using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.Book;

/// <summary>
/// Схемы производных наборов на настоящих файлах Parquet: события, базовые линии,
/// чекпойнты и признаки. Схема, совпадающая только на бумаге, ничего не стоит.
/// </summary>
public sealed class DerivedFactsShould
{
    [Fact]
    public async Task RoundTripOrderEventsThroughTheLake()
    {
        using var lake = new BookLake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.ObserveAsync([Samples.Order(1, price: 100)], Samples.Meta(0), token).ConfigureAwait(true);
        _ = await lake.ObserveAsync(
            [Samples.Order(1, price: 120, issuedMinute: 4)], Samples.Meta(5), token).ConfigureAwait(true);

        IReadOnlyList<FactRow> events = await lake.ReadAsync(FactSet.OrderEvents, token).ConfigureAwait(true);

        FactRow moved = events.ShouldHaveSingleItem();

        moved.Values[OrderEventFacts.Kind].ShouldBe((long)OrderEventKind.Repriced);
        moved.Values[OrderEventFacts.OrderId].ShouldBe(1L);
        moved.Values[OrderEventFacts.PriceCents].ShouldBe(12000L);
        moved.Values[OrderEventFacts.PreviousPriceCents].ShouldBe(10000L);
        moved.Values[OrderEventFacts.IsNpc].ShouldBe(0L);

        // Точный момент перестановки пережил запись и чтение.
        moved.Envelope.EventTime.Kind.ShouldBe(EventTimeKind.Instant);
        moved.Envelope.EventTime.Instant.ShouldBe(Samples.At(4));
        moved.Envelope.KnownAt.ShouldBe(Samples.At(5).AddSeconds(20));
    }

    [Fact]
    public async Task KeepBaselinesInTheirOwnSet()
    {
        using var lake = new BookLake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.ObserveAsync(
            [Samples.Order(1, price: 100), Samples.Order(2, price: 90)], Samples.Meta(0), token)
            .ConfigureAwait(true);

        IReadOnlyList<FactRow> baselines = await lake.ReadAsync(FactSet.OrderBaselines, token).ConfigureAwait(true);

        baselines.Count.ShouldBe(2);
        baselines.ShouldAllBe(static row => (long)row.Values[OrderEventFacts.Kind]! == (long)OrderEventKind.Baseline);

        // Рыночных событий при этом нет: базовая линия — калибровка.
        lake.Layout.HasFiles(FactSet.OrderEvents).ShouldBeFalse();
    }

    [Fact]
    public async Task RoundTripFeaturesIncludingAbsentSides()
    {
        using var lake = new BookLake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.ObserveAsync(
            [Samples.Order(1, price: 100, remain: 7)], Samples.Meta(0), token).ConfigureAwait(true);

        FactRow features = (await lake.ReadAsync(FactSet.BookFeatures, token).ConfigureAwait(true))
            .ShouldHaveSingleItem();

        features.Values[BookFeatureFacts.HasAsk].ShouldBe(1L);
        features.Values[BookFeatureFacts.BestAskCents].ShouldBe(10000L);
        features.Values[BookFeatureFacts.SellOrders].ShouldBe(1L);

        // Отсутствие стороны записано признаком, а не нулевой ценой.
        features.Values[BookFeatureFacts.HasBid].ShouldBe(0L);
        features.Values[BookFeatureFacts.BuyOrders].ShouldBe(0L);

        // Пороги глубины — настройка, поэтому и колонки строятся по ним.
        features.Values[BookFeatureFacts.DepthColumn(isBuy: false, 100)].ShouldBe(7L);
        features.Values[BookFeatureFacts.DepthColumn(isBuy: false, 500)].ShouldBe(7L);
    }

    [Fact]
    public async Task ConfirmEverySetOfOneObservationWithOneCoverageEntry()
    {
        using var lake = new BookLake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.ObserveAsync([Samples.Order(1, price: 100)], Samples.Meta(0), token).ConfigureAwait(true);

        // Базовые линии, признаки и чекпойнт — три набора из одного наблюдения.
        lake.Layout.HasFiles(FactSet.OrderBaselines).ShouldBeTrue();
        lake.Layout.HasFiles(FactSet.BookFeatures).ShouldBeTrue();
        lake.Layout.HasFiles(FactSet.BookCheckpoints).ShouldBeTrue();

        IReadOnlyList<CoverageEntry> coverage = await lake.Coverage
            .ReadAsync(TimeRange.Between(Samples.At(-1), Samples.At(60)), [], token)
            .ConfigureAwait(true);

        coverage.ShouldHaveSingleItem().Observation.ShouldBe(Samples.Meta(0).Observation);

        // И все три набора видны читателю — значит подтверждены.
        (await lake.ReadAsync(FactSet.OrderBaselines, token).ConfigureAwait(true)).ShouldNotBeEmpty();
        (await lake.ReadAsync(FactSet.BookFeatures, token).ConfigureAwait(true)).ShouldNotBeEmpty();
        (await lake.ReadAsync(FactSet.BookCheckpoints, token).ConfigureAwait(true)).ShouldNotBeEmpty();
    }
}
