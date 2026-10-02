using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Shouldly;

namespace EveTrader.Domain.Unit.Series;

/// <summary>
/// Признаки станционной торговли, выводимые из событий.
///
/// Сценарии `market-signals/feature-series` §«Признаки станционной торговли выводятся
/// из событий, а не из дневного агрегата». Различие, ради которого всё это и делается:
/// дневной агрегат не отличает продажу от отмены, а конкурента отменившего и
/// конкурента выкупленного лечат по-разному.
/// </summary>
public sealed class EventSeriesShould
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeRange Day = TimeRange.Between(Start, Start.AddDays(1));

    private static readonly RegionId Forge = RegionId.From(10000002);

    [Fact]
    public void SumFilledVolumeOfObservedFillsPerSide()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.ObservedTurnover),
            Forge,
            Admitted(),
            [
                Fill(order: 1, isBuy: false, filled: 10, hours: 1),
                Fill(order: 2, isBuy: false, filled: 5, hours: 2),
                Fill(order: 3, isBuy: true, filled: 7, hours: 3),
            ]);

        points.Count.ShouldBe(2);
        points.Single(static point => point.Side == SeriesSide.Sell).Value.ShouldBe(15d);
        points.Single(static point => point.Side == SeriesSide.Buy).Value.ShouldBe(7d);
    }

    [Fact]
    public void CountRepricesButNotNpcRestocks()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.RelistPressure),
            Forge,
            Admitted(),
            [
                Event(OrderEventKind.Repriced, order: 1, isBuy: false, hours: 1),
                Event(OrderEventKind.Repriced, order: 2, isBuy: false, hours: 2),
                // Пополнение склада NPC — не действие конкурента, и войной на 0.01 ISK
                // не является.
                Event(OrderEventKind.NpcRestock, order: 3, isBuy: false, hours: 3),
            ]);

        points.Single().Value.ShouldBe(2d);
    }

    [Fact]
    public void ReportTheShareOfDisappearancesThatFollowedAFill()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.FilledDisappearanceShare),
            Forge,
            Admitted(),
            [
                Fill(order: 1, isBuy: false, filled: 10, hours: 1),
                Event(OrderEventKind.Disappeared, order: 1, isBuy: false, hours: 2),
                Event(OrderEventKind.Disappeared, order: 2, isBuy: false, hours: 3),
                Event(OrderEventKind.Disappeared, order: 3, isBuy: true, hours: 4),
            ]);

        // Одно исчезновение из трёх имело наблюдённое исполнение. Величина
        // двусторонняя: она про слепую зону наблюдения, а не про сторону стакана.
        SeriesPoint point = points.Single();
        point.Side.ShouldBe(SeriesSide.Both);
        point.Value.ShouldBe(1d / 3d, 1e-12);
    }

    [Fact]
    public void GiveNoPointWhereNothingDisappeared()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.FilledDisappearanceShare),
            Forge,
            Admitted(),
            [Fill(order: 1, isBuy: false, filled: 10, hours: 1)]);

        // Ноль здесь означал бы «ни одно исчезновение не было продажей» — утверждение о
        // рынке, которого никто не делал.
        points.ShouldBeEmpty();
    }

    [Fact]
    public void CountAFillOutsideTheWindowNeitherInTurnoverNorInTheShare()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.ObservedTurnover),
            Forge,
            Admitted(),
            [Fill(order: 1, isBuy: false, filled: 10, hours: 30)]);

        points.ShouldBeEmpty();
    }

    [Fact]
    public void GiveNothingForARefusedWindow()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.ObservedTurnover),
            Forge,
            new SeriesWindow(Day, SeriesAdmission.Refused, CoverageState.NotObserved, 0),
            [Fill(order: 1, isBuy: false, filled: 10, hours: 1)]);

        points.ShouldBeEmpty();
    }

    [Fact]
    public void MarkPointsOfAWindowThatHeldAPartialObservation()
    {
        IReadOnlyList<SeriesPoint> points = EventSeries.Build(
            Definition(SeriesKind.ObservedTurnover),
            Forge,
            new SeriesWindow(Day, SeriesAdmission.AdmittedIncomplete, CoverageState.Observed, 1),
            [Fill(order: 1, isBuy: false, filled: 10, hours: 1)]);

        points.Single().Incomplete.ShouldBeTrue();
    }

    [Fact]
    public void RefuseAKindThatIsNotDerivedFromEvents() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => EventSeries.Build(
            SeriesDefinition.Of(SeriesKind.BestPriceHold, TimeSpan.FromDays(1), TimeSpan.FromHours(1)),
            Forge,
            Admitted(),
            []));

    private static SeriesDefinition Definition(SeriesKind kind) =>
        SeriesDefinition.Of(kind, TimeSpan.FromDays(1), TimeSpan.FromHours(1));

    private static SeriesWindow Admitted() =>
        new(Day, SeriesAdmission.Admitted, CoverageState.Observed, 0);

    private static OrderEvent Fill(long order, bool isBuy, long filled, int hours) =>
        Event(OrderEventKind.ObservedFill, order, isBuy, hours) with { FilledVolume = filled };

    private static OrderEvent Event(OrderEventKind kind, long order, bool isBuy, int hours) =>
        new(
            kind,
            order,
            TypeId: 34,
            LocationId: 60003760,
            isBuy,
            IskPrice.FromIsk(100m),
            IskPrice.FromIsk(100m),
            VolumeRemain: 100,
            FilledVolume: 0,
            EventTime.At(Start.AddHours(hours)),
            ObservedAt: Start.AddHours(hours),
            IssuedUnix: Start.ToUnixTimeSeconds(),
            DurationDays: 30,
            IsNpc: false,
            UndersampledStep: false);
}
