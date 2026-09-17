namespace EveTrader.Domain.Facts;

/// <summary>Набор фактов в озере. Имя набора — часть пути, см. <see cref="FactSets" />.</summary>
public enum FactSet
{
    OrderEvents = 0,
    OrderBaselines = 1,
    BookCheckpoints = 2,
    BookFeatures = 3,
    HistoryDaily = 4,
    Coverage = 5,
}
