using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Запись наблюдения в озеро. Порядок жёсткий: данные во временный файл, атомарное
/// переименование, запись покрытия последней. Отсюда определение факта — строка,
/// подтверждённая покрытием, — и отсюда же идемпотентность: прерывание до записи
/// покрытия не оставляет фактов, а повторная подача того же наблюдения ничего не меняет.
/// </summary>
public interface IFactWriter
{
    /// <summary>
    /// Записывает порцию строк и подтверждает её записями покрытия. Наблюдение
    /// становится фактами целиком либо не становится вовсе.
    ///
    /// Записей несколько там, где одно наблюдение накрывает несколько регионов:
    /// источник дневной истории публикует сутки глобальным файлом на все регионы.
    /// </summary>
    Task<FactWriteOutcome> WriteAsync(
        FactBatch batch,
        IReadOnlyList<CoverageEntry> coverage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Записывает несколько порций одного наблюдения и подтверждает их одной записью
    /// покрытия.
    ///
    /// Одно наблюдение стакана порождает сразу несколько наборов — события, базовые
    /// линии, чекпойнт, признаки, — и подтверждать их порознь нельзя: покрытие
    /// подтверждает наблюдение целиком, а не отдельный набор. Запись порциями по одной
    /// оставила бы часть наборов без подтверждения, то есть невидимыми.
    /// </summary>
    Task<FactWriteOutcome> WriteAllAsync(
        IReadOnlyList<FactBatch> batches,
        IReadOnlyList<CoverageEntry> coverage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Записывает покрытие без строк данных — исход «не изменилось» или отказ.
    /// Наблюдение состоялось, событий нет.
    /// </summary>
    Task<FactWriteOutcome> WriteCoverageOnlyAsync(
        IReadOnlyList<CoverageEntry> coverage,
        CancellationToken cancellationToken);
}
