using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Реестр материализованных интервалов — единственное основание для ответа на вопрос,
/// есть ли данные локально. Перечисление файлов таким основанием не является: файл может
/// лежать неподтверждённым, а подтверждённого файла может не быть на месте.
/// </summary>
public interface IMaterializationRegistry
{
    Task<IReadOnlyList<MaterializedInterval>> ReadAsync(FactSet set, CancellationToken cancellationToken);

    /// <summary>
    /// Отмечает интервал материализованным. Единица — интервал времени: архивные снимки
    /// глобальны на момент и по регионам не делятся, поэтому за загруженный интервал
    /// отмечаются все присутствовавшие регионы.
    /// </summary>
    Task RecordAsync(
        FactSet set,
        TimeRange range,
        IReadOnlyCollection<RegionId> regions,
        string source,
        DateTimeOffset loadedAt,
        CancellationToken cancellationToken);

    /// <summary>Материализован ли интервал по региону — ответ по реестру.</summary>
    Task<bool> IsMaterializedAsync(FactSet set, RegionId region, TimeRange range, CancellationToken cancellationToken);

    /// <summary>
    /// Применяет окно локального хранения: интервалы вне окна перестают числиться
    /// материализованными и становятся восполнимыми, если источник их публикует.
    /// Сами данные не удаляются — удаление сырья запрещено.
    /// </summary>
    Task<IReadOnlyList<MaterializedInterval>> ApplyWindowAsync(
        FactSet set,
        MaterializationWindow window,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
