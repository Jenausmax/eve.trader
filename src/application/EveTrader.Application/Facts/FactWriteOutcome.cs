namespace EveTrader.Application.Facts;

/// <summary>Чем закончилась запись наблюдения.</summary>
public enum FactWriteOutcome
{
    /// <summary>Строки записаны и подтверждены покрытием — они стали фактами.</summary>
    Written = 0,

    /// <summary>
    /// Наблюдение с таким идентификатором уже подтверждено. Состав фактов не изменился —
    /// повторная подача не удваивает.
    /// </summary>
    AlreadyPresent = 1,
}
