using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.History;

/// <summary>Отметка загруженного в реестре материализации.</summary>
internal static class MaterializationRecord
{
    /// <summary>
    /// Отмечает интервал, покрывающий загруженные сутки. Границы берутся по крайним
    /// суткам, а не по каждым в отдельности: реестр отвечает на вопрос «есть ли данные
    /// локально», и запись на сутки сделала бы его на порядки больше без нового ответа.
    /// </summary>
    public static Task OfDaysAsync(
        IMaterializationRegistry registry,
        IReadOnlyList<DateOnly> days,
        IReadOnlyCollection<RegionId> regions,
        string source,
        DateTimeOffset loadedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(days);

        DateOnly earliest = days.Min();
        DateOnly latest = days.Max();

        var from = new DateTimeOffset(earliest.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = new DateTimeOffset(latest.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);

        return registry.RecordAsync(
            FactSet.HistoryDaily, TimeRange.Between(from, to), regions, source, loadedAt, cancellationToken);
    }
}
