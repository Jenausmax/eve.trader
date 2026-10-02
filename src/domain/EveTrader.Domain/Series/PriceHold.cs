using EveTrader.Domain.Book;

namespace EveTrader.Domain.Series;

/// <summary>Завершившееся удержание лучшей цены на стороне стакана.</summary>
/// <param name="Held">Сколько цена продержалась лучшей.</param>
/// <param name="Price">Цена, которая держалась.</param>
public sealed record PriceHold(TimeSpan Held, IskPrice Price)
{
    public double Seconds => Held.TotalSeconds;
}
