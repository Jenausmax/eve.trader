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

    [Fact]
    public void TreatFeaturesAsDerived() => FactSets.IsRaw(FactSet.BookFeatures).ShouldBeFalse();

    [Fact]
    public void GiveEverySetItsOwnPathSegment()
    {
        var segments = Enum.GetValues<FactSet>().Select(FactSets.PathSegment).ToList();

        segments.Distinct(StringComparer.Ordinal).Count().ShouldBe(segments.Count);
        segments.ShouldAllBe(static segment => segment.Length > 0);
    }
}
