using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Признаки, выводимые из признаков стакана — то есть из последовательности снимков, а
/// не из отдельных событий.
///
/// Удержание лучшей цены по событиям не считается: событие говорит, что конкретный ордер
/// переставился, но не говорит, была ли его цена лучшей. Лучшая цена — свойство стакана
/// целиком, и живёт оно в признаках наблюдения.
/// </summary>
public static class BookFeatureSeries
{
    public static IReadOnlyList<SeriesPoint> Build(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IEnumerable<BookFeatures> features)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(features);

        if (!window.IsAdmitted)
        {
            return [];
        }

        List<BookFeatures> inWindow = [.. features.Where(snapshot => window.Range.Contains(snapshot.ObservedAt))];

        return definition.Kind switch
        {
            SeriesKind.BestPriceHold => BestPriceHold(definition, region, window, inWindow),
            SeriesKind.ObservedTurnover or SeriesKind.RelistPressure or SeriesKind.FilledDisappearanceShare =>
                throw new ArgumentOutOfRangeException(
                    nameof(definition), definition.Kind, "Вид признака выводится не из признаков стакана"),

            // Глубина конкуренции требует числа ордеров внутри полосы вокруг лучшей
            // цены. Признаки наблюдения несут число ордеров на стороне целиком и объём
            // внутри полосы, но не число ордеров внутри неё — величины в озере нет.
            SeriesKind.CompetitorDepth => throw new ArgumentOutOfRangeException(
                nameof(definition),
                definition.Kind,
                "Признаки наблюдения не несут числа ордеров внутри полосы вокруг лучшей цены"),
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
        ArgumentNullException.ThrowIfNull(features);

        var holds = new Dictionary<(int TypeId, long LocationId, SeriesSide Side), (double Seconds, int Runs)>();

        foreach (IGrouping<(int TypeId, long LocationId), BookFeatures> pair in features
            .GroupBy(static snapshot => (snapshot.TypeId, snapshot.LocationId)))
        {
            List<BookFeatures> ordered = [.. pair.OrderBy(static snapshot => snapshot.ObservedAt)];

            foreach (SeriesSide side in (SeriesSide[])[SeriesSide.Buy, SeriesSide.Sell])
            {
                foreach ((var seconds, _) in CompletedRuns(ordered, side))
                {
                    (int, long, SeriesSide) key = (pair.Key.TypeId, pair.Key.LocationId, side);
                    (var total, var runs) = holds.GetValueOrDefault(key);
                    holds[key] = (total + seconds, runs + 1);
                }
            }
        }

        return EventSeries.Points(
            definition,
            region,
            window,
            holds.ToDictionary(static pair => pair.Key, static pair => pair.Value.Seconds / pair.Value.Runs));
    }

    /// <summary>
    /// Завершившиеся удержания лучшей цены на стороне: длительность и цена.
    ///
    /// Снимок без этой стороны рвёт удержание: отсутствие стороны — не цена, и считать
    /// его продолжением прежней было бы выдумкой.
    /// </summary>
    public static IEnumerable<(double Seconds, IskPrice Price)> CompletedRuns(
        IReadOnlyList<BookFeatures> ordered,
        SeriesSide side)
    {
        ArgumentNullException.ThrowIfNull(ordered);

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
                yield return ((snapshot.ObservedAt - startedAt).TotalSeconds, ending);
            }

            current = price;
            startedAt = snapshot.ObservedAt;
        }
    }
}
