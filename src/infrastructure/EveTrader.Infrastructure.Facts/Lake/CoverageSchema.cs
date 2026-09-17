using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Parquet.Schema;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>Схема записи покрытия: одна попытка наблюдения региона.</summary>
public static class CoverageSchema
{
    public const string Observation = "observation_id";
    public const string Region = "region";
    public const string CollectedFrom = "collected_from";
    public const string CollectedTo = "collected_to";
    public const string Outcome = "outcome";
    public const string PagesReceived = "pages_received";
    public const string PagesExpected = "pages_expected";
    public const string OrderCount = "order_count";
    public const string Source = "source";
    public const string ObservationStepSeconds = "observation_step_seconds";
    public const string SourceGaps = "source_gaps";
    public const string FailureReason = "failure_reason";
    public const string KnownAt = "known_at";

    public static ParquetSchema Schema { get; } = new(
        new DataField<string>(Observation),
        new DataField<int>(Region),
        new DataField<DateTime>(CollectedFrom),
        new DataField<DateTime>(CollectedTo),
        new DataField<int>(Outcome),
        new DataField<int>(PagesReceived),
        new DataField<int>(PagesExpected),
        new DataField<long>(OrderCount),
        new DataField<string>(Source),
        new DataField<long>(ObservationStepSeconds),
        new DataField<int>(SourceGaps),
        new DataField<string?>(FailureReason),
        new DataField<DateTime>(KnownAt));

    public static IDictionary<string, object?> ToRow(CoverageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Observation] = entry.Observation.Value,
            [Region] = entry.Region.Value,
            [CollectedFrom] = entry.Collected.From.UtcDateTime,
            [CollectedTo] = entry.Collected.To.UtcDateTime,
            [Outcome] = (int)entry.Outcome,
            [PagesReceived] = entry.PagesReceived,
            [PagesExpected] = entry.PagesExpected,
            [OrderCount] = (long)entry.OrderCount,
            [Source] = entry.Source,
            [ObservationStepSeconds] = (long)entry.ObservationStep.TotalSeconds,
            [SourceGaps] = entry.SourceGaps,
            [FailureReason] = entry.FailureReason,
            [KnownAt] = entry.KnownAt.UtcDateTime,
        };
    }

    public static CoverageEntry FromRow(IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new CoverageEntry(
            ObservationId.From((string)row[Observation]!),
            RegionId.From(Convert.ToInt32(row[Region], System.Globalization.CultureInfo.InvariantCulture)),
            TimeRange.Between(Utc(row[CollectedFrom]), Utc(row[CollectedTo])),
            (CoverageOutcome)Convert.ToInt32(row[Outcome], System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(row[PagesReceived], System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(row[PagesExpected], System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(row[OrderCount], System.Globalization.CultureInfo.InvariantCulture),
            (string)row[Source]!,
            TimeSpan.FromSeconds(Convert.ToInt64(row[ObservationStepSeconds], System.Globalization.CultureInfo.InvariantCulture)),
            Convert.ToInt32(row[SourceGaps], System.Globalization.CultureInfo.InvariantCulture),
            Optional(row, FailureReason) as string,
            Utc(row[KnownAt]));
    }

    /// <summary>
    /// Untyped-разбор Parquet не кладёт в строку ключ колонки, у которой все значения
    /// пусты. Для необязательных колонок это нормальный случай, а не дефект файла.
    /// </summary>
    public static object? Optional(IReadOnlyDictionary<string, object?> row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.TryGetValue(column, out var value) ? value : null;
    }

    /// <summary>
    /// Parquet возвращает время как <see cref="DateTime" /> либо как
    /// <see cref="DateTimeOffset" /> в зависимости от того, как записан логический тип.
    /// Приводим к UTC явно: часовой пояс на строке факта — источник расхождений, которые
    /// потом ищут неделями.
    /// </summary>
    public static DateTimeOffset Utc(object? value) => value switch
    {
        DateTimeOffset offset => offset.ToUniversalTime(),
        DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException($"Ожидалось время, получено '{value?.GetType().Name ?? "null"}'"),
    };
}
