using System.Globalization;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Перевод признаков стакана в порцию фактов.
///
/// Отсутствие стороны записывается как отсутствие и в хранилище: колонка лучшей цены
/// несёт признак наличия отдельно от значения. Записать ноль было бы проще и неверно —
/// ноль это цена.
/// </summary>
public static class BookFeatureFacts
{
    public const string TypeId = "type_id";
    public const string LocationId = "location_id";
    public const string HasBid = "has_bid";
    public const string HasAsk = "has_ask";
    public const string BestBidCents = "best_bid_cents";
    public const string BestAskCents = "best_ask_cents";
    public const string BuyOrders = "buy_orders";
    public const string SellOrders = "sell_orders";
    public const string Incomplete = "incomplete";

    /// <summary>Имя колонки глубины по порогу: пороги — настройка, поэтому имя строится.</summary>
    public static string DepthColumn(bool isBuy, int basisPoints) =>
        string.Create(CultureInfo.InvariantCulture, $"{(isBuy ? "buy" : "sell")}_depth_{basisPoints}bp");

    public static FactBatch ToBatch(
        RegionId region,
        ObservationId observation,
        DateOnly observedDate,
        IReadOnlyList<BookFeatures> features,
        IReadOnlyList<int> thresholds,
        StaticDataVersion staticData)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(thresholds);

        var envelopes = new List<FactEnvelope>(features.Count);

        foreach (BookFeatures row in features)
        {
            envelopes.Add(new FactEnvelope(
                string.Create(CultureInfo.InvariantCulture, $"features/{region.Value}/{row.TypeId}/{row.LocationId}/{row.ObservedAt:yyyyMMddTHHmmssZ}"),
                EventTime.At(row.ObservedAt),
                row.ObservedAt,
                observation,
                staticData));
        }

        var columns = new List<FactColumn>
        {
            FactColumn.OfInt64(TypeId, [.. features.Select(f => (long)f.TypeId)]),
            FactColumn.OfInt64(LocationId, [.. features.Select(f => f.LocationId)]),
            FactColumn.OfInt64(HasBid, [.. features.Select(f => f.BestBid is null ? 0L : 1L)]),
            FactColumn.OfInt64(HasAsk, [.. features.Select(f => f.BestAsk is null ? 0L : 1L)]),
            FactColumn.OfInt64(BestBidCents, [.. features.Select(f => f.BestBid?.Cents ?? 0L)]),
            FactColumn.OfInt64(BestAskCents, [.. features.Select(f => f.BestAsk?.Cents ?? 0L)]),
            FactColumn.OfInt64(BuyOrders, [.. features.Select(f => (long)f.BuyOrders)]),
            FactColumn.OfInt64(SellOrders, [.. features.Select(f => (long)f.SellOrders)]),
            FactColumn.OfInt64(Incomplete, [.. features.Select(f => f.Incomplete ? 1L : 0L)]),
        };

        for (var index = 0; index < thresholds.Count; index++)
        {
            var slot = index;

            columns.Add(FactColumn.OfInt64(
                DepthColumn(isBuy: true, thresholds[slot]),
                [.. features.Select(f => slot < f.BuyDepth.Count ? f.BuyDepth[slot] : 0L)]));

            columns.Add(FactColumn.OfInt64(
                DepthColumn(isBuy: false, thresholds[slot]),
                [.. features.Select(f => slot < f.SellDepth.Count ? f.SellDepth[slot] : 0L)]));
        }

        return FactBatch.Of(FactSet.BookFeatures, region, observation, observedDate, envelopes, columns);
    }
}
