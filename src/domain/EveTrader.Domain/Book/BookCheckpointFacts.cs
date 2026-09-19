using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Перевод полного состава стакана в порцию фактов — суточный чекпойнт.
///
/// Без чекпойнтов восстановление состояния на момент сворачивает всю историю региона:
/// через год это миллиарды строк на один запрос, то есть требование записано, а
/// исполнить его нельзя.
/// </summary>
public static class BookCheckpointFacts
{
    public const string OrderId = "order_id";
    public const string TypeId = "type_id";
    public const string LocationId = "location_id";
    public const string IsBuy = "is_buy";
    public const string PriceCents = "price_cents";
    public const string VolumeRemain = "volume_remain";
    public const string VolumeTotal = "volume_total";
    public const string DurationDays = "duration_days";
    public const string IssuedUnix = "issued_unix";

    public static FactBatch ToBatch(
        RegionId region,
        ObservationId observation,
        DateOnly observedDate,
        ReadOnlySpan<OrderSnapshot> orders,
        ObservationMeta meta,
        StaticDataVersion staticData)
    {
        ArgumentNullException.ThrowIfNull(meta);

        // Чекпойнт описывает состав на момент наблюдения, а не событие: время события —
        // тот же момент, что и время наблюдения.
        var at = EventTime.At(meta.Collected.To);

        var envelopes = new List<FactEnvelope>(orders.Length);
        var ids = new long[orders.Length];
        var types = new long[orders.Length];
        var locations = new long[orders.Length];
        var sides = new long[orders.Length];
        var prices = new long[orders.Length];
        var remains = new long[orders.Length];
        var totals = new long[orders.Length];
        var durations = new long[orders.Length];
        var issued = new long[orders.Length];

        for (var index = 0; index < orders.Length; index++)
        {
            ref readonly OrderSnapshot order = ref orders[index];

            envelopes.Add(new FactEnvelope(
                string.Create(CultureInfo.InvariantCulture, $"checkpoint/{region.Value}/{meta.Collected.To:yyyyMMddTHHmmssZ}/{order.OrderId}"),
                at,
                meta.Collected.To,
                observation,
                staticData));

            ids[index] = order.OrderId;
            types[index] = order.TypeId;
            locations[index] = order.LocationId;
            sides[index] = order.IsBuy ? 1L : 0L;
            prices[index] = order.Price.Cents;
            remains[index] = order.VolumeRemain;
            totals[index] = order.VolumeTotal;
            durations[index] = order.DurationDays;
            issued[index] = order.IssuedUnix;
        }

        return FactBatch.Of(
            FactSet.BookCheckpoints,
            region,
            observation,
            observedDate,
            envelopes,
            [
                FactColumn.OfInt64(OrderId, ids),
                FactColumn.OfInt64(TypeId, types),
                FactColumn.OfInt64(LocationId, locations),
                FactColumn.OfInt64(IsBuy, sides),
                FactColumn.OfInt64(PriceCents, prices),
                FactColumn.OfInt64(VolumeRemain, remains),
                FactColumn.OfInt64(VolumeTotal, totals),
                FactColumn.OfInt64(DurationDays, durations),
                FactColumn.OfInt64(IssuedUnix, issued),
            ]);
    }
}
