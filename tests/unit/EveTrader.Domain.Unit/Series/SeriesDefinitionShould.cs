using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Shouldly;

namespace EveTrader.Domain.Unit.Series;

/// <summary>
/// Определение ряда и его место в ключе факта.
///
/// Сценарии `market-signals/feature-series` §«Ряд признака несёт своё определение».
/// Смысл требования практический: величина за час и за сутки отвечает на разные
/// вопросы, и ряд, в котором они смешались, в обучении неотличим от шума.
/// </summary>
public sealed class SeriesDefinitionShould
{
    private static SeriesDefinition Turnover(int windowHours = 24) =>
        SeriesDefinition.Of(
            SeriesKind.ObservedTurnover,
            TimeSpan.FromHours(windowHours),
            TimeSpan.FromHours(1));

    [Fact]
    public void CarryItsWindowStepAndSources()
    {
        SeriesDefinition definition = Turnover();

        definition.Window.ShouldBe(TimeSpan.FromHours(24));
        definition.Step.ShouldBe(TimeSpan.FromHours(1));
        definition.Sources.ShouldBe([FactSet.OrderEvents]);
    }

    [Fact]
    public void DeriveSourcesFromTheKindAndNotFromTheCaller()
    {
        // Удержание лучшей цены считается по признакам стакана, оборот — по событиям.
        // Перечень задаёт вид: вызывающий мог бы назвать источники не те, а ряд с
        // неверным происхождением нельзя корректно пересчитать после переливки сырья.
        SeriesKinds.SourcesOf(SeriesKind.BestPriceHold).ShouldBe([FactSet.BookFeatures]);
        SeriesKinds.SourcesOf(SeriesKind.RelistPressure).ShouldBe([FactSet.OrderEvents]);
    }

    [Fact]
    public void TellTwoWindowsOfTheSameKindApart()
    {
        Turnover(1).Key.ShouldNotBe(Turnover(24).Key);

        Point(Turnover(1)).FactKey.ShouldNotBe(Point(Turnover(24)).FactKey);
    }

    [Fact]
    public void TellTwoSidesOfTheSamePairApart() =>
        Point(Turnover(), SeriesSide.Buy).FactKey
            .ShouldNotBe(Point(Turnover(), SeriesSide.Sell).FactKey);

    [Fact]
    public void RequireABandOnlyWhereTheKindUsesOne()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(static () => SeriesDefinition.Of(
            SeriesKind.CompetitorDepth, TimeSpan.FromHours(24), TimeSpan.FromHours(1)));

        _ = Should.Throw<ArgumentOutOfRangeException>(static () => SeriesDefinition.Of(
            SeriesKind.ObservedTurnover, TimeSpan.FromHours(24), TimeSpan.FromHours(1), bandBasisPoints: 100));

        SeriesDefinition.Of(
            SeriesKind.CompetitorDepth, TimeSpan.FromHours(24), TimeSpan.FromHours(1), bandBasisPoints: 100)
            .Key.ShouldEndWith("/b100");
    }

    [Fact]
    public void RefuseAStepWiderThanItsWindow() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => SeriesDefinition.Of(
            SeriesKind.ObservedTurnover, TimeSpan.FromHours(1), TimeSpan.FromHours(24)));

    private static SeriesPoint Point(SeriesDefinition definition, SeriesSide side = SeriesSide.Sell) =>
        new(
            definition,
            RegionId.From(10000002),
            TypeId: 34,
            LocationId: 60003760,
            side,
            TimeRange.Between(
                new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero)),
            Value: 1000d,
            Incomplete: false);
}
