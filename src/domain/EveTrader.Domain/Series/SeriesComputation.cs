using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Ряды региона за интервал: на каждом конце окна — приговор покрытию и точки.
///
/// Каждая точка считается так, как её посчитала бы система в момент конца окна: окно
/// <c>[T − w, T)</c> накладывается по времени наблюдения, а узнанное после <c>T</c>
/// отсекается отдельно, по моменту знания. События приходят сюда уже со временем
/// наблюдения, равным моменту знания о них: так их читает приложение, поэтому срез по
/// окну у событий — это и срез по знанию. У признаков стакана два времени расходятся
/// при досинхронизации задним числом, поэтому признак входит в точку, только если
/// известен к <c>T</c>, и из нескольких версий одного снимка берётся последняя из
/// известных к <c>T</c> (<see cref="KnownBookFeatures" />). Записи покрытия, узнанные
/// позже <c>T</c>, в разбор окна тоже не входят, а
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

        KnownBookFeatures[] features =
        [
            .. inputs.Features
                .OrderBy(static known => known.Features.ObservedAt)
                .ThenBy(static known => known.Features.TypeId)
                .ThenBy(static known => known.Features.LocationId)
                .ThenBy(static known => known.Features.Observation.Value, StringComparer.Ordinal)
                .ThenBy(static known => known.KnownAt),
        ];

        IReadOnlyList<CoverageEntry> coverage = SeriesCoverage.Of(
            inputs.Coverage.Where(entry => entry.Region == inputs.Region));

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
                    SeriesCoverage.KnownAt(coverage, inputs.Baselines, end),
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
                        definition, inputs.Region, window, SeriesSlices.KnownWithin(features, range, end), inputs.Thresholds));
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

    /// <summary>
    /// Признаки, наблюдённые в окне и известные к <paramref name="knownBy" />: срез по
    /// времени наблюдения, затем по каждому снимку — последняя версия из известных к
    /// <paramref name="knownBy" />. Порядок выдачи — снова по времени наблюдения.
    /// </summary>
    public static IEnumerable<BookFeatures> KnownWithin(KnownBookFeatures[] features, TimeRange range, DateTimeOffset knownBy)
    {
        var from = Lower(features.Length, index => features[index].Features.ObservedAt, range.From);
        var to = Lower(features.Length, index => features[index].Features.ObservedAt, range.To);

        return Bitemporal.AsOf(new ArraySegment<KnownBookFeatures>(features, from, to - from), knownBy)
            .Select(static known => known.Features)
            .OrderBy(static known => known.ObservedAt)
            .ThenBy(static known => known.TypeId)
            .ThenBy(static known => known.LocationId)
            .ThenBy(static known => known.Observation.Value, StringComparer.Ordinal);
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
