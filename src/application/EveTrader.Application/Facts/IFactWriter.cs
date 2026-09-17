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
    /// Записывает порцию строк и подтверждает её записью покрытия. Наблюдение
    /// становится фактами целиком либо не становится вовсе.
    /// </summary>
    Task<FactWriteOutcome> WriteAsync(FactBatch batch, CoverageEntry coverage, CancellationToken cancellationToken);

    /// <summary>
    /// Записывает покрытие без строк данных — исход «не изменилось» или отказ.
    /// Наблюдение состоялось, событий нет.
    /// </summary>
    Task<FactWriteOutcome> WriteCoverageOnlyAsync(CoverageEntry coverage, CancellationToken cancellationToken);
}
