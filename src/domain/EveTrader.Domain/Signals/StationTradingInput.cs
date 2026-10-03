using EveTrader.Domain.Book;
using EveTrader.Domain.Series;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Всё, что правило видит о паре в момент решения, — и ничего сверх.
///
/// Момент решения приходит аргументом: домену «сейчас» недоступно, и бой отличается от
/// бэктеста только тем, какой момент и какой горизонт чтения подставлены снаружи.
/// </summary>
/// <param name="Scope">Станция.</param>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="Decision">Момент решения — конец окон рядов.</param>
/// <param name="Step">Шаг рядов и решений.</param>
/// <param name="Quote">Последний снимок стакана по паре не позже момента решения.</param>
/// <param name="Series">Приговоры окнам региона и точки рядов этой пары на момент решения.</param>
public sealed record StationTradingInput(
    StationTradingScope Scope,
    int TypeId,
    DateTimeOffset Decision,
    TimeSpan Step,
    BookFeatures? Quote,
    SeriesComputed Series);
