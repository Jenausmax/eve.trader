using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Domain.Unit.Facts;

public sealed class EventTimeShould
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CarryInstantWhenSourceReportsExactMoment()
    {
        var time = EventTime.At(Noon);

        time.Kind.ShouldBe(EventTimeKind.Instant);
        time.Instant.ShouldBe(Noon);
        time.From.ShouldBe(Noon);
        time.To.ShouldBe(Noon);
    }

    [Fact]
    public void RefuseToPassIntervalOffAsMoment()
    {
        var time = EventTime.Between(Noon, Noon.AddMinutes(5));

        time.Kind.ShouldBe(EventTimeKind.Interval);
        time.Instant.ShouldBeNull();
    }

    [Fact]
    public void AllowZeroWidthIntervalButKeepItAnInterval() =>
        EventTime.Between(Noon, Noon).Kind.ShouldBe(EventTimeKind.Interval);

    [Fact]
    public void RejectIntervalThatEndsBeforeItStarts() =>
        Should.Throw<ArgumentOutOfRangeException>(static () => EventTime.Between(Noon, Noon.AddMinutes(-1)));
}
