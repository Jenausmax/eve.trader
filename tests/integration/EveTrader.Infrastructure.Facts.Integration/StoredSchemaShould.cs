using EveTrader.Domain.Facts;
using EveTrader.Infrastructure.Facts.Lake;
using Shouldly;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>
/// Схемы покрытия и реестра материализации. Проверяются по записанному файлу: схема,
/// объявленная в коде, и схема на диске — разные вещи, пока их не сверили.
/// </summary>
public sealed class StoredSchemaShould
{
    [Fact]
    public async Task MatchTheDeclaredCoverageSchemaOnDisk()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteCoverageOnlyAsync(Sample.Covering("obs-schema", observedDay: 1), token).ConfigureAwait(true);

        var file = lake.Layout.CoverageFileFor(Sample.DayOnly(1), ObservationId.From("obs-schema"));
        IReadOnlyDictionary<string, object?> stored = (await ParquetCoverageLog.ReadFileAsync(file, token).ConfigureAwait(true)).Single();

        // failure_reason в выдаче отсутствует: у успешного наблюдения причины отказа нет.
        stored.Keys.Order(StringComparer.Ordinal).ShouldBe(
        [
            CoverageSchema.CollectedFrom,
            CoverageSchema.CollectedTo,
            CoverageSchema.KnownAt,
            CoverageSchema.Observation,
            CoverageSchema.ObservationStepSeconds,
            CoverageSchema.OrderCount,
            CoverageSchema.Outcome,
            CoverageSchema.PagesExpected,
            CoverageSchema.PagesReceived,
            CoverageSchema.Region,
            CoverageSchema.Source,
            CoverageSchema.SourceGaps,
        ]);
    }

    [Fact]
    public async Task MatchTheDeclaredRegistrySchemaOnDisk()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        await lake.Registry.RecordAsync(
            FactSet.OrderEvents,
            TimeRange.Between(Sample.Day(1), Sample.Day(2)),
            [Sample.TheForge],
            "archive",
            Sample.Day(2),
            token).ConfigureAwait(true);

        IReadOnlyDictionary<string, object?> stored = (await ParquetCoverageLog.ReadFileAsync(lake.Layout.RegistryFile(FactSet.OrderEvents), token).ConfigureAwait(true)).Single();

        stored.Keys.Order(StringComparer.Ordinal).ShouldBe(
        [
            ParquetMaterializationRegistry.LoadedAt,
            ParquetMaterializationRegistry.RangeFrom,
            ParquetMaterializationRegistry.RangeTo,
            ParquetMaterializationRegistry.Region,
            ParquetMaterializationRegistry.Source,
        ]);
    }

    [Fact]
    public async Task CarryEnvelopeColumnsInEveryFactFile()
    {
        using var lake = new Lake();
        CancellationToken token = TestContext.Current.CancellationToken;

        _ = await lake.Writer.WriteAsync(
            Sample.History("obs-envelope", calendarDay: 1, volume: 1, knownAt: Sample.Day(2)),
            Sample.Covering("obs-envelope", observedDay: 1),
            token).ConfigureAwait(true);

        var file = lake.Layout.FileFor(
            FactSet.HistoryDaily, Sample.DayOnly(1), Sample.TheForge, ObservationId.From("obs-envelope"));
        IReadOnlyDictionary<string, object?> stored = (await ParquetCoverageLog.ReadFileAsync(file, token).ConfigureAwait(true)).Single();

        foreach (var column in FactColumnNames.Envelope)
        {
            stored.Keys.ShouldContain(column);
        }

        // Регион и дата наблюдения в файл не пишутся — они в пути партиции.
        stored.Keys.ShouldNotContain(FactColumnNames.Region);
        stored.Keys.ShouldNotContain(FactColumnNames.ObservedDate);
    }
}
