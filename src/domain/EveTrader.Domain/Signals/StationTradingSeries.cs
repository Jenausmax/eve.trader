using EveTrader.Domain.Series;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Ряды, которые читает правило станционной торговли. Перечень один на материализацию и
/// на правило: ряд, который правило ждёт, а материализация не считает, дал бы вечное
/// «данных не хватило», и причина пряталась бы в расхождении двух списков.
/// </summary>
public static class StationTradingSeries
{
    /// <summary>Скорость оборота, давление перестановок, глубина конкуренции и размер слепой зоны.</summary>
    public static IReadOnlyList<SeriesDefinition> Definitions(TimeSpan window, TimeSpan step, int bandBasisPoints) =>
    [
        Turnover(window, step),
        RelistPressure(window, step),
        CompetitorDepth(window, step, bandBasisPoints),
        FilledShare(window, step),
    ];

    public static IReadOnlyList<SeriesDefinition> Definitions(StationTradingParameters parameters, TimeSpan step) =>
        Definitions(parameters.Window, step, parameters.CompetitorBandBasisPoints);

    public static SeriesDefinition Turnover(TimeSpan window, TimeSpan step) =>
        SeriesDefinition.Of(SeriesKind.ObservedTurnover, window, step);

    public static SeriesDefinition RelistPressure(TimeSpan window, TimeSpan step) =>
        SeriesDefinition.Of(SeriesKind.RelistPressure, window, step);

    public static SeriesDefinition CompetitorDepth(TimeSpan window, TimeSpan step, int bandBasisPoints) =>
        SeriesDefinition.Of(SeriesKind.CompetitorDepth, window, step, bandBasisPoints);

    public static SeriesDefinition FilledShare(TimeSpan window, TimeSpan step) =>
        SeriesDefinition.Of(SeriesKind.FilledDisappearanceShare, window, step);
}
