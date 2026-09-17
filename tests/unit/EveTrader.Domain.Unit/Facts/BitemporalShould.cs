using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Domain.Unit.Facts;

public sealed class BitemporalShould
{
    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Дневная строка за 1 января, полученная дважды с разным объёмом.</summary>
    private static FactEnvelope Version(string key, int knownAtDay, string observation) =>
        new(key,
            EventTime.At(Day1),
            Day1.AddDays(knownAtDay),
            ObservationId.From(observation),
            StaticDataVersion.None);

    [Fact]
    public void KeepBothVersionsOfTheSameFact()
    {
        FactEnvelope[] rows = [Version("history/10000002/34/2026-01-01", 0, "a"), Version("history/10000002/34/2026-01-01", 1, "b")];

        rows.Length.ShouldBe(2);
        Bitemporal.Latest(rows).Count.ShouldBe(1);
    }

    [Fact]
    public void HideVersionsThatDidNotExistYet()
    {
        FactEnvelope early = Version("k", 0, "a");
        FactEnvelope refined = Version("k", 5, "b");

        IReadOnlyList<FactEnvelope> seen = Bitemporal.AsOf([early, refined], Day1.AddDays(3));

        seen.Count.ShouldBe(1);
        seen[0].ShouldBe(early);
    }

    [Fact]
    public void ReturnLatestVersionWhenNoInstantGiven()
    {
        FactEnvelope early = Version("k", 0, "a");
        FactEnvelope refined = Version("k", 5, "b");

        Bitemporal.Latest([early, refined]).ShouldBe([refined]);
    }

    [Fact]
    public void ReturnNothingWhenEverythingIsStillInTheFuture() =>
        Bitemporal.AsOf([Version("k", 5, "a")], Day1.AddDays(1)).ShouldBeEmpty();

    [Fact]
    public void PickOnePerKey()
    {
        FactEnvelope[] rows =
        [
            Version("b", 1, "x"), Version("a", 2, "y"), Version("a", 1, "z"), Version("b", 3, "w"),
        ];

        IReadOnlyList<FactEnvelope> latest = Bitemporal.Latest(rows);

        latest.Select(static row => row.FactKey).ShouldBe(["a", "b"]);
        latest.Select(static row => row.KnownAt).ShouldBe([Day1.AddDays(2), Day1.AddDays(3)]);
    }

    [Fact]
    public void BreakTiesDeterministically()
    {
        FactEnvelope first = Version("k", 1, "aaa");
        FactEnvelope second = Version("k", 1, "bbb");

        Bitemporal.Latest([first, second]).ShouldBe(Bitemporal.Latest([second, first]));
        Bitemporal.Latest([first, second]).ShouldBe([second]);
    }
}
