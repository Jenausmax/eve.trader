namespace EveTrader.Application.Series;

/// <summary>Итог материализации рядов.</summary>
/// <param name="Windows">Окон рассмотрено.</param>
/// <param name="Refused">Окон отклонено: внутри был ненаблюдавшийся интервал.</param>
/// <param name="Incomplete">Окон допущено с пометкой неполноты.</param>
/// <param name="Points">Точек посчитано.</param>
/// <param name="Written">Порций записано.</param>
/// <param name="AlreadyPresent">Порций уже было подтверждено.</param>
public sealed record SeriesMaterializationReport(
    int Windows,
    int Refused,
    int Incomplete,
    int Points,
    int Written,
    int AlreadyPresent);
