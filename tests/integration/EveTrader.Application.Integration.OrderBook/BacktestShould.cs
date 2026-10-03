using System.Globalization;
using EveTrader.Application.Backtest;
using EveTrader.Application.Facts;
using EveTrader.Application.Series;
using EveTrader.Application.Signals;
using EveTrader.Domain.Backtest;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Бэктест: сценарии <c>market-signals/backtest</c> — на озере из архива EVE Ref.
///
/// Главное требование — чтение строго на момент решения. Прогон, нарушающий его, даёт
/// результат лучше боевого и не сообщает об этом; поэтому здесь в озеро подкладываются
/// факты, узнанные позже момента решения, и проверяется, что решение их не видело.
/// </summary>
public sealed class BacktestShould
{
    private static readonly DateTimeOffset RanAt = Snapshots.At(10 * 60);

    /// <summary>Момент решения, на котором сигнал есть и исход определим.</summary>
    private static readonly DateTimeOffset Decision = Snapshots.At(3 * 60);

    [Fact]
    public async Task GoThroughTheSameRuleLiveAndOnHistory()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        BacktestResult history = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        // «Вживую» в момент решения — один момент, тот же путь.
        SignalRun live = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Parameters, [Decision]), token)
            .ConfigureAwait(true);

        history.Run.Verdicts.Where(static verdict => verdict.Decision == Decision).Select(Shape)
            .ShouldBe(live.Verdicts.Select(Shape));

        // И устройство это закрепляет: прогон на истории получает порождение сигналов, а
        // не держит своё.
        typeof(BacktestRun).GetConstructors().Single().GetParameters()
            .ShouldContain(static parameter => parameter.ParameterType == typeof(SignalGeneration));
    }

    [Fact]
    public async Task IgnoreARawFactLearnedAfterTheDecision()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        // Досинхронизация задним числом: снимок стакана за 02:50 — раньше момента решения —
        // стал известен системе только в 05:00. Спред в нём съеден комиссиями: будь он
        // виден в 03:00, сигнала бы не было.
        await BackdatedQuoteAsync(lake, token).ConfigureAwait(true);

        SignalRun atDecision = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Parameters, [Decision]), token)
            .ConfigureAwait(true);

        atDecision.Verdicts.ShouldHaveSingleItem().IsSignal.ShouldBeTrue();
        atDecision.Verdicts.Single().Justification!.BestBid.ShouldBe(IskPrice.FromIsk(90m));
    }

    [Fact]
    public async Task KeepALateRawFactOutOfTheSeriesBehindTheDecision()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        // Досинхронизация задним числом попала в озеро до материализации рядов: снимок за
        // 02:50 с толпой конкурентов у продажи стал известен только в 05:00. Войди он в
        // ряд глубины конкуренции на 03:00, среднее ушло бы за порог, и сигнала бы не было.
        using StationTradingLake lake = await StationTradingLake.BuildAsync(CrowdedQuoteAsync, token).ConfigureAwait(true);

        BacktestResult history = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        StationTradingVerdict decided = history.Run.Verdicts.Single(static verdict => verdict.Decision == Decision);

        decided.IsSignal.ShouldBeTrue();
        decided.Justification.ShouldNotBeNull().SellCompetitors.ShouldBe(2d);
    }

    [Fact]
    public async Task UseTheVersionOfAFactKnownAtTheDecision()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        // Уточнённая версия ряда глубины конкуренции на 03:00 — пятьдесят конкурентов —
        // стала известна только в 06:00.
        await RefinedDepthAsync(lake, token).ConfigureAwait(true);

        BacktestResult history = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        StationTradingVerdict decided = history.Run.Verdicts.Single(static verdict => verdict.Decision == Decision);

        // В расчёт вошла версия, известная на момент решения.
        decided.IsSignal.ShouldBeTrue();
        decided.Justification!.SellCompetitors.ShouldBe(2d);

        // А последняя известная версия сигнал бы отменила — значит, факт не пустой, и
        // бэктест действительно его не видел.
        SeriesComputed latest = await new SeriesFactReader(lake.Fixture.Rows)
            .ReadAsync(
                StationTradingLake.Scope.Region,
                StationTradingSeries.Definitions(StationTradingLake.Parameters, StationTradingLake.Step),
                TimeRange.Between(Decision.AddMinutes(-1), Decision.AddMinutes(1)),
                null,
                token)
            .ConfigureAwait(true);

        StationTradingVerdict cheating = StationTradingRule.Evaluate(
            StationTradingLake.Parameters,
            new StationTradingInput(
                StationTradingLake.Scope,
                34,
                Decision,
                StationTradingLake.Step,
                Quote(decided),
                new SeriesComputed(latest.Windows, [.. latest.Points.Where(static point => point.TypeId == 34)])));

        cheating.Reasons.ShouldContain(VerdictReason.TooManyCompetitors);
    }

    [Fact]
    public async Task UseTheVersionOfARawFeatureKnownAtTheDecision()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        // Снимки за 02:15 и 02:45 уточнены до материализации рядов: те же факты, пятьдесят
        // конкурентов у продажи, но узнано это только в 05:00. На 03:00 известна исходная
        // версия — её ряд и должен взять, а не остаться без снимков вовсе.
        using StationTradingLake lake = await StationTradingLake.BuildAsync(RefinedRawQuotesAsync, token).ConfigureAwait(true);

        BacktestResult history = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        StationTradingVerdict decided = history.Run.Verdicts.Single(static verdict => verdict.Decision == Decision);

        decided.IsSignal.ShouldBeTrue();
        decided.Justification.ShouldNotBeNull().SellCompetitors.ShouldBe(2d);
    }

    [Fact]
    public async Task NameTheHorizonAndLeaveUnobservedOutcomesUnknown()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        BacktestResult result = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        // Горизонт назван: час от момента решения.
        result.Assessed.ShouldAllBe(static item => item.Horizon.Duration == TimeSpan.FromHours(1));
        result.Report.Horizon.ShouldBe(TimeSpan.FromHours(1));

        // Пока снимки идут, исход определён — круг замыкается: исполнения по обе стороны.
        result.Assessed.Single(static item => item.Signal.Decision == Decision).Outcome.ShouldBe(SignalOutcome.Success);

        // Последний снимок — 06:15. Горизонт решения 05:30 тянется до 06:30, и его
        // хвост не наблюдался: исход неизвестен и в метрики не входит.
        AssessedSignal late = result.Assessed.Single(static item => item.Signal.Decision == Snapshots.At((5 * 60) + 30));
        late.Outcome.ShouldBe(SignalOutcome.Unknown);
        late.Realized.ShouldBeNull();
    }

    [Fact]
    public async Task ReportQualityAgainstTheDoNotTradeBaseline()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        BacktestReport report = (await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true)).Report;

        report.Signals.ShouldBeGreaterThan(0);
        report.Determined.ShouldBeGreaterThan(0);
        report.Unknown.ShouldBeGreaterThan(0);
        report.Signals.ShouldBe(report.Determined + report.Unknown);
        report.BaselineTotalCents.ShouldBe(0L);
        report.HitRate.ShouldBe(1m);
        report.TotalRealizedCents.ShouldBe(591L * report.Successes);
        report.BeatsBaseline.ShouldBeTrue();
        report.UnknownShare.ShouldBeLessThanOrEqualTo(report.MaxUnknownShare);
    }

    [Fact]
    public async Task RepeatARunBitForBitIncludingOrder()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        BacktestResult first = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);
        BacktestResult second = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        second.Report.ShouldBe(first.Report);
        second.Run.Verdicts.Select(Shape).ShouldBe(first.Run.Verdicts.Select(Shape));
        second.Assessed.Select(static item => string.Create(
                CultureInfo.InvariantCulture, $"{item.Signal.FactKey}|{item.Outcome}|{item.Realized}"))
            .ShouldBe(first.Assessed.Select(static item => string.Create(
                CultureInfo.InvariantCulture, $"{item.Signal.FactKey}|{item.Outcome}|{item.Realized}")));
    }

    [Fact]
    public async Task CompareParameterSetsByRecordedReports()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        _ = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Parameters, RanAt), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);
        _ = await lake.Backtest
            .RunAsync(StationTradingLake.Setup(StationTradingLake.Stricter, RanAt.AddMinutes(5)), FeatureOptions.DefaultThresholds, token)
            .ConfigureAwait(true);

        // Сравнение строится по записанным отчётам — без повторного прогона.
        IReadOnlyList<BacktestReport> reports = await new BacktestReportReader(lake.Fixture.Rows)
            .ReadAsync(StationTradingLake.Interval, token)
            .ConfigureAwait(true);

        reports.Count.ShouldBe(2);
        reports.ShouldAllBe(static report => report.StaticData.Value == "sde-test");
        reports.Select(static report => report.RanAt).ShouldBe([RanAt, RanAt.AddMinutes(5)]);

        var comparison = new BacktestComparison(reports[0], reports[1]);

        comparison.Comparable.ShouldBeTrue();
        comparison.Leader.ShouldNotBeNull().ParameterSet.ShouldBe(StationTradingLake.Parameters.Name);

        // Строгий набор сигналов не дал — его прогон недоказателен, и сравнение это знает.
        reports[1].Signals.ShouldBe(0);
        comparison.Conclusive.ShouldBeFalse();
    }

    /// <summary>Снимок за 02:50 с безубыточным спредом, ставший известным в 05:00.</summary>
    private static Task BackdatedQuoteAsync(StationTradingLake lake, CancellationToken token) =>
        LateQuoteAsync(lake, ObservationId.From("backdated-0250"), IskPrice.FromIsk(99m), sellCompetitors: 1, token);

    /// <summary>Снимок за 02:50 с полусотней конкурентов у продажи, ставший известным в 05:00.</summary>
    private static Task CrowdedQuoteAsync(StationTradingLake lake, CancellationToken token) =>
        LateQuoteAsync(lake, ObservationId.From("crowded-0250"), IskPrice.FromIsk(90m), sellCompetitors: 50, token);

    private static async Task LateQuoteAsync(
        StationTradingLake lake,
        ObservationId observation,
        IskPrice bestBid,
        int sellCompetitors,
        CancellationToken token)
    {
        DateTimeOffset at = Snapshots.At((2 * 60) + 50);
        DateTimeOffset learned = Snapshots.At(5 * 60);

        FactBatch batch = BookFeatureFacts.ToBatch(
            StationTradingLake.Scope.Region,
            observation,
            DateOnly.FromDateTime(at.UtcDateTime),
            [
                new BookFeatures(
                    34, StationTradingLake.Scope.StationId, bestBid, IskPrice.FromIsk(100m), 2, 2,
                    [10, 10], [10, 10], [1, 1], [sellCompetitors, sellCompetitors], at, observation, Incomplete: false),
            ],
            FeatureOptions.DefaultThresholds,
            StaticDataVersion.From("sde-test"));

        batch = FactBatch.Of(
            batch.Set,
            batch.Region,
            batch.Observation,
            batch.ObservedDate,
            [.. batch.Envelopes.Select(envelope => envelope with { KnownAt = learned })],
            batch.Columns);

        _ = await lake.Fixture.Writer.WriteAsync(
            batch,
            [
                CoverageEntries.Success(
                    observation, StationTradingLake.Scope.Region, TimeRange.Between(at.AddSeconds(-20), at),
                    pages: 1, orderCount: 4, source: "late", observationStep: TimeSpan.FromMinutes(30), knownAt: learned),
            ],
            token).ConfigureAwait(true);
    }

    /// <summary>Уточнённые версии снимков за 02:15 и 02:45 с толпой у продажи, известные с 05:00.</summary>
    private static async Task RefinedRawQuotesAsync(StationTradingLake lake, CancellationToken token)
    {
        DateTimeOffset learned = Snapshots.At(5 * 60);

        IReadOnlyList<FactRow> recorded = await lake.Fixture.Rows
            .SelectAsync(FactSet.BookFeatures, TimeRange.Between(Snapshots.At(2 * 60), Decision), null, token)
            .ConfigureAwait(true);

        // Снимок архива датирован секундой позже своей четверти часа.
        foreach (DateTimeOffset at in (DateTimeOffset[])[Snapshots.At((2 * 60) + 15).AddSeconds(1), Snapshots.At((2 * 60) + 45).AddSeconds(1)])
        {
            var observation = ObservationId.From(string.Create(CultureInfo.InvariantCulture, $"refined-{at:HHmm}"));

            FactBatch batch = BookFeatureFacts.ToBatch(
                StationTradingLake.Scope.Region,
                observation,
                DateOnly.FromDateTime(at.UtcDateTime),
                [
                    new BookFeatures(
                        34, StationTradingLake.Scope.StationId, IskPrice.FromIsk(90m), IskPrice.FromIsk(100m), 2, 2,
                        [10, 10], [10, 10], [1, 1], [50, 50], at, observation, Incomplete: false),
                ],
                FeatureOptions.DefaultThresholds,
                StaticDataVersion.From("sde-test"));

            // Уточнение, а не новый снимок: ключ совпадает с записанным при импорте.
            recorded.Select(static row => row.FactKey).ShouldContain(batch.Envelopes.Single().FactKey);

            batch = FactBatch.Of(
                batch.Set,
                batch.Region,
                batch.Observation,
                batch.ObservedDate,
                [.. batch.Envelopes.Select(envelope => envelope with { KnownAt = learned })],
                batch.Columns);

            _ = await lake.Fixture.Writer.WriteAsync(
                batch,
                [
                    CoverageEntries.Success(
                        observation, StationTradingLake.Scope.Region, TimeRange.Between(at.AddSeconds(-20), at),
                        pages: 1, orderCount: 4, source: "refined", observationStep: TimeSpan.FromMinutes(30), knownAt: learned),
                ],
                token).ConfigureAwait(true);
        }
    }

    /// <summary>Уточнённая версия точки глубины конкуренции на 03:00, известная с 06:00.</summary>
    private static async Task RefinedDepthAsync(StationTradingLake lake, CancellationToken token)
    {
        SeriesDefinition depth = StationTradingSeries.CompetitorDepth(
            StationTradingLake.Parameters.Window, StationTradingLake.Step, StationTradingLake.Parameters.CompetitorBandBasisPoints);
        var range = TimeRange.Between(Decision - depth.Window, Decision);
        var observation = ObservationId.From("refined-depth");

        FactBatch batch = SeriesFacts.ToBatch(
            StationTradingLake.Scope.Region,
            observation,
            DateOnly.FromDateTime(Decision.UtcDateTime),
            [],
            [new SeriesPoint(depth, StationTradingLake.Scope.Region, 34, StationTradingLake.Scope.StationId, SeriesSide.Sell, range, 50d, Incomplete: false)],
            StaticDataVersion.From("sde-test"));

        batch = FactBatch.Of(
            batch.Set,
            batch.Region,
            batch.Observation,
            batch.ObservedDate,
            [.. batch.Envelopes.Select(static envelope => envelope with { KnownAt = Snapshots.At(6 * 60) })],
            batch.Columns);

        _ = await lake.Fixture.Writer.WriteAsync(
            batch,
            [CoverageEntries.Derived(observation, StationTradingLake.Scope.Region, TimeRange.Between(Decision, Decision.AddMinutes(30)), "refined", Snapshots.At(6 * 60))],
            token).ConfigureAwait(true);
    }

    private static BookFeatures Quote(StationTradingVerdict decided) =>
        new(
            34,
            StationTradingLake.Scope.StationId,
            decided.Justification!.BestBid,
            decided.Justification.BestAsk,
            2,
            2,
            [10, 10],
            [10, 10],
            [2, 2],
            [2, 2],
            decided.Justification.QuoteObservedAt,
            ObservationId.From("quote"),
            Incomplete: false);

    private static string Shape(StationTradingVerdict verdict) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{verdict.FactKey}|{verdict.Outcome}|{string.Join(',', verdict.Reasons)}|{verdict.Coverage}|{verdict.Justification?.NetMargin}|{verdict.Justification?.BuyCompetitors}|{verdict.Justification?.SellCompetitors}|{verdict.Justification?.BuyTurnover}|{verdict.Justification?.SellTurnover}");
}
