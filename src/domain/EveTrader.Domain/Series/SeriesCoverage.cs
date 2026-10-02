using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Какие записи покрытия ручаются за окно ряда и за какой отрезок времени.
///
/// Наблюдение стакана — снимок, а не непрерывная запись: интервал сбора у него в секунды,
/// а следующий снимок приходит через объявленный шаг. Отсутствие событий между двумя
/// снимками при этом — факт о рынке: дифф соседних снимков видит всё, что изменилось за
/// шаг. Поэтому снимок ручается за свой шаг назад — от предыдущего снимка до себя, — и
/// окно, по которому прошла непрерывная цепочка снимков, наблюдалось целиком. Цепочка,
/// разорванная дольше шага, оставляет в окне ненаблюдавшийся интервал, и признак за такое
/// окно не порождается.
///
/// Базовая линия ручается только за себя: до неё сравнивать было не с чем, и событий
/// за её шаг назад никто не искал.
///
/// Дневная история за окно ряда не ручается вовсе. Её запись накрывает сутки региона,
/// но говорит о дневном агрегате, а не о стакане внутри суток: признак, допущенный по
/// ней, считался бы по часам, когда стакан никто не снимал. Отличает её объявленный шаг
/// — сутки против минут у снимков стакана.
/// </summary>
public static class SeriesCoverage
{
    /// <summary>Шаг, начиная с которого запись говорит об агрегате, а не о снимке стакана.</summary>
    public static TimeSpan AggregateStep { get; } = TimeSpan.FromDays(1);

    /// <summary>Записи, ручающиеся за окна рядов, с отрезками, за которые они ручаются.</summary>
    public static IReadOnlyList<CoverageEntry> Of(
        IEnumerable<CoverageEntry> entries,
        IReadOnlySet<ObservationId> baselines)
    {
        return
        [
            .. entries
                .Where(static entry => entry.IsObservation && entry.ObservationStep < AggregateStep)
                .Select(entry => baselines.Contains(entry.Observation) || entry.ObservationStep <= TimeSpan.Zero
                    ? entry
                    : entry with { Collected = Vouched(entry) }),
        ];
    }

    /// <summary>Отрезок, за который снимок ручается: его шаг назад плюс сам интервал сбора.</summary>
    public static TimeRange Vouched(CoverageEntry entry)
    {
        DateTimeOffset from = entry.Collected.To - entry.ObservationStep;

        return TimeRange.Between(from < entry.Collected.From ? from : entry.Collected.From, entry.Collected.To);
    }
}
