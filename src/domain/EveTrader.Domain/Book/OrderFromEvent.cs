namespace EveTrader.Domain.Book;

/// <summary>Восстановление состояния ордера из описывающего его события.</summary>
internal static class OrderFromEvent
{
    /// <summary>
    /// Ордер после события. Событие несёт состояние ордера уже после себя, поэтому
    /// знать, что было до, не требуется.
    /// </summary>
    /// <param name="moment">Событие.</param>
    /// <param name="volumeTotal">
    /// Полный объём, если он известен из прежнего состава. Событие его не несёт: оно
    /// описывает изменение, а полный объём у ордера не меняется.
    /// </param>
    public static OrderSnapshot Rebuild(in OrderEvent moment, long? volumeTotal = null) =>
        new(
            moment.OrderId,
            moment.TypeId,
            moment.LocationId,
            moment.IsBuy,
            moment.Price,
            moment.VolumeRemain,
            volumeTotal ?? moment.VolumeRemain,
            moment.DurationDays,
            moment.IssuedUnix);
}
