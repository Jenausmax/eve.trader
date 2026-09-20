namespace EveTrader.Application.Book;

/// <summary>Итог прогона конвертации архива стакана.</summary>
/// <param name="Source">Источник.</param>
/// <param name="SnapshotsPublished">Сколько снимков источник публикует в интервале.</param>
/// <param name="SnapshotsRead">Сколько снимков скачано и свёрнуто.</param>
/// <param name="ResumedFrom">С какого снимка продолжен прогон; пусто, если начат с начала.</param>
/// <param name="ObservationsWritten">Сколько наблюдений региона записано.</param>
/// <param name="ObservationsAlreadyPresent">Сколько наблюдений уже были подтверждены покрытием.</param>
/// <param name="EventsWritten">Сколько событий жизни ордера записано.</param>
/// <param name="SourceGaps">Сколько раз ордер пропадал из снимка и возвращался.</param>
/// <param name="Regions">Сколько различных регионов встретилось.</param>
/// <param name="Days">Сколько календарных суток отмечено материализованными.</param>
public sealed record OrderBookImportReport(
    string Source,
    int SnapshotsPublished,
    int SnapshotsRead,
    DateTimeOffset? ResumedFrom,
    int ObservationsWritten,
    int ObservationsAlreadyPresent,
    long EventsWritten,
    long SourceGaps,
    int Regions,
    int Days);
