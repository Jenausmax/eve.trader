using EveTrader.Domain.Signals;

namespace EveTrader.Application.Signals;

/// <summary>Итог рассмотрения: исходы по парам и сводка по каждому моменту решения.</summary>
/// <param name="Verdicts">Исходы — по моменту решения, затем по типу предмета.</param>
/// <param name="Decisions">Сводка по моментам решения.</param>
public sealed record SignalRun(
    IReadOnlyList<StationTradingVerdict> Verdicts,
    IReadOnlyList<DecisionSummary> Decisions);
