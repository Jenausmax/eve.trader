using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Application.Signals;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;
using Shouldly;

namespace EveTrader.Application.Integration.OrderBook;

/// <summary>
/// Сигнал — факт озера: сценарии <c>market-signals/station-trading</c> §«Сигнал — факт
/// озера», §«Набор параметров именован, и сигналы разных наборов различимы» — на озере
/// из архива EVE Ref.
/// </summary>
public sealed class SignalFactsShould
{
    private static readonly DateTimeOffset GeneratedAt = Snapshots.At(8 * 60);

    [Fact]
    public async Task GenerateSignalsFromTheLake()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        SignalRun run = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Parameters, Decisions()), token)
            .ConfigureAwait(true);

        // Первые окна тянутся до базовой линии — данных не хватило; дальше пара ходовая,
        // маржа после комиссий 6.5 % — сигнал.
        run.Verdicts.ShouldContain(static verdict => verdict.IsSignal);
        run.Verdicts.ShouldContain(static verdict => verdict.Outcome == ConsiderationOutcome.InsufficientData);
        run.Verdicts.Where(static verdict => verdict.IsSignal)
            .ShouldAllBe(static verdict => verdict.Justification!.NetMargin == IskAmount.FromIsk(5.91m));
    }

    [Fact]
    public async Task RecordASignalWithBothTimesStaticDataAndTheParameterSet()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        SignalRun run = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Parameters, Decisions()), token)
            .ConfigureAwait(true);

        SignalRecordingReport report = await RecordAsync(lake, StationTradingLake.Parameters, run, token).ConfigureAwait(true);
        IReadOnlyList<FactRow> rows = await RowsAsync(lake, token).ConfigureAwait(true);

        report.Written.ShouldBeGreaterThan(0);
        rows.Count.ShouldBe(run.Verdicts.Count);

        // Время события — момент решения; момент знания — когда система исход породила.
        rows.ShouldAllBe(static row => row.Envelope.EventTime.Kind == EventTimeKind.Instant);
        rows.ShouldAllBe(static row => row.Envelope.EventTime.From <= GeneratedAt && row.Envelope.KnownAt == GeneratedAt);
        rows.ShouldAllBe(static row => row.Envelope.StaticData.Value == "sde-test");
        rows.ShouldAllBe(static row => (string)row.Values[SignalFacts.ParameterSet]! == StationTradingLake.Parameters.Name.Value);

        // Прочитанный задним числом сигнал — ровно тот, что был порождён, с обоснованием.
        IReadOnlyList<StationTradingVerdict> read = await new SignalFactReader(lake.Fixture.Rows)
            .ReadAsync(StationTradingLake.Scope, StationTradingLake.Interval, null, token)
            .ConfigureAwait(true);

        read.Select(Shape).ShouldBe(run.Verdicts.Select(Shape));
    }

    [Fact]
    public async Task KeepThePriorSignalWhenAnotherSetRevisesIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        SignalRun first = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Parameters, Decisions()), token)
            .ConfigureAwait(true);
        _ = await RecordAsync(lake, StationTradingLake.Parameters, first, token).ConfigureAwait(true);

        IReadOnlyList<string> before = [.. (await RowsAsync(lake, token).ConfigureAwait(true)).Select(RowShape)];

        SignalRun revised = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Stricter, Decisions()), token)
            .ConfigureAwait(true);
        _ = await RecordAsync(lake, StationTradingLake.Stricter, revised, token).ConfigureAwait(true);

        IReadOnlyList<FactRow> after = await RowsAsync(lake, token).ConfigureAwait(true);

        // Прежние сигналы не изменены — ни значение, ни момент знания.
        after.Where(static row => (string)row.Values[SignalFacts.ParameterSet]! == StationTradingLake.Parameters.Name.Value)
            .Select(RowShape)
            .ShouldBe(before);

        // Новые записаны отдельными фактами под новым именем набора.
        after.Count.ShouldBe(before.Count * 2);
        revised.Verdicts.ShouldNotContain(static verdict => verdict.IsSignal);
    }

    [Fact]
    public async Task NotRewriteASignalGeneratedAgain()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using StationTradingLake lake = await StationTradingLake.BuildAsync(token).ConfigureAwait(true);

        SignalRun run = await lake.Generation
            .EvaluateAsync(StationTradingLake.Request(StationTradingLake.Parameters, Decisions()), token)
            .ConfigureAwait(true);

        _ = await RecordAsync(lake, StationTradingLake.Parameters, run, token).ConfigureAwait(true);
        IReadOnlyList<string> before = [.. (await RowsAsync(lake, token).ConfigureAwait(true)).Select(RowShape)];

        SignalRecordingReport again = await lake.Recording
            .WriteAsync(
                StationTradingLake.Parameters, StationTradingLake.Scope, run.Verdicts, StationTradingLake.Step,
                GeneratedAt.AddHours(5), StaticDataVersion.From("sde-test"), token)
            .ConfigureAwait(true);

        // Сигнал неизменяем: повторное порождение того же отрезка тем же набором его не
        // переписывает, даже моментом знания.
        again.Written.ShouldBe(0);
        (await RowsAsync(lake, token).ConfigureAwait(true)).Select(RowShape).ShouldBe(before);
    }

    private static IReadOnlyList<DateTimeOffset> Decisions() =>
        SeriesGrid.Ends(StationTradingLake.Interval, StationTradingLake.Step);

    private static Task<SignalRecordingReport> RecordAsync(
        StationTradingLake lake,
        StationTradingParameters parameters,
        SignalRun run,
        CancellationToken token) =>
        lake.Recording.WriteAsync(
            parameters, StationTradingLake.Scope, run.Verdicts, StationTradingLake.Step, GeneratedAt,
            StaticDataVersion.From("sde-test"), token);

    private static async Task<IReadOnlyList<FactRow>> RowsAsync(StationTradingLake lake, CancellationToken token) =>
        [.. await lake.Fixture.Rows.ReadAsync(FactSet.Signals, StationTradingLake.Interval, null, token).ToListAsync(token).ConfigureAwait(true)];

    private static string RowShape(FactRow row) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{row.FactKey}|{row.Envelope.KnownAt:O}|{string.Join(';', row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))}");

    private static string Shape(StationTradingVerdict verdict) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{verdict.FactKey}|{verdict.Outcome}|{string.Join(',', verdict.Reasons)}|{verdict.Coverage}|{verdict.Justification?.NetMargin}|{verdict.Justification?.NetMarginRate}|{verdict.Justification?.BuyCompetitors}|{verdict.Justification?.SellTurnover}|{verdict.Justification?.Parameters.Name}");
}
