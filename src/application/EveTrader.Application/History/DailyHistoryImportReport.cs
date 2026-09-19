namespace EveTrader.Application.History;

/// <summary>Итог прогона импорта.</summary>
/// <param name="Source">Источник.</param>
/// <param name="DaysWritten">Сколько суток записано.</param>
/// <param name="DaysAlreadyPresent">Сколько суток уже были подтверждены покрытием.</param>
/// <param name="DaysUnchanged">Сколько суток источник не отдал — с прошлой загрузки не менялись.</param>
/// <param name="RowsWritten">Сколько строк записано.</param>
/// <param name="Regions">Сколько различных регионов встретилось.</param>
/// <param name="Types">Сколько различных типов встретилось.</param>
public sealed record DailyHistoryImportReport(
    string Source,
    int DaysWritten,
    int DaysAlreadyPresent,
    int DaysUnchanged,
    long RowsWritten,
    int Regions,
    int Types);
