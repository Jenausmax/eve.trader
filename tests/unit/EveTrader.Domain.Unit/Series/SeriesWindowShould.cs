using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Shouldly;

namespace EveTrader.Domain.Unit.Series;

/// <summary>
/// Разбор покрытия окна признака.
///
/// Сценарии `market-signals/feature-series` §«Признак по неполному окну не
/// порождается». Разница между отказом и пометкой не косметическая: частичное
/// наблюдение говорит «часть страниц не пришла» — это ограничение точности; пробел
/// говорит «мы не смотрели» — и посчитанная по нему величина не неточная, а другая.
/// </summary>
public sealed class SeriesWindowShould
{
    private static readonly TimeRange Window = TimeRange.Between(
        new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AdmitAWindowCoveredByCompleteObservations()
    {
        var window = SeriesWindow.Of(Window, Verdict(CoverageState.Observed, partial: 0));

        window.Admission.ShouldBe(SeriesAdmission.Admitted);
        window.IsAdmitted.ShouldBeTrue();
        window.IsIncomplete.ShouldBeFalse();
    }

    [Fact]
    public void RefuseAWindowWithAnUnobservedInterval()
    {
        var window = SeriesWindow.Of(Window, Verdict(CoverageState.NotObserved, partial: 0));

        window.Admission.ShouldBe(SeriesAdmission.Refused);

        // Причина доступна состоянием покрытия, а не текстом: по ней решают, лечится ли
        // это загрузкой.
        window.Coverage.ShouldBe(CoverageState.NotObserved);
    }

    [Fact]
    public void RefuseAWindowWhoseGapIsMerelyReplenishable()
    {
        // Восполнимость говорит, что данные можно догрузить, а не что они уже есть.
        var window = SeriesWindow.Of(Window, Verdict(CoverageState.NotMaterialized, partial: 0));

        window.Admission.ShouldBe(SeriesAdmission.Refused);
        window.Coverage.ShouldBe(CoverageState.NotMaterialized);
    }

    [Fact]
    public void MarkAWindowThatHoldsAPartialObservation()
    {
        var window = SeriesWindow.Of(Window, Verdict(CoverageState.Observed, partial: 2));

        window.Admission.ShouldBe(SeriesAdmission.AdmittedIncomplete);
        window.IsAdmitted.ShouldBeTrue();
        window.IsIncomplete.ShouldBeTrue();
        window.PartialObservations.ShouldBe(2);
    }

    [Fact]
    public void KeepIncompletePointsOutOfTraining()
    {
        SeriesPoint[] points = [Point(incomplete: false), Point(incomplete: true), Point(incomplete: false)];

        SeriesPoints.ForTraining(points).Count().ShouldBe(2);
        SeriesPoints.ForTraining(points).ShouldAllBe(static point => !point.Incomplete);
    }

    private static CoverageVerdict Verdict(CoverageState state, int partial) =>
        new(
            RegionId.From(10000002),
            Window,
            state,
            CoveredFraction: state == CoverageState.Observed ? 1d : 0.5d,
            Gaps: [],
            SourceGaps: 0,
            PartialObservations: partial,
            UnchangedObservations: 0);

    private static SeriesPoint Point(bool incomplete) =>
        new(
            SeriesDefinition.Of(SeriesKind.ObservedTurnover, TimeSpan.FromHours(24), TimeSpan.FromHours(1)),
            RegionId.From(10000002),
            TypeId: 34,
            LocationId: 60003760,
            SeriesSide.Sell,
            Window,
            Value: 1000d,
            incomplete);
}
