namespace EveTrader.Domain.Book;

/// <summary>
/// Настройки вычисления признаков.
///
/// Охват признаков задаётся отдельно от охвата наблюдения: признаки — перестраиваемый
/// кэш, а не сырьё, поэтому их можно считать узко и расширить задним числом, не потеряв
/// ничего.
/// </summary>
/// <param name="DepthThresholdsBasisPoints">
/// Отклонения от лучшей цены в сотых долях процента, в пределах которых считается
/// доступный объём. Набор порогов — параметр: менять его дёшево, пока признаки
/// выводимы из сырья.
/// </param>
/// <param name="IncludePair">
/// Какие пары «тип и локация» материализуются. <see langword="null" /> — все.
/// </param>
public sealed record FeatureOptions(
    IReadOnlyList<int> DepthThresholdsBasisPoints,
    Func<int, long, bool>? IncludePair)
{
    /// <summary>Один процент и пять процентов от лучшей цены.</summary>
    public static FeatureOptions Default { get; } = new([100, 500], null);

    /// <summary>Признаки не считаются вовсе.</summary>
    public static FeatureOptions None { get; } = new([], static (_, _) => false);

    public bool Includes(int typeId, long locationId) =>
        IncludePair is null || IncludePair(typeId, locationId);
}
