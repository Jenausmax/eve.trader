namespace EveTrader.Domain.Coverage;

/// <summary>
/// Три состояния, которые в данных выглядят одинаково — как отсутствие событий.
/// Их неразличение превращает незнание в факт о рынке.
/// </summary>
public enum CoverageState
{
    /// <summary>Данные есть; отсутствие событий — факт о рынке.</summary>
    Observed = 0,

    /// <summary>Локально нет, но интервал доступен наверху и восполним.</summary>
    NotMaterialized = 1,

    /// <summary>Данных нет ни локально, ни наверху; знание утрачено безвозвратно.</summary>
    NotObserved = 2,
}
