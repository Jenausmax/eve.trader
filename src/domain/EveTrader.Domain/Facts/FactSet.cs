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

    /// <summary>Ряды производных признаков. Производное: перестраивается из событий и признаков.</summary>
    FeatureSeries = 6,

    /// <summary>Исходы рассмотрения пар правилом сигнала. Производное: перестраивается из рядов.</summary>
    Signals = 7,

    /// <summary>
    /// Отчёты о прогонах бэктеста. Запись о том, что прогон состоялся и чем кончился, —
    /// сравнение наборов параметров строится по ним, а не повторным прогоном.
    /// </summary>
    BacktestReports = 8,
}
