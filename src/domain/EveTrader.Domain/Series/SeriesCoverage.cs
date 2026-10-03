using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Какие записи покрытия ручаются за окно ряда и за какой отрезок времени.
///
/// Наблюдение стакана — снимок, а не непрерывная запись: интервал сбора у него в секунды,
/// а следующий снимок приходит через объявленный шаг. Отсутствие событий между двумя
/// снимками при этом — факт о рынке: дифф соседних снимков видит всё, что изменилось
/// между ними. Поэтому снимок ручается за отрезок от предыдущего снимка цепочки до себя,
/// и окно, по которому прошла непрерывная цепочка снимков, наблюдалось целиком.
///
/// Шаг — объявленный, а не точный: источник публикует снимки с плавающей секундой, и
/// соседние снимки расходятся на минуту-другую больше шага. Цепочка поэтому считается
/// непрерывной, пока соседи не дальше полутора шагов, — тот же допуск, которым приёмка
/// отличает дрожь источника от разрыва. Дальше — разрыв: снимок ручается только за свой
/// шаг назад, и между ним и предыдущим остаётся ненаблюдавшийся интервал. Признак за
/// окно с таким интервалом не порождается.
///
/// Базовая линия ручается только за себя: до неё сравнивать было не с чем, и событий
/// до неё никто не искал.
///
/// Дневная история за окно ряда не ручается вовсе. Её запись накрывает сутки региона,
/// но говорит о дневном агрегате, а не о стакане внутри суток: признак, допущенный по
/// ней, считался бы по часам, когда стакан никто не снимал. Отличает её объявленный шаг
/// — сутки против минут у снимков стакана.
/// </summary>
public static class SeriesCoverage
{
    /// <summary>Во сколько шагов укладывается дрожь источника: дальше — уже разрыв цепочки.</summary>
    public const double StepTolerance = 1.5;

    /// <summary>Шаг, начиная с которого запись говорит об агрегате, а не о снимке стакана.</summary>
    public static TimeSpan AggregateStep { get; } = TimeSpan.FromDays(1);

    /// <summary>Записи о снимках стакана — те, что вообще могут ручаться за окно ряда.</summary>
    public static IReadOnlyList<CoverageEntry> Of(IEnumerable<CoverageEntry> entries) =>
    [
        .. entries.Where(static entry => entry.IsObservation
            && entry.ObservationStep > TimeSpan.Zero
            && entry.ObservationStep < AggregateStep),
    ];

    /// <summary>
    /// Цепочка снимков: каждый снимок — с отрезком, за который он ручается. Записи одного
    /// региона; отказы цепочку не продолжают — за отрезок они не ручаются.
    /// </summary>
    public static IReadOnlyList<CoverageEntry> Chain(
        IEnumerable<CoverageEntry> observations,
        IReadOnlySet<ObservationId> baselines)
    {
        var chain = new List<CoverageEntry>();
        CoverageEntry? previous = null;

        foreach (CoverageEntry entry in observations
            .OrderBy(static entry => entry.Collected.To)
            .ThenBy(static entry => entry.Observation.Value, StringComparer.Ordinal))
        {
            if (!entry.Covers)
            {
                chain.Add(entry);
                continue;
            }

            DateTimeOffset from = baselines.Contains(entry.Observation)
                ? entry.Collected.From
                : previous is { } prior && entry.Collected.To - prior.Collected.To <= entry.ObservationStep * StepTolerance
                    ? prior.Collected.To
                    : entry.Collected.To - entry.ObservationStep;

            chain.Add(entry with
            {
                Collected = TimeRange.Between(from < entry.Collected.From ? from : entry.Collected.From, entry.Collected.To),
            });

            previous = entry;
        }

        return chain;
    }

    /// <summary>
    /// Цепочка, известная на момент, — с хвостом от последнего снимка до этого момента.
    ///
    /// Хвост — не пробел. Следующий снимок ожидается через шаг, и пока он не просрочен,
    /// отрезок после последнего снимка не «не наблюдался», а ещё не наблюдён: снимок не
    /// пропущен, он просто не наступил. Пропуском становится снимок, который должен был
    /// прийти и не пришёл, — тогда хвост короче отрезка до момента, и окно с ним
    /// отклоняется.
    /// </summary>
    public static IReadOnlyList<CoverageEntry> KnownAt(
        IReadOnlyList<CoverageEntry> observations,
        IReadOnlySet<ObservationId> baselines,
        DateTimeOffset instant)
    {
        List<CoverageEntry> known =
            [.. Chain(observations.Where(entry => entry.KnownAt <= instant), baselines)];

        if (known.Where(static entry => entry.Covers).MaxBy(static entry => entry.Collected.To) is not { } last
            || last.Collected.To >= instant)
        {
            return known;
        }

        DateTimeOffset due = last.Collected.To + (last.ObservationStep * StepTolerance);

        known.Add(last with { Collected = TimeRange.Between(last.Collected.To, due < instant ? due : instant) });

        return known;
    }
}
