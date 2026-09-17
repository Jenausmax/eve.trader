namespace EveTrader.Domain.Facts;

/// <summary>
/// Запись реестра материализации: что лежит локально. Реестр — единственное основание
/// для ответа на вопрос о наличии данных; перечисление файлов таким основанием не является.
/// </summary>
/// <param name="Set">Набор фактов.</param>
/// <param name="Region">Регион.</param>
/// <param name="Range">Границы материализованного интервала.</param>
/// <param name="Source">Откуда загружено.</param>
/// <param name="LoadedAt">Когда загружено.</param>
public sealed record MaterializedInterval(
    FactSet Set,
    RegionId Region,
    TimeRange Range,
    string Source,
    DateTimeOffset LoadedAt);
