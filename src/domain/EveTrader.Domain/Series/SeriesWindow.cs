using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Окно ряда вместе с приговором о его покрытии.
///
/// Здесь проходит граница, ради которой заводился журнал покрытия: «оборота не было» и
/// «мы не смотрели» выглядят в данных одинаково, и без этого разбора пробел наблюдения
/// уехал бы в обучение рыночным утверждением, которого никто не делал.
/// </summary>
/// <param name="Range">Границы окна.</param>
/// <param name="Admission">Что делать с точкой.</param>
/// <param name="Coverage">Состояние покрытия окна — причина отказа, когда он есть.</param>
/// <param name="PartialObservations">Сколько наблюдений в окне получили не все страницы.</param>
public sealed record SeriesWindow(
    TimeRange Range,
    SeriesAdmission Admission,
    CoverageState Coverage,
    int PartialObservations)
{
    public bool IsAdmitted => Admission is not SeriesAdmission.Refused;

    public bool IsIncomplete => Admission is SeriesAdmission.AdmittedIncomplete;

    /// <summary>
    /// Разбор покрытия окна.
    ///
    /// Восполнимый пробел отказывает так же, как безвозвратный: восполнимость говорит,
    /// что данные можно догрузить, а не что они уже есть. Считать по ним сейчас нельзя
    /// ни в том, ни в другом случае — разница только в том, лечится ли это загрузкой.
    /// </summary>
    public static SeriesWindow Of(TimeRange range, CoverageVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.State is not CoverageState.Observed
            ? new SeriesWindow(
                range, SeriesAdmission.Refused, verdict.State, verdict.PartialObservations)
            : new SeriesWindow(
            range,
            verdict.PartialObservations > 0 ? SeriesAdmission.AdmittedIncomplete : SeriesAdmission.Admitted,
            CoverageState.Observed,
            verdict.PartialObservations);
    }
}
