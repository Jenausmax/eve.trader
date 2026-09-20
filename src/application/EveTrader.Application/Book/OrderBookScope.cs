using EveTrader.Domain.Facts;

namespace EveTrader.Application.Book;

/// <summary>
/// Что именно просят у источника стакана.
/// </summary>
/// <param name="Within">Интервал моментов наблюдения.</param>
/// <param name="Regions">
/// Регионы; пустой набор — все, какие даёт источник. Фильтр назван явно, потому что
/// архивный снимок глобален: полный снимок — это миллион шестьсот тысяч ордеров, и
/// свёртка всех регионов ради одного стоила бы ровно столько же.
/// </param>
public sealed record OrderBookScope(TimeRange Within, IReadOnlyList<RegionId> Regions)
{
    /// <summary>Весь интервал по всем регионам, какие даёт источник.</summary>
    public static OrderBookScope All(TimeRange within) => new(within, []);
}
