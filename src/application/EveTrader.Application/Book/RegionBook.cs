using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Book;

/// <summary>
/// Стакан одного региона внутри снимка.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Orders">Ордера региона.</param>
/// <param name="Collected">
/// Границы интервала сбора этого региона. Атомарного среза источник не даёт: полный
/// обход рынка занимает у него около шести минут, и границы берутся по крайним отметкам
/// времени самих строк, а не по имени файла.
/// </param>
public sealed record RegionBook(
    RegionId Region,
    IReadOnlyList<OrderSnapshot> Orders,
    TimeRange Collected);
