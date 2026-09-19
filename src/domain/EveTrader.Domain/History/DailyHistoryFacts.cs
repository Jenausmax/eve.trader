using EveTrader.Domain.Facts;

namespace EveTrader.Domain.History;

/// <summary>
/// Перевод строк дневной истории в порцию фактов. Чистая функция: раскладка колонок —
/// счётная работа, и живёт она в домене, а не в разборщике формата.
/// </summary>
public static class DailyHistoryFacts
{
    public const string Region = "region";
    public const string TypeId = "type_id";
    public const string Average = "average";
    public const string Highest = "highest";
    public const string Lowest = "lowest";
    public const string OrderCount = "order_count";
    public const string Volume = "volume";

    /// <summary>
    /// Собирает порцию за одни календарные сутки. Регион идёт колонкой: источник
    /// публикует сутки глобальным файлом на все регионы, и набор по региону не
    /// партиционируется.
    /// </summary>
    public static FactBatch ToBatch(
        ObservationId observation,
        DateOnly marketDate,
        IReadOnlyList<MarketHistoryRow> rows,
        StaticDataVersion staticData)
    {
        ArgumentNullException.ThrowIfNull(rows);

        MarketHistoryRow? foreign = rows.FirstOrDefault(row => row.MarketDate != marketDate);
        if (foreign is not null)
        {
            throw new ArgumentException(
                $"Строка за {foreign.MarketDate:yyyy-MM-dd} попала в порцию за {marketDate:yyyy-MM-dd}",
                nameof(rows));
        }

        // Сутки — интервал, а не момент: агрегат описывает весь день целиком.
        var from = new DateTimeOffset(marketDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var eventTime = EventTime.Between(from, from.AddDays(1));

        var envelopes = new List<FactEnvelope>(rows.Count);

        foreach (MarketHistoryRow row in rows)
        {
            envelopes.Add(new FactEnvelope(row.FactKey, eventTime, row.KnownAt, observation, staticData));
        }

        return FactBatch.Of(
            FactSet.HistoryDaily,
            null,
            observation,
            marketDate,
            envelopes,
            [
                FactColumn.OfInt64(Region, [.. rows.Select(row => (long)row.Region.Value)]),
                FactColumn.OfInt64(TypeId, [.. rows.Select(row => (long)row.TypeId)]),
                FactColumn.OfDouble(Average, [.. rows.Select(row => (double)row.Average)]),
                FactColumn.OfDouble(Highest, [.. rows.Select(row => (double)row.Highest)]),
                FactColumn.OfDouble(Lowest, [.. rows.Select(row => (double)row.Lowest)]),
                FactColumn.OfInt64(OrderCount, [.. rows.Select(row => row.OrderCount)]),
                FactColumn.OfInt64(Volume, [.. rows.Select(row => row.Volume)]),
            ]);
    }
}
