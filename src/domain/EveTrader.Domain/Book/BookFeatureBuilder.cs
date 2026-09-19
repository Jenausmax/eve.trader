using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Book;

/// <summary>
/// Сбор признаков стакана в той же проходке, что и разметка событий.
///
/// Полный стакан региона всё равно лежит в памяти — без него дифф невозможен. Пока по
/// нему идут, признаки получаются попутно. Альтернатива — считать их запросом после
/// записи событий — протаскивала бы миллионы строк через процесс на каждом наблюдении.
/// </summary>
public sealed class BookFeatureBuilder(FeatureOptions options)
{
    private readonly Dictionary<(int TypeId, long LocationId), Side> pairs = [];

    public void Reset() => pairs.Clear();

    public void Add(in OrderSnapshot order)
    {
        if (!options.Includes(order.TypeId, order.LocationId))
        {
            return;
        }

        (int TypeId, long LocationId) key = (order.TypeId, order.LocationId);

        if (!pairs.TryGetValue(key, out Side? side))
        {
            side = new Side();
            pairs[key] = side;
        }

        (order.IsBuy ? side.Buy : side.Sell).Add((order.Price, order.VolumeRemain));
    }

    public IReadOnlyList<BookFeatures> Build(ObservationMeta meta, ObservationId observation)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var features = new List<BookFeatures>(pairs.Count);

        foreach (((var typeId, var locationId), Side side) in pairs.OrderBy(static pair => pair.Key.TypeId).ThenBy(static pair => pair.Key.LocationId))
        {
            // Лучшая покупка — самая дорогая, лучшая продажа — самая дешёвая.
            IskPrice? bestBid = side.Buy.Count == 0 ? null : side.Buy.Max(static order => order.Price);
            IskPrice? bestAsk = side.Sell.Count == 0 ? null : side.Sell.Min(static order => order.Price);

            features.Add(new BookFeatures(
                typeId,
                locationId,
                bestBid,
                bestAsk,
                side.Buy.Count,
                side.Sell.Count,
                BookDepth.Within(side.Buy, bestBid, isBuy: true, options.DepthThresholdsBasisPoints),
                BookDepth.Within(side.Sell, bestAsk, isBuy: false, options.DepthThresholdsBasisPoints),
                meta.Collected.To,
                observation,
                !meta.IsComplete));
        }

        return features;
    }

    /// <summary>Две стороны стакана по паре.</summary>
    private sealed class Side
    {
        public List<(IskPrice Price, long Volume)> Buy { get; } = [];

        public List<(IskPrice Price, long Volume)> Sell { get; } = [];
    }
}
