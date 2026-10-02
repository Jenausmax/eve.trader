using EveTrader.Application.Series;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Infrastructure.Facts.Lake;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Набор рядов в озере: сценарии <c>market-signals/feature-series</c> §«Ряд признака
/// несёт своё определение», §«Ряды выводимы из фактов и читаются на момент времени» и
/// §«Ряды считаются вне хранилища» — на настоящих файлах Parquet и настоящем DuckDB.
/// </summary>
public sealed class SeriesFactsShould
{
    private static readonly DateTimeOffset End = new(2026, 9, 15, 3, 0, 0, TimeSpan.Zero);

    private static readonly SeriesDefinition Hourly =
        SeriesDefinition.Of(SeriesKind.ObservedTurnover, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30));

    private static readonly SeriesDefinition Daily =
        SeriesDefinition.Of(SeriesKind.ObservedTurnover, TimeSpan.FromDays(1), TimeSpan.FromMinutes(30));

    private static readonly TimeRange Ends = TimeRange.Between(End.AddHours(-1), End.AddHours(1));

    [Fact]
    public async Task WriteAndReadASeriesWithItsDefinitionAndBothTimes()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var lake = new Lake();

        _ = await lake.Writer.WriteAsync(
            Batch("series-a", Hourly, value: 42d), [Confirmation("series-a")], token).ConfigureAwait(true);

        SeriesComputed read = await new SeriesFactReader(lake.Rows)
            .ReadAsync(Sample.TheForge, [Hourly], Ends, null, token)
            .ConfigureAwait(true);

        SeriesPoint point = read.Points.ShouldHaveSingleItem();
        point.Definition.ShouldBe(Hourly);
        point.Definition.Sources.ShouldBe([FactSet.OrderEvents]);
        point.Window.ShouldBe(TimeRange.Between(End.AddHours(-1), End));
        point.Value.ShouldBe(42d);

        // Приговор окну едет рядом с точкой: без него отсутствие точки двусмысленно.
        SeriesWindowVerdict window = read.Windows.ShouldHaveSingleItem();
        window.Window.Admission.ShouldBe(SeriesAdmission.Admitted);

        // Момент знания — конец окна: точка посчитана из того, что было известно к нему.
        (await lake.Rows.SelectAsync(FactSet.FeatureSeries, Ends, null, token).ConfigureAwait(true))
            .ShouldAllBe(static row => row.Envelope.KnownAt == End && row.Envelope.StaticData.Value == "sde-test");
    }

    [Fact]
    public async Task KeepTwoWindowsOfOneFeatureApart()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var lake = new Lake();

        _ = await lake.Writer.WriteAsync(
            Batch("series-hourly", Hourly, value: 10d), [Confirmation("series-hourly")], token).ConfigureAwait(true);
        _ = await lake.Writer.WriteAsync(
            Batch("series-daily", Daily, value: 240d), [Confirmation("series-daily")], token).ConfigureAwait(true);

        var reader = new SeriesFactReader(lake.Rows);

        // Один признак с двумя окнами — два ряда, и при чтении они не смешиваются.
        (await reader.ReadAsync(Sample.TheForge, [Hourly], Ends, null, token).ConfigureAwait(true))
            .Points.ShouldHaveSingleItem().Value.ShouldBe(10d);
        (await reader.ReadAsync(Sample.TheForge, [Daily], Ends, null, token).ConfigureAwait(true))
            .Points.ShouldHaveSingleItem().Value.ShouldBe(240d);
    }

    [Fact]
    public async Task KeepRowsWithoutCoverageOutOfTheResult()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var lake = new Lake();

        // Прерванная запись: файл лёг, подтверждение — нет.
        FactBatch batch = Batch("series-unconfirmed", Hourly, value: 42d);
        await AtomicParquet.WriteAsync(
            lake.Layout.FileFor(batch.Set, batch.ObservedDate, batch.Region, batch.Observation),
            FactFileSchema.For(batch.Columns),
            FactFileSchema.Rows(batch),
            token).ConfigureAwait(true);

        lake.Layout.HasFiles(FactSet.FeatureSeries).ShouldBeTrue();

        (await new SeriesFactReader(lake.Rows).ReadAsync(Sample.TheForge, [Hourly], Ends, null, token).ConfigureAwait(true))
            .Points.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadTheVersionKnownAtAPastMoment()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var lake = new Lake();

        _ = await lake.Writer.WriteAsync(
            Batch("series-first", Hourly, value: 10d), [Confirmation("series-first")], token).ConfigureAwait(true);

        // Уточнённая версия той же точки стала известна тремя часами позже.
        FactBatch refined = Batch("series-refined", Hourly, value: 12d);
        refined = FactBatch.Of(
            refined.Set,
            refined.Region,
            refined.Observation,
            refined.ObservedDate,
            [.. refined.Envelopes.Select(static envelope => envelope with { KnownAt = End.AddHours(3) })],
            refined.Columns);

        _ = await lake.Writer.WriteAsync(refined, [Confirmation("series-refined")], token).ConfigureAwait(true);

        var reader = new SeriesFactReader(lake.Rows);

        (await reader.ReadAsync(Sample.TheForge, [Hourly], Ends, End.AddHours(1), token).ConfigureAwait(true))
            .Points.ShouldHaveSingleItem().Value.ShouldBe(10d);
        (await reader.ReadAsync(Sample.TheForge, [Hourly], Ends, null, token).ConfigureAwait(true))
            .Points.ShouldHaveSingleItem().Value.ShouldBe(12d);
    }

    [Fact]
    public async Task DropTheSeriesTogetherWithItsConfirmationButNotTheObservations()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var lake = new Lake();

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-history", calendarDay: 15, volume: 1, knownAt: Sample.Day(16)),
            [Sample.Covering("obs-history", observedDay: 15)],
            token).ConfigureAwait(true);
        _ = await lake.Writer.WriteAsync(
            Batch("series-a", Hourly, value: 42d), [Confirmation("series-a")], token).ConfigureAwait(true);

        _ = await lake.Maintenance.DropDerivedAsync(FactSet.FeatureSeries, Ends, token).ConfigureAwait(true);

        IReadOnlySet<ObservationId> confirmed = await lake.Coverage.ConfirmedObservationsAsync(token).ConfigureAwait(true);

        // Подтверждение производной порции ушло вместе с ней — иначе перестройка
        // наткнулась бы на «уже подтверждено». Наблюдения остались.
        confirmed.ShouldNotContain(ObservationId.From("series-a"));
        confirmed.ShouldContain(ObservationId.From("obs-history"));
    }

    private static FactBatch Batch(string observation, SeriesDefinition definition, double value)
    {
        var range = TimeRange.Between(End - definition.Window, End);
        var window = new SeriesWindow(range, SeriesAdmission.Admitted, CoverageState.Observed, 0);

        return SeriesFacts.ToBatch(
            Sample.TheForge,
            ObservationId.From(observation),
            DateOnly.FromDateTime(End.UtcDateTime),
            [new SeriesWindowVerdict(definition, Sample.TheForge, window)],
            [new SeriesPoint(definition, Sample.TheForge, 34, 60003760, SeriesSide.Sell, range, value, Incomplete: false)],
            StaticDataVersion.From("sde-test"));
    }

    private static CoverageEntry Confirmation(string observation) =>
        CoverageEntries.Derived(
            ObservationId.From(observation),
            Sample.TheForge,
            TimeRange.Between(End, End.AddMinutes(30)),
            SeriesFacts.Source,
            End);
}
