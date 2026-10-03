namespace EveTrader.Domain.Book;

/// <summary>
/// Настройки вычисления признаков.
///
/// Охват признаков задаётся отдельно от охвата наблюдения: признаки — перестраиваемый
/// кэш, а не сырьё, поэтому их можно считать узко и расширить задним числом, не потеряв
/// ничего.
///
/// Охвата по умолчанию нет. Раньше умолчанием были все пары, и это умолчание стоило
/// 770 ГиБ за окно против ~62 ГиБ сырья; теперь отсутствие объявления — отказ, а не
/// полный охват.
/// </summary>
/// <param name="DepthThresholdsBasisPoints">
/// Отклонения от лучшей цены в сотых долях процента, в пределах которых считаются
/// доступный объём и число ордеров. Набор порогов — параметр: менять его дёшево, пока
/// признаки выводимы из сырья.
/// </param>
/// <param name="Coverage">
/// Объявленный охват пар. <see langword="null" /> — не объявлен, и материализация такого
/// прогона отклоняется (<see cref="EnsureDeclared" />).
/// </param>
public sealed record FeatureOptions(
    IReadOnlyList<int> DepthThresholdsBasisPoints,
    FeatureCoverage? Coverage)
{
    /// <summary>Один процент и пять процентов от лучшей цены.</summary>
    public static IReadOnlyList<int> DefaultThresholds { get; } = [100, 500];

    /// <summary>
    /// Пороги заданы, охват — нет. Такое значение получает настройка, в которой охват не
    /// объявили, и прогон с ним отклоняется.
    /// </summary>
    public static FeatureOptions Undeclared { get; } = new(DefaultThresholds, null);

    /// <summary>Все пары — объявленно, для перестройки записанного полным охватом.</summary>
    public static FeatureOptions AllPairs { get; } = new(DefaultThresholds, FeatureCoverage.AllPairs);

    /// <summary>Признаки не считаются вовсе.</summary>
    public static FeatureOptions None { get; } = new([], FeatureCoverage.Nothing);

    public bool IsDeclared => Coverage is not null;

    /// <summary>Пороги по умолчанию и объявленный охват.</summary>
    public static FeatureOptions For(FeatureCoverage coverage) => new(DefaultThresholds, coverage);

    /// <summary>
    /// Входит ли пара в охват. Необъявленный охват не включает ничего: материализовать
    /// по нему нечего, а отказ прогона — забота <see cref="EnsureDeclared" />.
    /// </summary>
    public bool Includes(int typeId, long locationId) =>
        Coverage is { } coverage && coverage.Includes(typeId, locationId);

    /// <summary>Отклоняет прогон без объявленного охвата — с указанием, что охват обязателен.</summary>
    public void EnsureDeclared()
    {
        if (!IsDeclared)
        {
            throw new InvalidOperationException(
                "Охват признаков не объявлен: материализация признаков без объявленного охвата запрещена. " +
                "Объявите перечень пар — основанием служат пары, нужные правилам сигналов.");
        }
    }
}
