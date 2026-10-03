using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Признаки, выводимые из событий жизни ордера.
///
/// Источник здесь именно события, а не дневная история, и это главный выбор изменения.
/// Дневной агрегат даёт объём и число сделок, но не различает: продано по моей цене,
/// ордер отменён, ордер истёк. Станционная торговля живёт ровно на этом различии —
/// конкурента, который отменил ордер, и конкурента, которого выкупили, лечат по-разному.
///
/// Окно накладывается по времени наблюдения, а не по времени события. Причина та же,
/// что у восстановления состава в реплее: время события у перестановки — это
/// <c>issued</c>, который вполне попадает внутрь интервала сбора предыдущего снимка, и
/// окно по нему теряло бы события на границе.
/// </summary>
public static class EventSeries
{
    public static IReadOnlyList<SeriesPoint> Build(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IEnumerable<OrderEvent> events)
    {
        if (!window.IsAdmitted)
        {
            return [];
        }

        List<OrderEvent> inWindow = [.. events.Where(moment => window.Range.Contains(moment.ObservedAt))];

        return definition.Kind switch
        {
            SeriesKind.ObservedTurnover => Turnover(definition, region, window, inWindow),
            SeriesKind.RelistPressure => RelistPressure(definition, region, window, inWindow),
            SeriesKind.FilledDisappearanceShare => FilledShare(definition, region, window, inWindow),
            // Ветви перечислены явно, включая невыводимые отсюда. Неявная ветвь тут
            // опасна: автофикс анализатора однажды уже дописал в неё
            // NotImplementedException, и вместо внятного отказа вид признака падал бы
            // с пустым сообщением.
            SeriesKind.BestPriceHold or SeriesKind.CompetitorDepth => throw new ArgumentOutOfRangeException(
                nameof(definition), definition.Kind, "Вид признака выводится не из событий"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(definition), definition.Kind, "Неизвестный вид признака"),
        };
    }

    /// <summary>
    /// Наблюдённый оборот: сумма исполненных объёмов по событиям наблюдённого
    /// исполнения.
    ///
    /// «Наблюдённый» в названии не для красоты. Сделка, после которой ордер исчез между
    /// двумя наблюдениями, видна как исчезновение, а не как продажа, и в эту сумму не
    /// попадает. Величина — нижняя граница настоящего оборота, и размер зазора оценивает
    /// <see cref="SeriesKind.FilledDisappearanceShare" />.
    /// </summary>
    public static IReadOnlyList<SeriesPoint> Turnover(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IReadOnlyList<OrderEvent> events)
    {
        var totals = new Dictionary<SeriesKey, long>();

        foreach (OrderEvent moment in events.Where(static moment => moment.Kind is OrderEventKind.ObservedFill))
        {
            var key = new SeriesKey(moment.TypeId, moment.LocationId, SideOf(moment));
            totals[key] = totals.GetValueOrDefault(key) + moment.FilledVolume;
        }

        return Points(definition, region, window, totals.ToDictionary(static pair => pair.Key, static pair => (double)pair.Value));
    }

    /// <summary>
    /// Давление перестановок: сколько раз за окно цена ордера переставлялась.
    ///
    /// Считаются только перестановки. Пополнение склада NPC — отдельный вид события и
    /// сюда не входит: это не действие конкурента, и принимать его за войну на 0.01 ISK
    /// значило бы видеть конкуренцию там, где её нет.
    /// </summary>
    public static IReadOnlyList<SeriesPoint> RelistPressure(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IReadOnlyList<OrderEvent> events)
    {
        var counts = new Dictionary<SeriesKey, long>();

        foreach (OrderEvent moment in events.Where(static moment => moment.Kind is OrderEventKind.Repriced))
        {
            var key = new SeriesKey(moment.TypeId, moment.LocationId, SideOf(moment));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return Points(definition, region, window, counts.ToDictionary(static pair => pair.Key, static pair => (double)pair.Value));
    }

    /// <summary>
    /// Доля исчезновений, которым предшествовало наблюдённое исполнение.
    ///
    /// Величина отвечает на вопрос «какую часть исчезновений мы вправе считать
    /// продажами», то есть измеряет слепую зону наблюдения. Она — нижняя граница:
    /// исполнения, случившиеся до начала окна, в расчёт не входят, и приписывать им
    /// влияние на исчезновение внутри окна оснований нет.
    ///
    /// Пара без исчезновений точки не даёт вовсе. Ноль здесь означал бы «ни одно
    /// исчезновение не было продажей» — утверждение о рынке, которого никто не делал.
    /// </summary>
    public static IReadOnlyList<SeriesPoint> FilledShare(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IReadOnlyList<OrderEvent> events)
    {
        var filled = new HashSet<long>(events
            .Where(static moment => moment.Kind is OrderEventKind.ObservedFill)
            .Select(static moment => moment.OrderId));

        var gone = new Dictionary<SeriesKey, SeriesTally>();

        foreach (OrderEvent moment in events.Where(static moment => moment.Kind is OrderEventKind.Disappeared))
        {
            var key = new SeriesKey(moment.TypeId, moment.LocationId, SeriesSide.Both);
            gone[key] = gone.GetValueOrDefault(key).Add(filled.Contains(moment.OrderId) ? 1 : 0);
        }

        return Points(definition, region, window, gone.ToDictionary(static pair => pair.Key, static pair => pair.Value.Share));
    }

    public static SeriesSide SideOf(in OrderEvent moment) => moment.IsBuy ? SeriesSide.Buy : SeriesSide.Sell;

    /// <summary>
    /// Точки в детерминированном порядке.
    ///
    /// Сортировка не косметика: порядок обхода словаря зависит от истории вставок, и без
    /// явного упорядочивания два прогона одного интервала дали бы разный порядок строк.
    /// Бэктест на таких данных невоспроизводим, а невоспроизводимый бэктест не
    /// отлаживается.
    /// </summary>
    public static IReadOnlyList<SeriesPoint> Points(
        SeriesDefinition definition,
        RegionId region,
        SeriesWindow window,
        IReadOnlyDictionary<SeriesKey, double> values)
    {
        return
        [
            .. values
                .OrderBy(static pair => pair.Key)
                .Select(pair => new SeriesPoint(
                    definition,
                    region,
                    pair.Key.TypeId,
                    pair.Key.LocationId,
                    pair.Key.Side,
                    window.Range,
                    pair.Value,
                    window.IsIncomplete)),
        ];
    }
}
