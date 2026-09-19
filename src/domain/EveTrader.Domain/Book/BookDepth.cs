namespace EveTrader.Domain.Book;

/// <summary>Доступный объём в пределах порогов отклонения от лучшей цены.</summary>
internal static class BookDepth
{
    /// <summary>
    /// Объём по каждому порогу. Для покупки порог идёт вниз от лучшей цены, для
    /// продажи — вверх: «хуже» в обе стороны означает разное направление.
    ///
    /// Нет лучшей цены — нет и глубины: пустой список, а не нули. Ноль это объём, и
    /// выдавать им отсутствие стороны значит соврать.
    /// </summary>
    public static IReadOnlyList<long> Within(
        List<(IskPrice Price, long Volume)> orders,
        IskPrice? best,
        bool isBuy,
        IReadOnlyList<int> thresholdsBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(thresholdsBasisPoints);

        if (best is not { } anchor || thresholdsBasisPoints.Count == 0)
        {
            return [];
        }

        var depths = new long[thresholdsBasisPoints.Count];

        for (var index = 0; index < depths.Length; index++)
        {
            var slack = anchor.Cents * thresholdsBasisPoints[index] / 10_000;
            var limit = isBuy ? anchor.Cents - slack : anchor.Cents + slack;

            long total = 0;

            foreach ((IskPrice price, var volume) in orders)
            {
                if (isBuy ? price.Cents >= limit : price.Cents <= limit)
                {
                    total += volume;
                }
            }

            depths[index] = total;
        }

        return depths;
    }
}
