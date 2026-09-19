using System.Runtime.InteropServices;

namespace EveTrader.Domain.Book;

/// <summary>
/// Ордер, каким его видно в наблюдении. Структура фиксированного размера: миллион
/// объектов на наблюдение — это паузы сборщика вместо работы, поэтому стакан живёт
/// массивом структур, а не коллекцией ссылок.
///
/// Времени «сейчас» здесь нет и быть не может: <see cref="IssuedUnix" /> приходит от
/// источника, время наблюдения передаётся отдельно аргументом.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct OrderSnapshot
{
    public OrderSnapshot(
        long orderId,
        int typeId,
        long locationId,
        bool isBuy,
        IskPrice price,
        long volumeRemain,
        long volumeTotal,
        short durationDays,
        long issuedUnix)
    {
        OrderId = orderId;
        TypeId = typeId;
        LocationId = locationId;
        IsBuy = isBuy;
        Price = price;
        VolumeRemain = volumeRemain;
        VolumeTotal = volumeTotal;
        DurationDays = durationDays;
        IssuedUnix = issuedUnix;
    }

    public long OrderId { get; }

    public int TypeId { get; }

    public long LocationId { get; }

    public bool IsBuy { get; }

    public IskPrice Price { get; }

    public long VolumeRemain { get; }

    public long VolumeTotal { get; }

    public short DurationDays { get; }

    /// <summary>
    /// Момент последней правки ордера в секундах эпохи — не дата создания. Правка
    /// сбрасывает длительность ордера, и это единственный источник точного времени
    /// события во всей конструкции.
    /// </summary>
    public long IssuedUnix { get; }

    public DateTimeOffset Issued => DateTimeOffset.FromUnixTimeSeconds(IssuedUnix);

    /// <summary>Пара «тип и локация» — единица, по которой считаются признаки стакана.</summary>
    public BookKey Key => new(TypeId, LocationId, IsBuy);
}
