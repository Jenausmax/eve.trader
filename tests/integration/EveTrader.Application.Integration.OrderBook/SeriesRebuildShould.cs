using System.Globalization;
using EveTrader.Application.Series;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Infrastructure.Facts.Lake;
using Shouldly;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Сценарий <c>market-signals/feature-series</c> §«Ряды выводимы из фактов и читаются на
/// момент времени» — «Ряд перестроен из фактов», на озере, собранном из архива EVE Ref
/// той же конвертацией, что у оператора.
///
/// Ряд — производное: его можно удалить и перестроить, и перестройка обязана дать то
/// же самое. Иначе материализованный ряд был бы не кэшем выводимого, а ещё одним
/// источником правды, расходящимся с сырьём.
/// </summary>
public sealed class SeriesRebuildShould
{
    private static readonly TimeRange Within = TimeRange.Between(Snapshots.At(0), Snapshots.At(6 * 60));

    private static readonly IReadOnlyList<SeriesDefinition> Definitions =
    [
        SeriesDefinition.Of(SeriesKind.ObservedTurnover, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30)),
        SeriesDefinition.Of(SeriesKind.RelistPressure, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30)),
        SeriesDefinition.Of(SeriesKind.CompetitorDepth, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30), bandBasisPoints: 100),
        SeriesDefinition.Of(SeriesKind.BestPriceHold, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30)),
    ];

    [Fact]
    public async Task RebuildTheSameSeriesAfterItWasDropped()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        Archive(fixture);
        _ = await fixture.RunAsync(fixture.Archive(), Within, token).ConfigureAwait(true);

        SeriesMaterialization materialization = Materialization(fixture);
        var reader = new SeriesFactReader(fixture.Rows);

        SeriesMaterializationReport first = await materialization.RunAsync(Request(), token).ConfigureAwait(true);
        IReadOnlyList<string> original = Shape(
            await reader.ReadAsync(OrderBookFixture.TheForge, Definitions, Within, null, token).ConfigureAwait(true));

        first.Points.ShouldBeGreaterThan(0);
        first.Written.ShouldBeGreaterThan(0);
        original.ShouldContain(static row => row.StartsWith("point", StringComparison.Ordinal));

        var maintenance = new ParquetFactMaintenance(fixture.Layout, fixture.Coverage);
        _ = await maintenance.DropDerivedAsync(FactSet.FeatureSeries, Within, token).ConfigureAwait(true);

        // Удалено — значит удалено: читать нечего.
        (await reader.ReadAsync(OrderBookFixture.TheForge, Definitions, Within, null, token).ConfigureAwait(true))
            .Points.ShouldBeEmpty();

        SeriesMaterializationReport rebuilt = await materialization.RunAsync(Request(), token).ConfigureAwait(true);
        IReadOnlyList<string> again = Shape(
            await reader.ReadAsync(OrderBookFixture.TheForge, Definitions, Within, null, token).ConfigureAwait(true));

        // Перестройка записала заново, а не упёрлась в «уже подтверждено».
        rebuilt.Written.ShouldBe(first.Written);
        again.ShouldBe(original);
    }

    [Fact]
    public async Task NotDoubleAnIntervalMaterializedTwice()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        Archive(fixture);
        _ = await fixture.RunAsync(fixture.Archive(), Within, token).ConfigureAwait(true);

        SeriesMaterialization materialization = Materialization(fixture);

        SeriesMaterializationReport first = await materialization.RunAsync(Request(), token).ConfigureAwait(true);
        SeriesMaterializationReport second = await materialization.RunAsync(Request(), token).ConfigureAwait(true);

        // Идентификатор порции выводится из содержимого: тот же отрезок тем же набором
        // определений — та же порция, и вторая подача её не удваивает.
        second.Written.ShouldBe(0);
        second.AlreadyPresent.ShouldBe(first.Written);
    }

    [Fact]
    public async Task KeepTheConfirmationOutOfTheObservationJournal()
    {
        using var fixture = new OrderBookFixture();
        CancellationToken token = TestContext.Current.CancellationToken;

        Archive(fixture);
        _ = await fixture.RunAsync(fixture.Archive(), Within, token).ConfigureAwait(true);
        var observations = (await fixture.Coverage.ReadAsync(Within, [], token).ConfigureAwait(true)).Count;

        _ = await Materialization(fixture).RunAsync(Request(), token).ConfigureAwait(true);

        // Порции рядов подтверждены журналом покрытия, но наблюдениями региона не стали:
        // журнал наблюдений их не показывает.
        (await fixture.Coverage.ReadAsync(Within, [], token).ConfigureAwait(true)).Count.ShouldBe(observations);
        (await fixture.Coverage.ConfirmedObservationsAsync(token).ConfigureAwait(true))
            .ShouldContain(static observation => observation.Value.StartsWith("series-", StringComparison.Ordinal));
    }

    private static SeriesRequest Request() =>
        new(
            OrderBookFixture.TheForge,
            Within,
            Definitions,
            FeatureOptions.DefaultThresholds,
            StaticDataVersion.From("sde-test"),
            UpstreamCatalog.Empty);

    private static SeriesMaterialization Materialization(OrderBookFixture fixture) =>
        new(fixture.Rows, fixture.Coverage, fixture.Registry, fixture.Writer);

    /// <summary>
    /// Снимки Jita 4-4 каждые полчаса с 00:15: продажа, которую выкупают кусками, и
    /// покупка, которую переставляют.
    /// </summary>
    private static void Archive(OrderBookFixture fixture)
    {
        for (var slot = 0; slot < 11; slot++)
        {
            var minute = 15 + (slot * 30);

            fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100, remain: 1000 - (slot * 40), total: 1000),
                Snapshots.Of(2, 10000002, price: 100.5m, remain: 300, total: 300),
                Snapshots.Of(3, 10000002, price: 90 + (slot % 2), isBuy: true, issuedMinute: minute - 5),
                Snapshots.Of(4, 10000002, price: 89.5m, isBuy: true));
        }
    }

    private static IReadOnlyList<string> Shape(SeriesComputed computed) =>
    [
        .. computed.Windows.Select(static window => string.Create(
            CultureInfo.InvariantCulture,
            $"window|{window.FactKey}|{window.Window.Admission}|{window.Window.Coverage}")),
        .. computed.Points.Select(static point => string.Create(
            CultureInfo.InvariantCulture,
            $"point|{point.FactKey}|{BitConverter.DoubleToInt64Bits(point.Value)}|{point.Incomplete}")),
    ];
}
