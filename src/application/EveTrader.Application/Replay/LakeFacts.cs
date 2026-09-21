using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Domain.Book;

namespace EveTrader.Application.Replay;

/// <summary>
/// Чтение строк озера обратно в доменные типы.
///
/// Обратное преобразование живёт здесь, а не рядом с записью, потому что нужно оно
/// только реплею: боевой путь пишет факты и больше к ним как к ордерам не возвращается.
/// </summary>
internal static class LakeFacts
{
    public static OrderSnapshot CheckpointOrder(FactRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new OrderSnapshot(
            Int64Of(row, BookCheckpointFacts.OrderId),
            (int)Int64Of(row, BookCheckpointFacts.TypeId),
            Int64Of(row, BookCheckpointFacts.LocationId),
            Int64Of(row, BookCheckpointFacts.IsBuy) == 1,
            IskPrice.FromCents(Int64Of(row, BookCheckpointFacts.PriceCents)),
            Int64Of(row, BookCheckpointFacts.VolumeRemain),
            Int64Of(row, BookCheckpointFacts.VolumeTotal),
            (short)Int64Of(row, BookCheckpointFacts.DurationDays),
            Int64Of(row, BookCheckpointFacts.IssuedUnix));
    }

    public static OrderEvent Event(FactRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new OrderEvent(
            (OrderEventKind)Int64Of(row, OrderEventFacts.Kind),
            Int64Of(row, OrderEventFacts.OrderId),
            (int)Int64Of(row, OrderEventFacts.TypeId),
            Int64Of(row, OrderEventFacts.LocationId),
            Int64Of(row, OrderEventFacts.IsBuy) == 1,
            IskPrice.FromCents(Int64Of(row, OrderEventFacts.PriceCents)),
            IskPrice.FromCents(Int64Of(row, OrderEventFacts.PreviousPriceCents)),
            Int64Of(row, OrderEventFacts.VolumeRemain),
            Int64Of(row, OrderEventFacts.FilledVolume),
            row.Envelope.EventTime,
            row.Envelope.KnownAt,
            Int64Of(row, OrderEventFacts.IssuedUnix),
            (short)Int64Of(row, OrderEventFacts.DurationDays),
            Int64Of(row, OrderEventFacts.IsNpc) == 1,
            Int64Of(row, OrderEventFacts.UndersampledStep) == 1);
    }

    public static long Int64Of(FactRow row, string column) =>
        row.Values.TryGetValue(column, out var value) && value is not null
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : 0L;
}
