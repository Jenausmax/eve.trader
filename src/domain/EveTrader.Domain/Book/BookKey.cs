namespace EveTrader.Domain.Book;

/// <summary>Сторона стакана по паре «тип и локация».</summary>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="LocationId">Локация.</param>
/// <param name="IsBuy">Сторона покупки.</param>
public readonly record struct BookKey(int TypeId, long LocationId, bool IsBuy)
{
    /// <summary>Та же пара без стороны — по ней сводятся признаки двустороннего стакана.</summary>
    public (int TypeId, long LocationId) Pair => (TypeId, LocationId);
}
