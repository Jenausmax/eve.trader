using EveTrader.Application.Book;
using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Сценарии спеки <c>market-facts/materialization</c> §«Архивные данные приводятся к
/// общей схеме»: снимок архива обязан пройти ту же свёртку, что живое наблюдение, и
/// попасть в те же наборы.
/// </summary>
public sealed class ArchiveFoldShould
{
    private static TimeRange Window => TimeRange.Between(Snapshots.At(-60), Snapshots.At(24 * 60));

    [Fact]
    public async Task TurnSnapshotsIntoEventsCheckpointsAndCoverageOfTheSameSchema()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Первый снимок — база, второй несёт перестановку цены и исполнение.
        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15),
            Snapshots.Of(1, 10000002, price: 100),
            Snapshots.Of(2, 10000002, price: 90, remain: 50));

        fixture.Stub.Snapshots[Snapshots.At(45)] = Snapshots.Csv(
            Snapshots.At(45),
            Snapshots.Of(1, 10000002, price: 95, issuedMinute: 30),
            Snapshots.Of(2, 10000002, price: 90, remain: 40));

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        report.SnapshotsRead.ShouldBe(2);
        report.Regions.ShouldBe(1);

        IReadOnlyList<FactRow> baselines = await fixture.Rows
            .SelectAsync(FactSet.OrderBaselines, Window, null, token)
            .ConfigureAwait(true);

        baselines.Count.ShouldBe(2, "первое наблюдение региона — калибровка, а не события");

        IReadOnlyList<FactRow> events = await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true);

        events.Select(static row => (OrderEventKind)Convert.ToInt32(row.Values[OrderEventFacts.Kind], null))
            .Order()
            .ShouldBe(new[] { OrderEventKind.ObservedFill, OrderEventKind.Repriced }.Order());

        // Чекпойнт состава — раз в сутки на регион, по первому полному наблюдению.
        IReadOnlyList<FactRow> checkpoints = await fixture.Rows
            .SelectAsync(FactSet.BookCheckpoints, Window, null, token)
            .ConfigureAwait(true);

        checkpoints.Count.ShouldBe(2);
        checkpoints.ShouldAllBe(static row => row.Region.Value == 10000002);

        // Признаки стакана считаются той же проходкой.
        IReadOnlyList<FactRow> features = await fixture.Rows
            .SelectAsync(FactSet.BookFeatures, Window, null, token)
            .ConfigureAwait(true);

        features.ShouldNotBeEmpty();

        // Каждое наблюдение подтверждено записью покрытия своего региона.
        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        coverage.Count.ShouldBe(2);
        coverage.ShouldAllBe(static entry => entry.Source == "everef-orders");
        coverage.ShouldAllBe(static entry => entry.Region.Value == 10000002);
        coverage.ShouldAllBe(static entry => entry.Outcome == CoverageOutcome.Success);
    }

    [Fact]
    public async Task SplitOneGlobalSnapshotIntoAnObservationPerRegion()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Снимок глобален на момент: в одном файле сразу несколько регионов.
        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15),
            Snapshots.Of(1, 10000002, price: 100),
            Snapshots.Of(2, 10000043, price: 80));

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        report.Regions.ShouldBe(2);
        report.ObservationsWritten.ShouldBe(2);

        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        // Записи покрытия лежат по имени наблюдения вперемешку, поэтому регион входит
        // в имя: иначе подтверждённым оказался бы ровно один регион из снимка.
        coverage.Select(static entry => entry.Region.Value).Order().ShouldBe([10000002, 10000043]);
        coverage.Select(static entry => entry.Observation.Value).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task FoldSnapshotsInOrderEvenWhenALaterOneArrivesFirst()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var minute = 15; minute <= 105; minute += 30)
        {
            fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100 + minute, issuedMinute: minute - 5));
        }

        // Первый снимок едет вдвое дольше следующего: без выдачи по порядку свёртка
        // увидела бы их наоборот и выдала бы выдуманные события.
        fixture.Stub.Delays[Snapshots.At(15)] = TimeSpan.FromMilliseconds(120);

        _ = await fixture.RunAsync(fixture.Archive(), Window, token).ConfigureAwait(true);

        IReadOnlyList<FactRow> events = await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true);

        // Три перестановки подряд: 15 -> 45 -> 75 -> 105, каждая от предыдущей цены.
        events.Count.ShouldBe(3);

        List<long> previous = [.. events
            .OrderBy(static row => row.Envelope.KnownAt)
            .Select(static row => Convert.ToInt64(row.Values[OrderEventFacts.PreviousPriceCents], null))];

        previous.ShouldBe([(100 + 15) * 100L, (100 + 45) * 100L, (100 + 75) * 100L]);
    }

    [Fact]
    public async Task TakeTheCollectionIntervalFromTheRowsNotFromTheFileName()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Источник обходит рынок не мгновенно: строки региона помечены своим временем.
        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(12), Snapshots.Of(1, 10000002, price: 100));

        _ = await fixture.RunAsync(fixture.Archive(), Window, token).ConfigureAwait(true);

        CoverageEntry entry = (await fixture.Coverage.ReadAsync(Window, [], token).ConfigureAwait(true)).Single();

        entry.Collected.From.ShouldBe(Snapshots.At(12));
    }

    [Fact]
    public async Task SkipASnapshotWhoseFileIsBrokenInsteadOfAbortingTheRun()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15), Snapshots.Of(1, 10000002, price: 100));

        // Заголовок без обязательной колонки — дефект данных, а не обрыв: повтором не лечится.
        fixture.Stub.Snapshots[Snapshots.At(45)] = "duration,is_buy_order,region_id\n90,false,10000002\n";

        fixture.Stub.Snapshots[Snapshots.At(75)] = Snapshots.Csv(
            Snapshots.At(75), Snapshots.Of(1, 10000002, price: 120, issuedMinute: 60));

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        report.SnapshotsRead.ShouldBe(2, "испорченный снимок пропускается, прогон идёт дальше");

        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        // Пропущенный снимок остался без покрытия, то есть восполним следующим прогоном.
        coverage.ShouldNotContain(static entry => entry.Collected.From == Snapshots.At(45));
    }

    [Fact]
    public async Task RecoverFromATruncatedDownloadInsteadOfLosingTheSnapshot()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15), Snapshots.Of(1, 10000002, price: 100));
        fixture.Stub.Snapshots[Snapshots.At(45)] = Snapshots.Csv(
            Snapshots.At(45), Snapshots.Of(1, 10000002, price: 110, issuedMinute: 30));

        fixture.Stub.TruncateAttempts[Snapshots.At(45)] = 2;

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        fixture.Stub.TruncatedServed.ShouldBe(2);
        report.SnapshotsRead.ShouldBe(2, "оборванный снимок забирается повтором, а не теряется");
    }

    [Fact]
    public async Task ReadOnlyTheRegionsThatWereAsked()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15),
            Snapshots.Of(1, 10000002, price: 100),
            Snapshots.Of(2, 10000043, price: 80));

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token, [OrderBookFixture.TheForge])
            .ConfigureAwait(true);

        report.Regions.ShouldBe(1);

        CoverageEntry entry = (await fixture.Coverage.ReadAsync(Window, [], token).ConfigureAwait(true)).Single();

        entry.Region.ShouldBe(OrderBookFixture.TheForge);
    }
}
