using EveTrader.Domain.Facts;

namespace EveTrader.Application.Book;

/// <summary>
/// Выбор точки продолжения после обрыва.
///
/// Основание — журнал покрытия, а не файлы и не отдельное состояние прогона. Отдельное
/// состояние пришлось бы записывать атомарно вместе с фактами, иначе оно расходилось бы
/// с ними ровно на обрыве, то есть ровно тогда, когда нужно.
/// </summary>
internal static class OrderBookResume
{
    /// <summary>Разделитель, которым имя снимка отделено от региона в имени наблюдения.</summary>
    public const string RegionMarker = "-region-";

    /// <summary>
    /// Индекс снимка, с которого продолжать прогон.
    ///
    /// На один снимок раньше последнего покрытого, а не сразу за ним. Обрыв застаёт
    /// прогон посреди снимка: часть регионов записана, часть нет. Незаписанным нужен
    /// предшественник, иначе свёртка объявит их первым наблюдением и выдаст базовую
    /// линию там, где были события. Лишний снимок стоит одной загрузки, а записанные
    /// регионы на нём подтверждены и второй раз не пишутся.
    /// </summary>
    public static int IndexOf(
        IReadOnlyList<DateTimeOffset> published,
        Func<DateTimeOffset, string> keyOf,
        IReadOnlySet<ObservationId> confirmed)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(confirmed);

        IReadOnlySet<string> covered = KeysOf(confirmed);
        var last = -1;

        for (var index = 0; index < published.Count; index++)
        {
            if (covered.Contains(keyOf(published[index])))
            {
                last = index;
            }
        }

        return Math.Max(0, last - 1);
    }

    /// <summary>
    /// Имена снимков, по которым есть хоть одна подтверждённая запись. Имя наблюдения
    /// несёт регион, а покрытыми надо считать снимки — отсюда усечение по разделителю.
    /// </summary>
    public static IReadOnlySet<string> KeysOf(IReadOnlySet<ObservationId> confirmed)
    {
        ArgumentNullException.ThrowIfNull(confirmed);

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (ObservationId observation in confirmed)
        {
            var at = observation.Value.LastIndexOf(RegionMarker, StringComparison.Ordinal);

            if (at > 0)
            {
                _ = keys.Add(observation.Value[..at]);
            }
        }

        return keys;
    }
}
