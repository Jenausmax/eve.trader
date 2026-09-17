using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Facts.Integration;

/// <summary>Образцы наблюдений: дневная история как самый простой набор с двумя временами.</summary>
internal static class Sample
{
    public static RegionId TheForge { get; } = RegionId.From(10000002);

    public static DateTimeOffset Day(int day) => new(2026, 1, day, 0, 0, 0, TimeSpan.Zero);

    public static DateOnly DayOnly(int day) => DateOnly.FromDateTime(Day(day).UtcDateTime);

    /// <summary>Дневная строка истории: ключ один, версия задаётся временем получения.</summary>
    public static FactBatch History(
        string observation,
        int calendarDay,
        long volume,
        DateTimeOffset knownAt,
        RegionId? region = null,
        int? observedDay = null)
    {
        var id = ObservationId.From(observation);
        RegionId at = region ?? TheForge;

        var envelope = new FactEnvelope(
            $"history/{at.Value}/34/2026-01-{calendarDay:00}",
            EventTime.At(Day(calendarDay)),
            knownAt,
            id,
            StaticDataVersion.From("sde-2026.01"));

        return FactBatch.Of(
            FactSet.HistoryDaily,
            at,
            id,
            DayOnly(observedDay ?? calendarDay),
            [envelope],
            [FactColumn.OfInt64("volume", [volume])]);
    }

    public static CoverageEntry Covering(
        string observation,
        int observedDay,
        RegionId? region = null,
        int sourceGaps = 0) =>
        CoverageEntries.Success(
            ObservationId.From(observation),
            region ?? TheForge,
            TimeRange.Between(Day(observedDay), Day(observedDay).AddHours(1)),
            pages: 1,
            orderCount: 1,
            source: "archive",
            observationStep: TimeSpan.FromMinutes(5),
            knownAt: Day(observedDay).AddHours(1),
            sourceGaps: sourceGaps);
}
