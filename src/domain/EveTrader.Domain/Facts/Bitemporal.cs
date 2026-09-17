namespace EveTrader.Domain.Facts;

/// <summary>
/// Отбор версии факта по времени получения — то, что делает бэктест честным.
/// Запрос на момент не может увидеть строку, записанную позже этого момента, даже если
/// она описывает более раннее событие.
/// </summary>
public static class Bitemporal
{
    /// <summary>
    /// Последняя версия каждого факта, известная системе на указанный момент. Строки с
    /// более поздним временем получения не рассматриваются вовсе.
    /// </summary>
    public static IReadOnlyList<T> AsOf<T>(IEnumerable<T> rows, DateTimeOffset instant)
        where T : IBitemporalFact
    {
        ArgumentNullException.ThrowIfNull(rows);

        return Pick(rows.Where(row => row.KnownAt <= instant));
    }

    /// <summary>Последняя известная версия каждого факта, без ограничения по времени.</summary>
    public static IReadOnlyList<T> Latest<T>(IEnumerable<T> rows)
        where T : IBitemporalFact
    {
        ArgumentNullException.ThrowIfNull(rows);

        return Pick(rows);
    }

    /// <summary>
    /// Берёт по ключу строку с наибольшим временем получения. Тай-брейк по
    /// идентификатору наблюдения, а порядок результата — по ключу: без этого два
    /// прогона одного запроса дали бы разный порядок строк, и реплей перестал бы быть
    /// эталоном.
    /// </summary>
    public static IReadOnlyList<T> Pick<T>(IEnumerable<T> rows)
        where T : IBitemporalFact =>
        [.. rows
            .GroupBy(static row => row.FactKey, StringComparer.Ordinal)
            .Select(static versions => versions
                .OrderByDescending(static row => row.KnownAt)
                .ThenByDescending(static row => row.Observation.Value, StringComparer.Ordinal)
                .First())
            .OrderBy(static row => row.FactKey, StringComparer.Ordinal)];
}
