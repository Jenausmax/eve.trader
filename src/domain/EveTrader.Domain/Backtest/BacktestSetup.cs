using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Domain.Backtest;

/// <summary>Условия прогона: набор, станция, интервал, шаг, горизонт и порог неизвестных исходов.</summary>
/// <param name="Parameters">Набор параметров правила.</param>
/// <param name="Scope">Станция.</param>
/// <param name="Interval">Интервал моментов решения.</param>
/// <param name="Step">Шаг решений.</param>
/// <param name="Horizon">Горизонт оценки исхода; называется в отчёте.</param>
/// <param name="MaxUnknownShare">
/// Доля сигналов с неизвестным исходом, сверх которой прогон доказательным не является.
/// </param>
/// <param name="StaticData">Версия статических данных.</param>
/// <param name="RanAt">Момент прогона — приходит снаружи: домену «сейчас» недоступно.</param>
public sealed record BacktestSetup(
    StationTradingParameters Parameters,
    StationTradingScope Scope,
    TimeRange Interval,
    TimeSpan Step,
    TimeSpan Horizon,
    decimal MaxUnknownShare,
    StaticDataVersion StaticData,
    DateTimeOffset RanAt);
