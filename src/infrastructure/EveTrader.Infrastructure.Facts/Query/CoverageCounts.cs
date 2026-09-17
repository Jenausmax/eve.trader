namespace EveTrader.Infrastructure.Facts.Query;

/// <summary>Счётчики покрытия по региону, посчитанные запросом.</summary>
/// <param name="Observations">Сколько наблюдений записано.</param>
/// <param name="SourceGaps">Сколько ордеров пропало и вернулось.</param>
/// <param name="Partial">Сколько наблюдений неполные.</param>
/// <param name="Failed">Сколько попыток завершились отказом.</param>
public readonly record struct CoverageCounts(long Observations, long SourceGaps, long Partial, long Failed);
