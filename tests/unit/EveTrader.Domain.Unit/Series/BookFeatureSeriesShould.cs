using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Shouldly;

namespace EveTrader.Domain.Unit.Series;

/// <summary>
/// Признаки, которые по событиям не выводятся: удержание лучшей цены и глубина
/// конкуренции. Событие говорит, что переставился конкретный ордер, но не говорит, была
/// ли его цена лучшей и сколько ордеров стояло рядом. Это свойства стакана целиком.
/// </summary>
public sealed class BookFeatureSeriesShould
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeRange Day = TimeRange.Between(Start, Start.AddDays(1));

    private static readonly IReadOnlyList<int> Thresholds = FeatureOptions.DefaultThresholds;

    [Fact]
    public void AverageCompletedHoldsOfTheBestPrice()
    {
        // Продажа: 100 держится час, 99 держится три часа, затем 98 — удержание 98 ещё
        // не завершилось и в среднее не входит. (1 + 3) / 2 = 2 часа.
        IReadOnlyList<SeriesPoint> points = BookFeatureSeries.Build(
            Definition(SeriesKind.BestPriceHold),
            RegionId.From(10000002),
            Admitted(),
            [
                Snapshot(hours: 0, ask: 100m),
                Snapshot(hours: 1, ask: 99m),
                Snapshot(hours: 4, ask: 98m),
            ],
            Thresholds);

        points.Single(static point => point.Side == SeriesSide.Sell)
            .Value.ShouldBe(TimeSpan.FromHours(2).TotalSeconds);
    }

    [Fact]
    public void GiveNoPointWhereTheBestPriceNeverChanged()
    {
        IReadOnlyList<SeriesPoint> points = BookFeatureSeries.Build(
            Definition(SeriesKind.BestPriceHold),
            RegionId.From(10000002),
            Admitted(),
            [Snapshot(hours: 0, ask: 100m), Snapshot(hours: 3, ask: 100m)],
            Thresholds);

        // Удержание, которое ещё длится, наблюдённой длительности не имеет. Устойчивость
        // такой пары видна по нулевому давлению перестановок, а не отсюда.
        points.ShouldBeEmpty();
    }

    [Fact]
    public void TreatAMissingSideAsABreakAndNotAsAPrice()
    {
        // Сторона исчезла и вернулась по той же цене. Это два удержания, а не одно:
        // отсутствие стороны — не цена.
        IReadOnlyList<PriceHold> runs =
        [
            .. BookFeatureSeries.CompletedRuns(
                [
                    Snapshot(hours: 0, ask: 100m),
                    Snapshot(hours: 1, ask: null),
                    Snapshot(hours: 2, ask: 100m),
                    Snapshot(hours: 3, ask: 99m),
                ],
                SeriesSide.Sell),
        ];

        // Два удержания по часу, а не одно трёхчасовое: промежуток без стороны их
        // разделил. Слитое удержание соврало бы, что цена держалась все три часа.
        runs.Count.ShouldBe(2);
        runs.ShouldAllBe(static run => run.Seconds == TimeSpan.FromHours(1).TotalSeconds);
        runs.ShouldAllBe(static run => run.Price == IskPrice.FromIsk(100m));
    }

    [Fact]
    public void AverageTheOrdersWithinTheBandOverTheWindow()
    {
        // Полоса 1 % — первый порог. Конкурентов в ней 2, 4 и 3: в среднем 3.
        IReadOnlyList<SeriesPoint> points = BookFeatureSeries.Build(
            Depth(bandBasisPoints: 100),
            RegionId.From(10000002),
            Admitted(),
            [
                Snapshot(hours: 0, ask: 100m, sellWithin: [2, 9]),
                Snapshot(hours: 1, ask: 100m, sellWithin: [4, 9]),
                Snapshot(hours: 2, ask: 101m, sellWithin: [3, 9]),
            ],
            Thresholds);

        SeriesPoint point = points.ShouldHaveSingleItem();
        point.Side.ShouldBe(SeriesSide.Sell);
        point.Value.ShouldBe(3d);
    }

    [Fact]
    public void ReadTheThresholdThatMatchesTheBand()
    {
        // Полоса 5 % — второй порог: берётся его счётчик, а не первого.
        IReadOnlyList<SeriesPoint> points = BookFeatureSeries.Build(
            Depth(bandBasisPoints: 500),
            RegionId.From(10000002),
            Admitted(),
            [Snapshot(hours: 0, ask: 100m, sellWithin: [2, 9])],
            Thresholds);

        points.ShouldHaveSingleItem().Value.ShouldBe(9d);
    }

    [Fact]
    public void LeaveSnapshotsWithoutTheSideOutOfTheAverage()
    {
        // Во втором снимке продажи нет: это не ноль конкурентов, а отсутствие цены, от
        // которой полосу отмерять. Среднее по двум годным снимкам — (2 + 4) / 2.
        IReadOnlyList<SeriesPoint> points = BookFeatureSeries.Build(
            Depth(bandBasisPoints: 100),
            RegionId.From(10000002),
            Admitted(),
            [
                Snapshot(hours: 0, ask: 100m, sellWithin: [2, 9]),
                Snapshot(hours: 1, ask: null),
                Snapshot(hours: 2, ask: 100m, sellWithin: [4, 9]),
            ],
            Thresholds);

        points.ShouldHaveSingleItem().Value.ShouldBe(3d);
    }

    [Fact]
    public void GiveNoDepthWhereTheCountsWereNeverRecorded()
    {
        // Снимок записан до того, как признаки начали нести счётчики: сторона есть, а
        // величины нет. Выдумывать ноль нельзя.
        IReadOnlyList<SeriesPoint> points = BookFeatureSeries.Build(
            Depth(bandBasisPoints: 100),
            RegionId.From(10000002),
            Admitted(),
            [Snapshot(hours: 0, ask: 100m)],
            Thresholds);

        points.ShouldBeEmpty();
    }

    [Fact]
    public void RefuseABandThatIsNotAmongTheFeatureThresholds() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => BookFeatureSeries.Build(
            Depth(bandBasisPoints: 250),
            RegionId.From(10000002),
            Admitted(),
            [Snapshot(hours: 0, ask: 100m, sellWithin: [2, 9])],
            Thresholds));

    [Fact]
    public void RefuseAKindThatIsNotDerivedFromBookFeatures() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => BookFeatureSeries.Build(
            Definition(SeriesKind.ObservedTurnover), RegionId.From(10000002), Admitted(), [], Thresholds));

    private static SeriesDefinition Definition(SeriesKind kind) =>
        SeriesDefinition.Of(kind, TimeSpan.FromDays(1), TimeSpan.FromHours(1));

    private static SeriesDefinition Depth(int bandBasisPoints) =>
        SeriesDefinition.Of(SeriesKind.CompetitorDepth, TimeSpan.FromDays(1), TimeSpan.FromHours(1), bandBasisPoints);

    private static SeriesWindow Admitted() =>
        new(Day, SeriesAdmission.Admitted, CoverageState.Observed, 0);

    private static BookFeatures Snapshot(int hours, decimal? ask, IReadOnlyList<int>? sellWithin = null) =>
        new(
            TypeId: 34,
            LocationId: 60003760,
            BestBid: null,
            BestAsk: ask is { } price ? IskPrice.FromIsk(price) : null,
            BuyOrders: 0,
            SellOrders: ask is null ? 0 : 3,
            BuyDepth: [],
            SellDepth: [],
            BuyOrdersWithin: [],
            SellOrdersWithin: ask is null ? [] : sellWithin ?? [],
            ObservedAt: Start.AddHours(hours),
            Observation: ObservationId.From($"snap-{hours}"),
            Incomplete: false);
}
