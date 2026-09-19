using System.Runtime.CompilerServices;

namespace EveTrader.Domain.Book;

/// <summary>Адресация в таблице <see cref="OrderIndex" />.</summary>
internal static class OrderIndexProbe
{
    /// <summary>
    /// Начальный слот для идентификатора.
    ///
    /// Перемешивание старших битов обязательно: идентификаторы ордеров идут почти
    /// подряд, и без него все они сели бы в один участок таблицы, превратив открытую
    /// адресацию в линейный список.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Slot(long orderId, int mask)
    {
        var hash = (ulong)orderId * 0x9E3779B97F4A7C15UL;

        return (int)(hash >> 32) & mask;
    }
}
