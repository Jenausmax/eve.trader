using EveTrader.Domain.Series;

namespace EveTrader.Application.Signals;

/// <summary>
/// Что было на момент решения в целом: окна рядов и сколько пар каким исходом кончилось.
///
/// Нужна для причин молчания там, где пар рассматривать не из чего: окно отклонено,
/// снимков в нём нет, — и пустой список исходов иначе не отличался бы от «рассмотрели
/// всё и ничего не нашли».
/// </summary>
/// <param name="Decision">Момент решения.</param>
/// <param name="Admission">Худший допуск окон рядов, которые читает правило.</param>
/// <param name="Coverage">Состояние покрытия окна.</param>
/// <param name="Considered">Пар рассмотрено.</param>
/// <param name="Signals">Сигналов.</param>
/// <param name="ConditionsNotMet">Пар, где данных хватило, а условия не выполнены.</param>
/// <param name="InsufficientData">Пар, где данных не хватило.</param>
public sealed record DecisionSummary(
    DateTimeOffset Decision,
    SeriesAdmission? Admission,
    Domain.Coverage.CoverageState? Coverage,
    int Considered,
    int Signals,
    int ConditionsNotMet,
    int InsufficientData);
