using System.Globalization;
using EveTrader.Application.Facts;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Acceptance;

/// <summary>
/// Приведение строк и записей покрытия к сравнимому виду.
///
/// Вынесено отдельным типом, а не приватными методами приёмки: приватные методы в
/// продакшн-коде запрещены, а сравнение строк — самостоятельная забота, которую
/// проверяют отдельно от самого прогона.
/// </summary>
public static class FactShapes
{
    /// <summary>
    /// Сравнимая часть строки: колонки набора, время события и регион.
    ///
    /// Время получения в сравнение не входит намеренно. Перестройка происходит позже
    /// исходного прогона, и требовать совпадения момента, когда факт стал известен,
    /// значило бы требовать путешествия во времени. Сверяется, что известно, а не когда
    /// узнали.
    /// </summary>
    public static string Of(FactRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var values = string.Join(
            '|',
            row.Values
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{row.Region.Value}|{row.Envelope.EventTime.Kind}|{row.Envelope.EventTime.From:O}|{row.Envelope.EventTime.To:O}|{values}");
    }

    /// <summary>
    /// Сверка записанного с перестроенным, по моментам наблюдения.
    ///
    /// Сравниваются мультимножества форм строк: два признака с одинаковыми значениями
    /// в одном наблюдении неразличимы и по построению быть не должны, но если такое
    /// случится, разница в их числе всё равно будет видна.
    /// </summary>
    public static IReadOnlyList<FeatureMismatch> Compare(
        IReadOnlyList<FactRow> recorded,
        IReadOnlyList<FactRow> rebuilt)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(rebuilt);

        Dictionary<DateTimeOffset, Dictionary<string, int>> before = Tally(recorded);
        Dictionary<DateTimeOffset, Dictionary<string, int>> after = Tally(rebuilt);

        var moments = new SortedSet<DateTimeOffset>(before.Keys);
        moments.UnionWith(after.Keys);

        var mismatches = new List<FeatureMismatch>();

        foreach (DateTimeOffset moment in moments)
        {
            Dictionary<string, int> expected = before.GetValueOrDefault(moment) ?? [];
            Dictionary<string, int> produced = after.GetValueOrDefault(moment) ?? [];

            var missing = 0;
            var extra = 0;

            foreach ((var shape, var count) in expected)
            {
                missing += Math.Max(0, count - produced.GetValueOrDefault(shape));
            }

            foreach ((var shape, var count) in produced)
            {
                extra += Math.Max(0, count - expected.GetValueOrDefault(shape));
            }

            mismatches.Add(new FeatureMismatch(
                moment, expected.Values.Sum(), produced.Values.Sum(), missing, extra));
        }

        return mismatches;
    }

    /// <summary>Формы строк по моментам наблюдения, с числом повторов.</summary>
    public static Dictionary<DateTimeOffset, Dictionary<string, int>> Tally(IReadOnlyList<FactRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var tally = new Dictionary<DateTimeOffset, Dictionary<string, int>>();

        foreach (FactRow row in rows)
        {
            Dictionary<string, int> moment = tally.TryGetValue(row.Envelope.EventTime.From, out Dictionary<string, int>? found)
                ? found
                : tally[row.Envelope.EventTime.From] = new Dictionary<string, int>(StringComparer.Ordinal);

            var shape = Of(row);
            moment[shape] = moment.GetValueOrDefault(shape) + 1;
        }

        return tally;
    }

    /// <summary>
    /// Наблюдения, чьё окно подтверждения исчезновений закрылось внутри интервала, —
    /// только они участвуют в сверке.
    ///
    /// Исчезновение по норме становится фактом не тогда, когда ордер пропал, а когда
    /// пропажу подтвердили несколько наблюдений подряд. У последнего наблюдения цепочки
    /// подтверждать нечем: следующего наблюдения ещё нет. Записанные признаки взяты из
    /// самого снимка, где ордера уже нет; перестроенные — из состава, подтверждённого
    /// событиями, где он ещё есть. Расхождение здесь — не дефект перестройки, а возраст
    /// вывода, и требовать совпадения значило бы требовать, чтобы конвейер поверил в
    /// пропажу раньше, чем сам себе разрешил.
    ///
    /// Цепочку рвёт не только конец интервала, но и базовая линия: свёртка начинает с
    /// чистого состояния, и кандидаты на исчезновение, накопленные до неё, подтверждения
    /// уже не получат. Поэтому наблюдение перед базовой линией тоже вне сверки.
    /// </summary>
    /// <param name="entries">Записи покрытия за интервал.</param>
    /// <param name="baselines">Наблюдения, записавшие базовую линию.</param>
    /// <param name="disappearanceWindow">Сколько наблюдений подряд подтверждают исчезновение.</param>
    public static HashSet<ObservationId> WindowClosed(
        IReadOnlyList<CoverageEntry> entries,
        IReadOnlySet<ObservationId> baselines,
        int disappearanceWindow)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(baselines);

        var closed = new HashSet<ObservationId>();

        foreach (IGrouping<(RegionId Region, string Source), CoverageEntry> chain in entries
            .Where(static entry => entry.Outcome is CoverageOutcome.Success)
            .GroupBy(static entry => (entry.Region, entry.Source)))
        {
            List<CoverageEntry> ordered = [.. chain.OrderBy(static entry => entry.Collected.From)];

            for (var index = 0; index < ordered.Count; index++)
            {
                var confirmable = true;

                // Подтверждение занимает окно целиком: при окне в два наблюдения хватает
                // следующего, при трёх нужны два следующих подряд.
                for (var ahead = 1; ahead < disappearanceWindow && confirmable; ahead++)
                {
                    confirmable = index + ahead < ordered.Count
                        && !baselines.Contains(ordered[index + ahead].Observation);
                }

                if (confirmable)
                {
                    _ = closed.Add(ordered[index].Observation);
                }
            }
        }

        return closed;
    }

    /// <summary>
    /// Узнано ли наблюдение задним числом: источник рассказал о нём позже, чем прошёл
    /// такт, к которому оно относится.
    ///
    /// Один такт запаса — не придирка: узнать о наблюдении ровно в момент его окончания
    /// нельзя, запись всегда чуть позже. Досинхронизацией это становится, когда
    /// запаздывание перестаёт объясняться самой записью.
    /// </summary>
    public static bool IsBackdated(CoverageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.KnownAt > entry.Collected.To + entry.ObservationStep;
    }

    /// <summary>
    /// Разрывы в цепочке наблюдений региона: соседние наблюдения разошлись больше чем
    /// на полтора объявленных шага.
    ///
    /// Полтора, а не один: источник публикует снимки с плавающей секундой, и точное
    /// равенство шагу не выполняется никогда.
    ///
    /// Цепочка — на регион и источник, а не на один регион: суточная история и снимки
    /// стакана наблюдают один и тот же регион независимо и с разным шагом, и соседями
    /// друг другу не приходятся.
    /// </summary>
    public static IReadOnlyList<ObservationGap> GapsIn(IReadOnlyList<CoverageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var gaps = new List<ObservationGap>();

        foreach (IGrouping<(RegionId Region, string Source), CoverageEntry> chain in entries
            .GroupBy(static entry => (entry.Region, entry.Source)))
        {
            List<CoverageEntry> ordered = [.. chain.OrderBy(static entry => entry.Collected.From)];

            for (var index = 1; index < ordered.Count; index++)
            {
                TimeSpan step = ordered[index].ObservationStep;
                TimeSpan apart = ordered[index].Collected.From - ordered[index - 1].Collected.From;

                if (step <= TimeSpan.Zero || apart <= step * 1.5)
                {
                    continue;
                }

                gaps.Add(new ObservationGap(
                    chain.Key.Region,
                    TimeRange.Between(ordered[index - 1].Collected.To, ordered[index].Collected.From),
                    (int)(apart / step) - 1));
            }
        }

        return gaps;
    }
}
