namespace EveTrader.Application.Intake;

/// <summary>Итог приёма наблюдений.</summary>
/// <param name="Source">Источник.</param>
/// <param name="Written">Наблюдений записано.</param>
/// <param name="AlreadyPresent">Наблюдений уже было подтверждено.</param>
/// <param name="Unchanged">Ответов «не изменилось».</param>
/// <param name="Partial">Частичных наблюдений.</param>
/// <param name="Failed">Отказов.</param>
/// <param name="Events">Событий порождено.</param>
/// <param name="SourceGaps">Пропусков источника учтено.</param>
/// <param name="Regions">Регионов встречено.</param>
public sealed record IntakeReport(
    string Source,
    int Written,
    int AlreadyPresent,
    int Unchanged,
    int Partial,
    int Failed,
    int Events,
    int SourceGaps,
    int Regions);
