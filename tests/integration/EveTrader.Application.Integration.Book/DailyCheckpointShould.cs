using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.Book;

/// <summary>
/// Сценарии спеки <c>market-facts/bitemporal-store</c> §«Суточные чекпойнты состава
/// стакана».
/// </summary>
public sealed class DailyCheckpointShould
{
    private const int Day = 24 * 60;

    /// <summary>Наблюдения трёх суток: по три в день.</summary>
    private static async Task<BookLake> ThreeDaysAsync(CancellationToken token)
    {
        var lake = new BookLake();

        var price = 100m;

        foreach (var minute in new[] { 0, 60, 120, Day, Day + 60, Day + 120, 2 * Day, (2 * Day) + 60 })
        {
            price += 1m;

            _ = await lake.ObserveAsync(
                [Samples.Order(1, price, issuedMinute: minute), Samples.Order(2, price: 50)],
                Samples.Meta(minute),
                token).ConfigureAwait(true);
        }

        return lake;
    }

    [Fact]
    public async Task WriteOneCheckpointPerDayPerRegion()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using BookLake lake = await ThreeDaysAsync(token).ConfigureAwait(true);

        IReadOnlyList<FactRow> checkpoints = await lake.ReadAsync(FactSet.BookCheckpoints, token).ConfigureAwait(true);

        // Восемь наблюдений, но трое суток — значит три чекпойнта, по два ордера в каждом.
        checkpoints.Select(static row => row.Envelope.KnownAt.UtcDateTime.Date).Distinct().Count().ShouldBe(3);
        checkpoints.Count.ShouldBe(6);
    }

    [Fact]
    public async Task LeaveNoMoreThanADayOfEventsToFold()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using BookLake lake = await ThreeDaysAsync(token).ConfigureAwait(true);

        IReadOnlyList<FactRow> checkpoints = await lake.ReadAsync(FactSet.BookCheckpoints, token).ConfigureAwait(true);
        IReadOnlyList<FactRow> events = await lake.ReadAsync(FactSet.OrderEvents, token).ConfigureAwait(true);

        DateTimeOffset latest = checkpoints.Max(row => row.Envelope.KnownAt);

        // Ровно то, ради чего чекпойнты и заведены: восстановление любого момента
        // сворачивает не больше суток событий, а не всю историю региона.
        List<FactRow> toFold = [.. events.Where(row => row.Envelope.KnownAt > latest)];

        toFold.ShouldAllBe(row => row.Envelope.KnownAt - latest < TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task CheckpointTheWholeBookNotJustWhatChanged()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using BookLake lake = await ThreeDaysAsync(token).ConfigureAwait(true);

        IReadOnlyList<FactRow> checkpoints = await lake.ReadAsync(FactSet.BookCheckpoints, token).ConfigureAwait(true);

        DateTimeOffset latest = checkpoints.Max(row => row.Envelope.KnownAt);
        List<FactRow> last = [.. checkpoints.Where(row => row.Envelope.KnownAt == latest)];

        // Второй ордер за трое суток не менялся ни разу — и всё равно в чекпойнте есть.
        last.Select(row => (long)row.Values[BookCheckpointFacts.OrderId]!).Order().ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task NeverCheckpointAPartialObservation()
    {
        using var lake = new BookLake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.ObserveAsync([Samples.Order(1, price: 100)], Samples.Meta(0), token).ConfigureAwait(true);

        // Следующие сутки, но наблюдение неполное: состав стакана неизвестен.
        _ = await lake.ObserveAsync(
            [Samples.Order(1, price: 100)], Samples.Meta(Day, complete: false), token).ConfigureAwait(true);

        IReadOnlyList<FactRow> checkpoints = await lake.ReadAsync(FactSet.BookCheckpoints, token).ConfigureAwait(true);

        checkpoints.Select(static row => row.Envelope.KnownAt.UtcDateTime.Date).Distinct().Count()
            .ShouldBe(1, "по частичному наблюдению состав записывать нельзя");
    }
}
