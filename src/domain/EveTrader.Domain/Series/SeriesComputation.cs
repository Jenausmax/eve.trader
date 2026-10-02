using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Ряды региона за интервал: на каждом конце окна — приговор покрытию и точки.
///
/// Каждая точка считается так, как её посчитала бы система в момент конца окна: окно
/// накладывается по времени наблюдения, а время наблюдения строк стакана и есть время,
/// когда о них узнали, — значит окно <c>[T − w, T)</c> само отсекает узнанное после
/// <c>T</c>. Записи покрытия, узнанные позже <c>T</c>, в разбор окна тоже не входят, а
/// отрезок от последнего снимка до <c>T</c> считается ещё не наблюдённым, а не пробелом,
/// пока следующий снимок не просрочен (<see cref="SeriesCoverage.KnownAt" />).
/// Отсюда главное свойство для бэктеста: точка, записанная с моментом знания <c>T</c>,
/// не содержит ничего, чего система в <c>T</c> не знала.
///
/// Функция чистая и детерминированная: один вход — один выход побитово, включая порядок.
/// Порядок обхода от хеш-таблиц не зависит — входы упорядочиваются явно, накопители
/// целые, выдача сортируется.
/// </summary>
public static class SeriesComputation
{
    public static SeriesComputed Compute(
        SeriesInputs inputs,
        IReadOnlyList<SeriesDefinition> definitions,
        TimeRange interval)
    {
        OrderEvent[] events =
        [
            .. inputs.Events
                .OrderBy(static moment => moment.ObservedAt)
                .ThenBy(static moment => moment.OrderId)
                .ThenBy(static moment => (int)moment.Kind),
        ];

        BookFeatures[] features =
        [
            .. inputs.Features
                .OrderBy(static snapshot => snapshot.ObservedAt)
                .ThenBy(static snapshot => snapshot.TypeId)
                .ThenBy(static snapshot => snapshot.LocationId)
                .ThenBy(static snapshot => snapshot.Observation.Value, StringComparer.Ordinal),
        ];

        IReadOnlyList<CoverageEntry> coverage = SeriesCoverage.Of(
            inputs.Coverage.Where(entry => entry.Region == inputs.Region), inputs.Baselines);

        var windows = new List<SeriesWindowVerdict>();
        var points = new List<SeriesPoint>();

        foreach (SeriesDefinition definition in definitions
            .Distinct()
            .OrderBy(static definition => definition.Key, StringComparer.Ordinal))
        {
            foreach (DateTimeOffset end in SeriesGrid.Ends(interval, definition.Step))
            {
                var range = TimeRange.Between(end - definition.Window, end);

                CoverageVerdict verdict = CoverageResolver.Resolve(
                    inputs.Region,
                    range,
                    FactSet.OrderEvents,
                    SeriesCoverage.KnownAt(coverage, end),
                    inputs.Materialized,
                    inputs.Upstream);

                var window = SeriesWindow.Of(range, verdict);
                windows.Add(new SeriesWindowVerdict(definition, inputs.Region, window));

                if (!window.IsAdmitted)
                {
                    continue;
                }

                points.AddRange(SeriesKinds.SourcesOf(definition.Kind).Contains(FactSet.OrderEvents)
                    ? EventSeries.Build(definition, inputs.Region, window, SeriesSlices.Within(events, range))
                    : BookFeatureSeries.Build(
                        definition, inputs.Region, window, SeriesSlices.Within(features, range), inputs.Thresholds));
            }
        }

        return new SeriesComputed(windows, points);
    }
}

/// <summary>
/// Срез упорядоченных по времени наблюдения строк по окну — двоичным поиском, а не
/// перебором: окон в прогоне сотни, строк в окне хаба — сотни тысяч.
/// </summary>
file static class SeriesSlices
{
    public static ArraySegment<OrderEvent> Within(OrderEvent[] events, TimeRange range)
    {
        var from = Lower(events.Length, index => events[index].ObservedAt, range.From);
        var to = Lower(events.Length, index => events[index].ObservedAt, range.To);

        return new ArraySegment<OrderEvent>(events, from, to - from);
    }

    public static ArraySegment<BookFeatures> Within(BookFeatures[] features, TimeRange range)
    {
        var from = Lower(features.Length, index => features[index].ObservedAt, range.From);
        var to = Lower(features.Length, index => features[index].ObservedAt, range.To);

        return new ArraySegment<BookFeatures>(features, from, to - from);
    }

    /// <summary>Первая позиция, чьё время не раньше <paramref name="instant" />.</summary>
    public static int Lower(int length, Func<int, DateTimeOffset> at, DateTimeOffset instant)
    {
        var low = 0;
        var high = length;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (at(middle) < instant)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
