using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Domain.Unit.Facts;

public sealed class FactSetsShould
{
    [Theory]
    [InlineData(FactSet.OrderEvents)]
    [InlineData(FactSet.OrderBaselines)]
    [InlineData(FactSet.BookCheckpoints)]
    [InlineData(FactSet.HistoryDaily)]
    [InlineData(FactSet.Coverage)]
    public void TreatObservedDataAsRaw(FactSet set) => FactSets.IsRaw(set).ShouldBeTrue();

    [Theory]
    [InlineData(FactSet.BookFeatures)]
    [InlineData(FactSet.FeatureSeries)]
    [InlineData(FactSet.Signals)]
    public void TreatFeaturesSeriesAndSignalsAsDerived(FactSet set) => FactSets.IsRaw(set).ShouldBeFalse();

    [Fact]
    public void KeepBacktestReportsAsRecordsOfRunsThatHappened() =>
        // Отчёт — запись о том, что прогон состоялся и чем кончился, включая момент
        // прогона. Повторный прогон даёт другой отчёт, а не тот же: удалять его как
        // перестраиваемый нельзя.
        FactSets.IsRaw(FactSet.BacktestReports).ShouldBeTrue();

    [Fact]
    public void GiveEverySetItsOwnPathSegment()
    {
        var segments = Enum.GetValues<FactSet>().Select(FactSets.PathSegment).ToList();

        segments.Distinct(StringComparer.Ordinal).Count().ShouldBe(segments.Count);
        segments.ShouldAllBe(static segment => segment.Length > 0);
    }
}
