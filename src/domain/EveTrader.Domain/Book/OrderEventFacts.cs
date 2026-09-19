using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Перевод событий жизни ордера в порцию фактов. Базовые линии уезжают в свой набор:
/// они калибровка, а не рыночные события, и смешивать их с появлениями нельзя.
/// </summary>
public static class OrderEventFacts
{
    public const string Kind = "kind";
    public const string OrderId = "order_id";
    public const string TypeId = "type_id";
    public const string LocationId = "location_id";
    public const string IsBuy = "is_buy";
    public const string PriceCents = "price_cents";
    public const string PreviousPriceCents = "previous_price_cents";
    public const string VolumeRemain = "volume_remain";
    public const string FilledVolume = "filled_volume";
    public const string IssuedUnix = "issued_unix";
    public const string DurationDays = "duration_days";
    public const string IsNpc = "is_npc";
    public const string UndersampledStep = "undersampled_step";

    /// <summary>
    /// Ключ события. Событие не уточняется новой версией — уточняется наблюдение; поэтому
    /// ключ несёт и момент наблюдения, и вид: повторная подача того же наблюдения даёт
    /// тот же ключ, а разные события одного ордера ключами не сталкиваются.
    /// </summary>
    public static string FactKey(in OrderEvent moment) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"event/{moment.OrderId}/{(int)moment.Kind}/{moment.ObservedAt:yyyyMMddTHHmmssZ}");

    public static FactBatch ToBatch(
        FactSet set,
        RegionId region,
        ObservationId observation,
        DateOnly observedDate,
        IReadOnlyList<OrderEvent> events,
        StaticDataVersion staticData)
    {
        ArgumentNullException.ThrowIfNull(events);

        var envelopes = new List<FactEnvelope>(events.Count);

        foreach (OrderEvent moment in events)
        {
            envelopes.Add(new FactEnvelope(
                FactKey(in moment), moment.EventTime, moment.ObservedAt, observation, staticData));
        }

        return FactBatch.Of(
            set,
            region,
            observation,
            observedDate,
            envelopes,
            [
                FactColumn.OfInt64(Kind, [.. events.Select(static e => (long)e.Kind)]),
                FactColumn.OfInt64(OrderId, [.. events.Select(static e => e.OrderId)]),
                FactColumn.OfInt64(TypeId, [.. events.Select(static e => (long)e.TypeId)]),
                FactColumn.OfInt64(LocationId, [.. events.Select(static e => e.LocationId)]),
                FactColumn.OfInt64(IsBuy, [.. events.Select(static e => e.IsBuy ? 1L : 0L)]),
                FactColumn.OfInt64(PriceCents, [.. events.Select(static e => e.Price.Cents)]),
                FactColumn.OfInt64(PreviousPriceCents, [.. events.Select(static e => e.PreviousPrice.Cents)]),
                FactColumn.OfInt64(VolumeRemain, [.. events.Select(static e => e.VolumeRemain)]),
                FactColumn.OfInt64(FilledVolume, [.. events.Select(static e => e.FilledVolume)]),
                FactColumn.OfInt64(IssuedUnix, [.. events.Select(static e => e.IssuedUnix)]),
                FactColumn.OfInt64(DurationDays, [.. events.Select(static e => (long)e.DurationDays)]),
                FactColumn.OfInt64(IsNpc, [.. events.Select(static e => e.IsNpc ? 1L : 0L)]),
                FactColumn.OfInt64(UndersampledStep, [.. events.Select(static e => e.UndersampledStep ? 1L : 0L)]),
            ]);
    }

    /// <summary>Набор, в который едет событие: базовая линия отдельно от рыночных событий.</summary>
    public static FactSet SetFor(OrderEventKind kind) =>
        kind == OrderEventKind.Baseline ? FactSet.OrderBaselines : FactSet.OrderEvents;
}
