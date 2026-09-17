using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Domain.Unit.Facts;

public sealed class TimeRangesShould
{
    private static readonly DateTimeOffset Midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour) => Midnight.AddHours(hour);

    private static TimeRange Range(int from, int to) => TimeRange.Between(At(from), At(to));

    [Fact]
    public void MergeOverlappingAndAdjacentRanges()
    {
        IReadOnlyList<TimeRange> merged = TimeRanges.Merge([Range(0, 2), Range(1, 3), Range(3, 4), Range(6, 7)]);

        merged.Count.ShouldBe(2);
        merged[0].ShouldBe(Range(0, 4));
        merged[1].ShouldBe(Range(6, 7));
    }

    [Fact]
    public void SubtractCoveredPartsLeavingHoles()
    {
        IReadOnlyList<TimeRange> remainder = TimeRanges.Subtract(Range(0, 10), [Range(2, 4), Range(6, 7)]);

        remainder.ShouldBe([Range(0, 2), Range(4, 6), Range(7, 10)]);
    }

    [Fact]
    public void ReturnWholeRangeWhenNothingSubtracted() =>
        TimeRanges.Subtract(Range(0, 5), []).ShouldBe([Range(0, 5)]);

    [Fact]
    public void ReturnNothingWhenFullyCovered() =>
        TimeRanges.Subtract(Range(1, 4), [Range(0, 9)]).ShouldBeEmpty();

    [Fact]
    public void IntersectClipsToRequestedRange() =>
        TimeRanges.Intersect(Range(2, 6), [Range(0, 3), Range(5, 9)])
            .ShouldBe([Range(2, 3), Range(5, 6)]);

    [Fact]
    public void SumDurationWithoutDoubleCountingOverlap() =>
        TimeRanges.TotalDuration([Range(0, 3), Range(2, 4)]).ShouldBe(TimeSpan.FromHours(4));

    [Fact]
    public void RejectRangeThatEndsBeforeItStarts() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => TimeRange.Between(At(5), At(3)));

    [Fact]
    public void TreatRangeAsHalfOpen()
    {
        Range(1, 3).Contains(At(1)).ShouldBeTrue();
        Range(1, 3).Contains(At(3)).ShouldBeFalse();
        Range(1, 2).Overlaps(Range(2, 3)).ShouldBeFalse();
    }
}
