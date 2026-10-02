using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Признаки, выводимые из признаков стакана — то есть из последовательности снимков, а
/// не из отдельных событий.
///
/// Удержание лучшей цены по событиям не считается: событие говорит, что конкретный ордер
/// переставился, но не говорит, была ли его цена лучшей. Лучшая цена — свойство стакана
/// целиком, и живёт оно в признаках наблюдения. Глубина конкуренции — тоже: число
/// ордеров у лучшей цены есть свойство снимка, а не события.
/// </summary>
public static class BookFeatureSeries
{
    /// <param name="definition">Определение ряда.</param>
    /// <param name="region">Регион.</param>
    /// <param name="window">Окно с приговором о покрытии.</param>
    /// <param name="features">Признаки стакана.</param>
    /// <param name="thresholds">
    /// Пороги, с которыми признаки посчитаны: число ордеров внутри порога лежит в
    /// признаках списком по порогам, и полосу ряда надо найти среди них.
    /// </param>
    public static IReadOnlyList<SeriesPoint> Build(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IEnumerable<BookFeatures> features,
        IReadOnlyList<int> thresholds)
    {
        if (!window.IsAdmitted)
        {
            return [];
        }

        List<BookFeatures> inWindow = [.. features.Where(snapshot => window.Range.Contains(snapshot.ObservedAt))];

        return definition.Kind switch
        {
            SeriesKind.BestPriceHold => BestPriceHold(definition, region, window, inWindow),
            SeriesKind.CompetitorDepth => CompetitorDepth(definition, region, window, inWindow, thresholds),
            SeriesKind.ObservedTurnover or SeriesKind.RelistPressure or SeriesKind.FilledDisappearanceShare =>
                throw new ArgumentOutOfRangeException(
                    nameof(definition), definition.Kind, "Вид признака выводится не из признаков стакана"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(definition), definition.Kind, "Неизвестный вид признака"),
        };
    }

    /// <summary>
    /// Средняя длительность удержания лучшей цены, в секундах.
    ///
    /// Считаются только **завершившиеся** удержания — те, что начались и кончились
    /// внутри окна. Пара, у которой лучшая цена за окно не менялась, точки не даёт
    /// вовсе: удержание, которое ещё длится, наблюдённой длительности не имеет, а
    /// подставлять вместо неё длину окна значило бы смешать измеренное с усечённым.
    ///
    /// Устойчивость такой пары видна по другому признаку — у неё нулевое давление
    /// перестановок.
    /// </summary>
    public static IReadOnlyList<SeriesPoint> BestPriceHold(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IReadOnlyList<BookFeatures> features)
    {
        var holds = new Dictionary<SeriesKey, SeriesTally>();

        foreach (IGrouping<BookPair, BookFeatures> pair in features
            .GroupBy(static snapshot => new BookPair(snapshot.TypeId, snapshot.LocationId)))
        {
            List<BookFeatures> ordered = [.. pair.OrderBy(static snapshot => snapshot.ObservedAt)];

            foreach (SeriesSide side in (SeriesSide[])[SeriesSide.Buy, SeriesSide.Sell])
            {
                foreach (PriceHold hold in CompletedRuns(ordered, side))
                {
                    var key = new SeriesKey(pair.Key.TypeId, pair.Key.LocationId, side);
                    holds[key] = holds.GetValueOrDefault(key).Add(hold.Held.Ticks);
                }
            }
        }

        return EventSeries.Points(
            definition,
            region,
            window,
            holds.ToDictionary(static pair => pair.Key, static pair => pair.Value.Mean / TimeSpan.TicksPerSecond));
    }

    /// <summary>
    /// Глубина конкуренции: сколько ордеров стоит в полосе от лучшей цены, в среднем по
    /// снимкам окна.
    ///
    /// Полоса — та же величина, что порог признаков наблюдения, и считается она там же,
    /// где объём: выводить её задним числом неоткуда, по записанным признакам состав
    /// стакана не восстановим.
    ///
    /// Снимок, где стороны нет, в среднее не входит: отсутствие стороны — не ноль
    /// конкурентов, а отсутствие цены, от которой полосу отмерять. Снимок, записанный до
    /// появления счётчиков в признаках, не входит тоже — величины в нём нет. Пара без
    /// единого годного снимка точки не даёт.
    /// </summary>
    public static IReadOnlyList<SeriesPoint> CompetitorDepth(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IReadOnlyList<BookFeatures> features,
        IReadOnlyList<int> thresholds)
    {
        var band = BandIndex(definition, thresholds);
        var depths = new Dictionary<SeriesKey, SeriesTally>();

        foreach (BookFeatures snapshot in features)
        {
            if (snapshot.BestBid is not null && band < snapshot.BuyOrdersWithin.Count)
            {
                var key = new SeriesKey(snapshot.TypeId, snapshot.LocationId, SeriesSide.Buy);
                depths[key] = depths.GetValueOrDefault(key).Add(snapshot.BuyOrdersWithin[band]);
            }

            if (snapshot.BestAsk is not null && band < snapshot.SellOrdersWithin.Count)
            {
                var key = new SeriesKey(snapshot.TypeId, snapshot.LocationId, SeriesSide.Sell);
                depths[key] = depths.GetValueOrDefault(key).Add(snapshot.SellOrdersWithin[band]);
            }
        }

        return EventSeries.Points(
            definition,
            region,
            window,
            depths.ToDictionary(static pair => pair.Key, static pair => pair.Value.Mean));
    }

    /// <summary>
    /// Место полосы ряда среди порогов признаков. Полосы, которой среди порогов нет, ряд
    /// не получит ни при каком снимке — это ошибка настройки, и молчать о ней нельзя.
    /// </summary>
    public static int BandIndex(SeriesDefinition definition, IReadOnlyList<int> thresholds)
    {
        for (var index = 0; index < thresholds.Count; index++)
        {
            if (thresholds[index] == definition.BandBasisPoints)
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(definition),
            definition.BandBasisPoints,
            "Полоса ряда не входит в пороги признаков наблюдения: число ордеров внутри неё не записывалось");
    }

    /// <summary>
    /// Завершившиеся удержания лучшей цены на стороне: длительность и цена.
    ///
    /// Снимок без этой стороны рвёт удержание: отсутствие стороны — не цена, и считать
    /// его продолжением прежней было бы выдумкой.
    /// </summary>
    public static IEnumerable<PriceHold> CompletedRuns(
        IReadOnlyList<BookFeatures> ordered,
        SeriesSide side)
    {
        IskPrice? current = null;
        DateTimeOffset startedAt = default;

        foreach (BookFeatures snapshot in ordered)
        {
            IskPrice? price = side is SeriesSide.Buy ? snapshot.BestBid : snapshot.BestAsk;

            if (current is { } held && price == held)
            {
                continue;
            }

            if (current is { } ending)
            {
                yield return new PriceHold(snapshot.ObservedAt - startedAt, ending);
            }

            current = price;
            startedAt = snapshot.ObservedAt;
        }
    }
}
