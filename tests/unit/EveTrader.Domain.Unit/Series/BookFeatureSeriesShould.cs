using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Shouldly;

namespace EveTrader.Domain.Unit.Series;

/// <summary>
/// Удержание лучшей цены — признак, который по событиям не выводится: событие говорит,
/// что переставился конкретный ордер, но не говорит, была ли его цена лучшей. Лучшая
/// цена — свойство стакана целиком.
/// </summary>
public sealed class BookFeatureSeriesShould
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeRange Day = TimeRange.Between(Start, Start.AddDays(1));

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
            ]);

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
            [Snapshot(hours: 0, ask: 100m), Snapshot(hours: 3, ask: 100m)]);

        // Удержание, которое ещё длится, наблюдённой длительности не имеет. Устойчивость
        // такой пары видна по нулевому давлению перестановок, а не отсюда.
        points.ShouldBeEmpty();
    }

    [Fact]
    public void TreatAMissingSideAsABreakAndNotAsAPrice()
    {
        // Сторона исчезла и вернулась по той же цене. Это два удержания, а не одно:
        // отсутствие стороны — не цена.
        IReadOnlyList<(double Seconds, IskPrice Price)> runs =
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
    public void RefuseAKindThatIsNotDerivedFromBookFeatures() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => BookFeatureSeries.Build(
            Definition(SeriesKind.ObservedTurnover), RegionId.From(10000002), Admitted(), []));

    private static SeriesDefinition Definition(SeriesKind kind) =>
        SeriesDefinition.Of(kind, TimeSpan.FromDays(1), TimeSpan.FromHours(1));

    private static SeriesWindow Admitted() =>
        new(Day, SeriesAdmission.Admitted, CoverageState.Observed, 0);

    private static BookFeatures Snapshot(int hours, decimal? ask) =>
        new(
            TypeId: 34,
            LocationId: 60003760,
            BestBid: null,
            BestAsk: ask is { } price ? IskPrice.FromIsk(price) : null,
            BuyOrders: 0,
            SellOrders: ask is null ? 0 : 3,
            BuyDepth: [],
            SellDepth: [],
            ObservedAt: Start.AddHours(hours),
            Observation: ObservationId.From($"snap-{hours}"),
            Incomplete: false);
}
