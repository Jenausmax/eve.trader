using EveTrader.Application.Book;
using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Прогон окна порциями по суткам с продолжением с обрыва — задачи 1.1 и 3.1 изменения
/// <c>add-orderbook-archive-import</c>.
///
/// Обрыв здесь настоящий: прогон заводится заново, без единого объекта в памяти от
/// предыдущего. Точка продолжения обязана выводиться из озера, а не из состояния
/// процесса, — иначе она расходилась бы с фактами ровно на обрыве.
/// </summary>
public sealed class WindowConversionShould
{
    private static TimeRange Window => TimeRange.Between(Snapshots.At(-60), Snapshots.At(3 * 24 * 60));

    [Fact]
    public async Task MarkEveryRegionOfEveryLoadedDayInTheRegistry()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Двое суток по два снимка, два региона в каждом.
        foreach (var minute in (int[])[15, 45, (24 * 60) + 15, (24 * 60) + 45])
        {
            fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100 + minute, issuedMinute: minute),
                Snapshots.Of(2, 10000043, price: 80 + minute, issuedMinute: minute));
        }

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        report.SnapshotsRead.ShouldBe(4);
        report.Days.ShouldBe(2);

        IReadOnlyList<MaterializedInterval> intervals = await fixture.Registry
            .ReadAsync(FactSet.OrderEvents, token)
            .ConfigureAwait(true);

        intervals.Select(static interval => interval.Region.Value).Distinct().Order()
            .ShouldBe([10000002, 10000043]);
        intervals.ShouldAllBe(static interval => interval.Source == "everef-orders");

        // Сутки — единица отметки: две порции, а не четыре снимка.
        intervals.Select(static interval => interval.Range.From).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task NotDownloadTheWholeWindowAgainOnRestart()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var minute = 15; minute <= 195; minute += 30)
        {
            fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100 + minute, issuedMinute: minute));
        }

        _ = await fixture.RunAsync(fixture.Archive(), Window, token).ConfigureAwait(true);

        fixture.Stub.BodiesServed.ShouldBe(7);

        OrderBookImportReport again = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        // Продолжение берёт ровно два последних снимка: предпоследний греет свёртку,
        // последний доказывает, что писать нечего. Остальное окно не спрашивается вовсе.
        fixture.Stub.BodiesServed.ShouldBe(9);
        again.SnapshotsRead.ShouldBe(2);
        again.ObservationsWritten.ShouldBe(0);
        again.ObservationsAlreadyPresent.ShouldBe(2);
    }

    [Fact]
    public async Task NeitherDuplicateFactsNorLoseSnapshotsWhenTheRunIsInterrupted()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var minute = 15; minute <= 195; minute += 30)
        {
            fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100 + minute, issuedMinute: minute));
        }

        // Первый прогон доходит до половины окна и обрывается.
        _ = await fixture
            .RunAsync(fixture.Archive(), TimeRange.Between(Snapshots.At(-60), Snapshots.At(100)), token)
            .ConfigureAwait(true);

        IReadOnlyList<FactRow> afterBreak = await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true);

        // Перезапуск на полном окне доводит остаток.
        OrderBookImportReport resumed = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        _ = resumed.ResumedFrom.ShouldNotBeNull();
        resumed.ResumedFrom.Value.ShouldBeLessThan(Snapshots.At(100));

        IReadOnlyList<FactRow> all = await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true);

        // Семь снимков подряд по одному ордеру с меняющейся ценой — шесть перестановок,
        // ни одной пропущенной и ни одной задвоенной.
        all.Count.ShouldBe(6);
        all.Count.ShouldBeGreaterThan(afterBreak.Count);
        all.Select(static row => row.FactKey).Distinct(StringComparer.Ordinal).Count().ShouldBe(6);

        // Ни одной базовой линии сверх первой: продолжение не объявило регион новым.
        IReadOnlyList<FactRow> baselines = await fixture.Rows
            .SelectAsync(FactSet.OrderBaselines, Window, null, token)
            .ConfigureAwait(true);

        baselines.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NeverExceedTheDeclaredParallelism()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        for (var minute = 15; minute <= 555; minute += 30)
        {
            fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100 + minute, issuedMinute: minute));
        }

        _ = await fixture.RunAsync(fixture.Archive(parallelism: 2), Window, token).ConfigureAwait(true);

        // Предел объявлен источнику и держится и на листингах, и на телах файлов.
        fixture.Stub.PeakParallelism.ShouldBeLessThanOrEqualTo(2);
        fixture.Stub.BodiesServed.ShouldBe(19);
    }
}
