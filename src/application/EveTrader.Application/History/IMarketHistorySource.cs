namespace EveTrader.Application.History;

/// <summary>
/// Единый вход дневной истории. Его реализуют внешний архив и живой ESI, и это не
/// удобство, а проверка: если стадии после приёма где-то различают источник, различие
/// вылезет здесь — на простом наборе, а не позже на сложном.
/// </summary>
public interface IMarketHistorySource
{
    /// <summary>Имя источника; попадает в запись покрытия.</summary>
    string Name { get; }

    /// <summary>
    /// Наблюдения за указанный охват, сгруппированные по календарным суткам. Источник
    /// вправе не вернуть сутки вовсе — например, если с прошлой загрузки они не менялись.
    /// </summary>
    IAsyncEnumerable<MarketHistoryObservation> ObserveAsync(
        MarketHistoryScope scope,
        CancellationToken cancellationToken);
}
