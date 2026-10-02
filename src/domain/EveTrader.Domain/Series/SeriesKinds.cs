using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>Свойства видов признаков: из чего выводятся и как называются в ключе.</summary>
public static class SeriesKinds
{
    /// <summary>
    /// Наборы фактов, из которых вид выводится.
    ///
    /// Перечень задаётся видом, а не вызывающим: вызывающий мог бы объявить источники не
    /// те, а ряд с неверно названным происхождением хуже отсутствующего — по нему нельзя
    /// решить, надо ли его пересчитывать после переливки сырья.
    /// </summary>
    public static IReadOnlyList<FactSet> SourcesOf(SeriesKind kind) => kind switch
    {
        SeriesKind.ObservedTurnover => [FactSet.OrderEvents],
        SeriesKind.RelistPressure => [FactSet.OrderEvents],
        SeriesKind.FilledDisappearanceShare => [FactSet.OrderEvents],
        SeriesKind.BestPriceHold => [FactSet.BookFeatures],
        SeriesKind.CompetitorDepth => [FactSet.BookFeatures],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид признака"),
    };

    public static string PathSegment(SeriesKind kind) => kind switch
    {
        SeriesKind.ObservedTurnover => "turnover",
        SeriesKind.RelistPressure => "relist-pressure",
        SeriesKind.FilledDisappearanceShare => "filled-share",
        SeriesKind.BestPriceHold => "best-price-hold",
        SeriesKind.CompetitorDepth => "competitor-depth",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид признака"),
    };

    /// <summary>Нужна ли виду полоса вокруг лучшей цены.</summary>
    public static bool UsesBand(SeriesKind kind) => kind is SeriesKind.CompetitorDepth;
}
