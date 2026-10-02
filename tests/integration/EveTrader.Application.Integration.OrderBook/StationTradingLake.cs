using EveTrader.Application.Backtest;
using EveTrader.Application.Series;
using EveTrader.Application.Signals;
using EveTrader.Domain.Backtest;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Озеро станционной торговли под один тест: архив EVE Ref с ходовой парой в Jita 4-4,
/// сконвертированный тем же импортом, что у оператора, и ряды правила поверх него.
///
/// Снимки каждые полчаса с 00:15 до 06:15. Продажу по 100 выкупают по 50 штук за шаг,
/// покупку по 90 — по 30; рядом по одному конкуренту на сторону. Маржа после комиссий —
/// 5.91 ISK на единицу (6.5 % от затрат на покупку).
/// </summary>
internal sealed class StationTradingLake : IDisposable
{
    public static readonly TimeRange Interval = TimeRange.Between(Snapshots.At(0), Snapshots.At(6 * 60));

    public static readonly TimeSpan Step = TimeSpan.FromMinutes(30);

    public static readonly StationTradingScope Scope = StationTradingScope.Jita44;

    public static readonly StationTradingParameters Parameters = StationTradingParameters.Of(
        "jita",
        FeeSchedule.Of(brokerFeeRate: 0.01m, salesTaxRate: 0.02m, relistFeeRate: 0.001m),
        TimeSpan.FromHours(1),
        minNetMarginRate: 0.02m,
        minObservedTurnover: 1,
        competitorBandBasisPoints: 100,
        maxCompetitors: 5,
        maxRelistPressure: 20,
        expectedBuyRelists: 1,
        expectedSellRelists: 1);

    /// <summary>Набор строже: маржа в 6.5 % его порог не проходит.</summary>
    public static readonly StationTradingParameters Stricter = StationTradingParameters.Of(
        "jita", Parameters.Fees, Parameters.Window, 0.08m, 1, 100, 5, 20, 1, 1);

    public OrderBookFixture Fixture { get; } = new();

    public SignalGeneration Generation => new(Fixture.Rows);

    public SignalRecording Recording => new(Fixture.Writer);

    public BacktestRun Backtest => new(Generation, Fixture.Rows, Fixture.Coverage, Fixture.Registry, Fixture.Writer);

    public static Task<StationTradingLake> BuildAsync(CancellationToken cancellationToken) =>
        BuildAsync(static (_, _) => Task.CompletedTask, cancellationToken);

    /// <summary>
    /// Озеро, в которое <paramref name="beforeSeries" /> дописывает факты после импорта
    /// архива и до материализации рядов — так досинхронизация задним числом успевает
    /// попасть во входы рядов.
    /// </summary>
    public static async Task<StationTradingLake> BuildAsync(
        Func<StationTradingLake, CancellationToken, Task> beforeSeries,
        CancellationToken cancellationToken)
    {
        var lake = new StationTradingLake();

        for (var slot = 0; slot <= 12; slot++)
        {
            var minute = 15 + (slot * 30);

            lake.Fixture.Stub.Snapshots[Snapshots.At(minute)] = Snapshots.Csv(
                Snapshots.At(minute),
                Snapshots.Of(1, 10000002, price: 100, remain: 2000 - (slot * 50), total: 2000),
                Snapshots.Of(2, 10000002, price: 100.5m, remain: 500, total: 500),
                Snapshots.Of(3, 10000002, price: 90, remain: 2000 - (slot * 30), total: 2000, isBuy: true),
                Snapshots.Of(4, 10000002, price: 89.5m, remain: 500, total: 500, isBuy: true));
        }

        _ = await lake.Fixture
            .RunAsync(lake.Fixture.Archive(), TimeRange.Between(Snapshots.At(0), Snapshots.At(7 * 60)), cancellationToken)
            .ConfigureAwait(true);

        await beforeSeries(lake, cancellationToken).ConfigureAwait(true);

        foreach (StationTradingParameters parameters in (StationTradingParameters[])[Parameters, Stricter])
        {
            _ = await new SeriesMaterialization(lake.Fixture.Rows, lake.Fixture.Coverage, lake.Fixture.Registry, lake.Fixture.Writer)
                .RunAsync(
                    new SeriesRequest(
                        Scope.Region,
                        Interval,
                        StationTradingSeries.Definitions(parameters, Step),
                        FeatureOptions.DefaultThresholds,
                        StaticDataVersion.From("sde-test"),
                        UpstreamCatalog.Empty),
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return lake;
    }

    public static SignalRequest Request(StationTradingParameters parameters, IReadOnlyList<DateTimeOffset> decisions) =>
        new(parameters, Scope, Step, decisions, FeatureOptions.DefaultThresholds);

    public static BacktestSetup Setup(StationTradingParameters parameters, DateTimeOffset ranAt) =>
        new(
            parameters,
            Scope,
            Interval,
            Step,
            TimeSpan.FromHours(1),
            MaxUnknownShare: 0.5m,
            StaticDataVersion.From("sde-test"),
            ranAt);

    public void Dispose() => Fixture.Dispose();
}
