using EveTrader.Domain.Signals;

namespace EveTrader.Application.Signals;

/// <summary>Что рассмотреть: набор параметров, станцию и моменты решения.</summary>
/// <param name="Parameters">Набор параметров правила.</param>
/// <param name="Scope">Станция — перечень пар, которые правило читает.</param>
/// <param name="Step">Шаг рядов и решений.</param>
/// <param name="Decisions">Моменты решения — концы окон рядов.</param>
/// <param name="Thresholds">Пороги, с которыми записаны признаки стакана.</param>
public sealed record SignalRequest(
    StationTradingParameters Parameters,
    StationTradingScope Scope,
    TimeSpan Step,
    IReadOnlyList<DateTimeOffset> Decisions,
    IReadOnlyList<int> Thresholds);
