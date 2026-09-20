using EveTrader.Application.Book;
using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Shouldly;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Сценарии спеки <c>market-observation/intake</c> §«Источник объявляет форму
/// наблюдения» и <c>market-facts/materialization</c> §«Частота наблюдения сохранена».
///
/// Шаг наблюдения — единственное, чем потребителю позволено различать происхождение
/// факта. Всё остальное различие между архивом и живым сбором обязано исчезнуть на
/// приёме.
/// </summary>
public sealed class ObservationStepShould
{
    private static TimeRange Window => TimeRange.Between(Snapshots.At(-60), Snapshots.At(24 * 60));

    [Fact]
    public async Task CarryTheArchiveStepIntoCoverageAndMarkFrequencySensitiveEventsIncomplete()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15), Snapshots.Of(1, 10000002, price: 100));
        fixture.Stub.Snapshots[Snapshots.At(45)] = Snapshots.Csv(
            Snapshots.At(45), Snapshots.Of(1, 10000002, price: 95, issuedMinute: 30));

        _ = await fixture.RunAsync(fixture.Archive(), Window, token).ConfigureAwait(true);

        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        coverage.ShouldAllBe(static entry => entry.ObservationStep == TimeSpan.FromMinutes(30));

        // Игра допускает правку ордера раз в пять минут, значит за тридцать их влезает
        // шесть: перестановка, полученная таким шагом, полна быть не может.
        FactRow repriced = (await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true)).Single();

        Convert.ToInt32(repriced.Values[OrderEventFacts.Kind], null).ShouldBe((int)OrderEventKind.Repriced);
        Convert.ToInt64(repriced.Values[OrderEventFacts.UndersampledStep], null)
            .ShouldBe(1L, "тридцать минут недосэмплируют перестановки вшестеро");
    }

    [Fact]
    public async Task CarryALiveStepUnchangedAndLeaveEventsComplete()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15), Snapshots.Of(1, 10000002, price: 100));
        fixture.Stub.Snapshots[Snapshots.At(45)] = Snapshots.Csv(
            Snapshots.At(45), Snapshots.Of(1, 10000002, price: 95, issuedMinute: 30));

        // Тот же приём, но источник объявляет шаг живого сбора — по границе срока
        // годности ESI это пять минут. Приём переносит объявленное, а не своё.
        _ = await fixture
            .RunAsync(fixture.Archive(step: TimeSpan.FromMinutes(5)), Window, token)
            .ConfigureAwait(true);

        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        coverage.ShouldAllBe(static entry => entry.ObservationStep == TimeSpan.FromMinutes(5));

        FactRow repriced = (await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true)).Single();

        Convert.ToInt64(repriced.Values[OrderEventFacts.UndersampledStep], null)
            .ShouldBe(0L, "шаг не реже интервала правки — перестановки полны");
    }

    [Fact]
    public async Task CountSourceGapsInCoverage()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        // Ордер пропадает из одного снимка и возвращается в следующем — это дефект
        // источника, а не исчезновение с последующим появлением.
        fixture.Stub.Snapshots[Snapshots.At(15)] = Snapshots.Csv(
            Snapshots.At(15),
            Snapshots.Of(1, 10000002, price: 100),
            Snapshots.Of(2, 10000002, price: 90));

        fixture.Stub.Snapshots[Snapshots.At(45)] = Snapshots.Csv(
            Snapshots.At(45), Snapshots.Of(1, 10000002, price: 100));

        fixture.Stub.Snapshots[Snapshots.At(75)] = Snapshots.Csv(
            Snapshots.At(75),
            Snapshots.Of(1, 10000002, price: 100),
            Snapshots.Of(2, 10000002, price: 90));

        OrderBookImportReport report = await fixture
            .RunAsync(fixture.Archive(), Window, token)
            .ConfigureAwait(true);

        report.SourceGaps.ShouldBe(1);

        IReadOnlyList<FactRow> events = await fixture.Rows
            .SelectAsync(FactSet.OrderEvents, Window, null, token)
            .ConfigureAwait(true);

        // Окно подтверждения исчезновения — два наблюдения, а пропуск длился одно:
        // исчезновения не случилось. Вернувшийся ордер при этом размечается появлением —
        // так устроена свёртка из add-order-event-derivation, и здесь это закрепляется
        // как есть: менять разметку событий этот чейндж не вправе.
        events.Select(static row => (OrderEventKind)Convert.ToInt32(row.Values[OrderEventFacts.Kind], null))
            .ShouldBe([OrderEventKind.Appeared]);

        IReadOnlyList<CoverageEntry> coverage = await fixture.Coverage
            .ReadAsync(Window, [], token)
            .ConfigureAwait(true);

        coverage.Sum(static entry => entry.SourceGaps).ShouldBe(1);
    }
}
