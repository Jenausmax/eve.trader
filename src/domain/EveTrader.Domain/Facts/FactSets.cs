namespace EveTrader.Domain.Facts;

/// <summary>Свойства наборов фактов: сырьё против производного и сегмент пути в озере.</summary>
public static class FactSets
{
    /// <summary>
    /// Сырьё хранится бессрочно в пределах материализованных интервалов. Производное
    /// удаляется и перестраивается — в этом и разница: производное выводимо из сырья,
    /// сырьё не выводимо ни из чего.
    /// </summary>
    public static bool IsRaw(FactSet set) => set is not FactSet.BookFeatures;

    /// <summary>
    /// Входит ли регион в путь партиции. Для наблюдений стакана — да: они снимаются
    /// по региону, и партиция совпадает с единицей работы. Для дневной истории — нет:
    /// источник публикует сутки одним глобальным файлом на все регионы, и разнесение
    /// его по регионам дало бы сотни файлов по паре сотен строк вместо одного целого.
    /// Регион там живёт колонкой.
    /// </summary>
    public static bool PartitionsByRegion(FactSet set) => set is not FactSet.HistoryDaily;

    public static string PathSegment(FactSet set) => set switch
    {
        FactSet.OrderEvents => "order-events",
        FactSet.OrderBaselines => "order-baselines",
        FactSet.BookCheckpoints => "book-checkpoints",
        FactSet.BookFeatures => "book-features",
        FactSet.HistoryDaily => "history-daily",
        FactSet.Coverage => "coverage",
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, "Неизвестный набор фактов"),
    };
}
