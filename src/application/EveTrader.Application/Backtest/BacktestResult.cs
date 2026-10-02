using EveTrader.Application.Signals;
using EveTrader.Domain.Backtest;

namespace EveTrader.Application.Backtest;

/// <summary>Итог прогона: отчёт, исходы каждого сигнала и всё, что рассмотрено.</summary>
/// <param name="Report">Отчёт — он же записывается фактом.</param>
/// <param name="Assessed">Сигналы с исходами, по моменту решения.</param>
/// <param name="Run">Исходы рассмотрения всех пар и сводка по моментам.</param>
public sealed record BacktestResult(
    BacktestReport Report,
    IReadOnlyList<AssessedSignal> Assessed,
    SignalRun Run);
