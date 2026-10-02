namespace EveTrader.Domain.Book;

/// <summary>Доступный объём и число ордеров в пределах порогов отклонения от лучшей цены.</summary>
internal static class BookDepth
{
    /// <summary>
    /// Объём и число ордеров по каждому порогу — в одной проходке по стороне. Для покупки
    /// порог идёт вниз от лучшей цены, для продажи — вверх: «хуже» в обе стороны означает
    /// разное направление.
    ///
    /// Число ордеров внутри порога — не то же, что число ордеров на стороне: у ходового
    /// типа в хабе сотни ордеров в широком диапазоне цен, а конкуренцию создают только
    /// верхние. Считается здесь, рядом с объёмом, потому что по записанным признакам
    /// состав стакана уже не восстановить.
    ///
    /// Нет лучшей цены — нет ни глубины, ни счётчиков: пустые списки, а не нули. Ноль это
    /// объём, и выдавать им отсутствие стороны значит соврать.
    /// </summary>
    public static SideDepth Within(
        List<(IskPrice Price, long Volume)> orders,
        IskPrice? best,
        bool isBuy,
        IReadOnlyList<int> thresholdsBasisPoints)
    {
        if (best is not { } anchor || thresholdsBasisPoints.Count == 0)
        {
            return SideDepth.Absent;
        }

        var volumes = new long[thresholdsBasisPoints.Count];
        var counts = new int[thresholdsBasisPoints.Count];

        for (var index = 0; index < volumes.Length; index++)
        {
            var slack = anchor.Cents * thresholdsBasisPoints[index] / 10_000;
            var limit = isBuy ? anchor.Cents - slack : anchor.Cents + slack;

            long total = 0;
            var within = 0;

            foreach ((IskPrice price, var volume) in orders)
            {
                if (isBuy ? price.Cents >= limit : price.Cents <= limit)
                {
                    total += volume;
                    within++;
                }
            }

            volumes[index] = total;
            counts[index] = within;
        }

        return new SideDepth(volumes, counts);
    }
}
