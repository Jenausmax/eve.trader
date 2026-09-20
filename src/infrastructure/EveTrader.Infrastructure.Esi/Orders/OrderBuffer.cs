using EveTrader.Domain.Book;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Буфер ордеров наблюдения, переиспользуемый между опросами.
///
/// Растёт только вверх и никогда не пересоздаётся под меньший размер: новый массив на
/// миллион структур каждые пять минут фрагментирует кучу больших объектов.
/// </summary>
internal sealed class OrderBuffer
{
    /// <summary>Страница ESI — тысяча ордеров; столько и берём на страницу.</summary>
    public const int OrdersPerPage = 1000;

    public OrderSnapshot[] Items { get; private set; } = new OrderSnapshot[4096];

    public void EnsureFor(int pages) =>
        Items = Items.Length >= Math.Max(4096, pages * OrdersPerPage)
            ? Items
            : new OrderSnapshot[Math.Max(4096, pages * OrdersPerPage)];
}
