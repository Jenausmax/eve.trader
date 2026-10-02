namespace EveTrader.Domain.Series;

/// <summary>Пара «тип и локация» без стороны — единица, по которой собираются снимки.</summary>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="LocationId">Локация.</param>
public readonly record struct BookPair(int TypeId, long LocationId);
