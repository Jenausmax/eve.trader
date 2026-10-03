using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Domain.Backtest;

/// <summary>Сигнал и его исход на названном горизонте.</summary>
/// <param name="Signal">Сигнал.</param>
/// <param name="Horizon">Горизонт оценки — от момента решения вперёд.</param>
/// <param name="Outcome">Исход.</param>
/// <param name="Realized">
/// Результат на единицу товара: маржа сигнала при успехе, потерянная брокерская комиссия
/// при неудаче. <see langword="null" /> при неизвестном исходе — у него результата нет.
/// </param>
public sealed record AssessedSignal(
    StationTradingVerdict Signal,
    TimeRange Horizon,
    SignalOutcome Outcome,
    IskAmount? Realized);
