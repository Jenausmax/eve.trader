using System.Globalization;
using EveTrader.Application.Book;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Разбор CSV снимка стакана EVE Ref.
///
/// Колонки <c>duration</c>, <c>is_buy_order</c>, <c>issued</c>, <c>location_id</c>,
/// <c>order_id</c>, <c>price</c>, <c>type_id</c>, <c>volume_remain</c> и
/// <c>volume_total</c> источник берёт у ESI дословно; <c>region_id</c> и
/// <c>http_last_modified</c> добавляет сам.
///
/// Разбор идёт по именам колонок, а не по их местам: у дневной истории той же свалки
/// порядок колонок менялся между поколениями формата, и позиционный разбор молча
/// поменял бы значения местами. Колонки <c>system_id</c>, <c>station_id</c> и
/// <c>constellation_id</c> не читаются вовсе — у ордеров в игровых структурах они
/// приходят пустыми, и опираться на них было бы опираться на дырку.
/// </summary>
public static class OrderBookCsv
{
    public const string Duration = "duration";
    public const string HttpLastModified = "http_last_modified";
    public const string IsBuyOrder = "is_buy_order";
    public const string Issued = "issued";
    public const string LocationId = "location_id";
    public const string OrderId = "order_id";
    public const string Price = "price";
    public const string RegionId = "region_id";
    public const string TypeId = "type_id";
    public const string VolumeRemain = "volume_remain";
    public const string VolumeTotal = "volume_total";

    /// <summary>
    /// Читает снимок целиком и раскладывает по регионам.
    ///
    /// Целиком, а не потоком: свёртка требует стакан региона разом, и ордера одного
    /// региона в файле рядом не гарантированы.
    /// </summary>
    /// <param name="reader">Распакованный CSV.</param>
    /// <param name="knownAtFallback">Время сбора для строк без <c>http_last_modified</c>.</param>
    /// <param name="wanted">Нужные регионы; пустой набор — все.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task<IReadOnlyList<RegionBook>> ReadAsync(
        TextReader reader,
        DateTimeOffset knownAtFallback,
        IReadOnlyCollection<RegionId> wanted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(wanted);

        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (header is null)
        {
            return [];
        }

        Dictionary<string, int> columns = Columns(header);
        HashSet<RegionId>? only = wanted.Count == 0 ? null : [.. wanted];

        var orders = new Dictionary<RegionId, List<OrderSnapshot>>();
        var earliest = new Dictionary<RegionId, DateTimeOffset>();
        var latest = new Dictionary<RegionId, DateTimeOffset>();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(',');
            RegionId region;
            OrderSnapshot order;
            DateTimeOffset collectedAt;

            try
            {
                region = Domain.Facts.RegionId.From(
                    int.Parse(fields[columns[RegionId]], CultureInfo.InvariantCulture));

                if (only is not null && !only.Contains(region))
                {
                    continue;
                }

                order = Parse(fields, columns);
                collectedAt = CollectedAt(fields, columns, knownAtFallback);
            }
            catch (Exception failure)
                when (failure is FormatException or IndexOutOfRangeException
                    or OverflowException or ArgumentOutOfRangeException or KeyNotFoundException)
            {
                throw new ArchiveFormatException($"Строка снимка не разобрана: {line}", failure);
            }

            if (!orders.TryGetValue(region, out List<OrderSnapshot>? book))
            {
                book = [];
                orders[region] = book;
                earliest[region] = collectedAt;
                latest[region] = collectedAt;
            }

            book.Add(order);

            if (collectedAt < earliest[region])
            {
                earliest[region] = collectedAt;
            }

            if (collectedAt > latest[region])
            {
                latest[region] = collectedAt;
            }
        }

        return
        [
            .. orders
                .OrderBy(static pair => pair.Key)
                .Select(pair => new RegionBook(
                    pair.Key,
                    [.. pair.Value],
                    Collected(earliest[pair.Key], latest[pair.Key]))),
        ];
    }

    /// <summary>
    /// Границы интервала сбора региона. Полуинтервал требует строгого порядка границ, а
    /// маленький регион источник обходит за один запрос и отмечает одной секундой —
    /// тогда конец берётся секундой позже начала, а не подменяется именем файла.
    /// </summary>
    public static TimeRange Collected(DateTimeOffset earliest, DateTimeOffset latest) =>
        TimeRange.Between(earliest, latest > earliest ? latest : earliest.AddSeconds(1));

    public static Dictionary<string, int> Columns(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = header.Split(',');

        for (var index = 0; index < names.Length; index++)
        {
            columns[names[index].Trim()] = index;
        }

        // http_last_modified обязательным не объявлен: поколения формата до v3 его не несут.
        var missing = new[]
        {
            Duration, IsBuyOrder, Issued, LocationId, OrderId, Price, RegionId, TypeId, VolumeRemain, VolumeTotal,
        }
            .Where(required => !columns.ContainsKey(required))
            .ToList();

        return missing.Count == 0
            ? columns
            : throw new ArchiveFormatException(
                $"В заголовке снимка нет колонок {string.Join(", ", missing.Select(name => $"'{name}'"))}: {header}");
    }

    /// <summary>
    /// Когда источник увидел строку. Колонка бывает отсутствующей и присутствующей, но
    /// пустой; оба случая означают одно — источник не сказал, когда узнал строку, — и
    /// оба сводятся к времени изменения файла как честной верхней оценке.
    /// </summary>
    public static DateTimeOffset CollectedAt(
        string[] fields,
        IReadOnlyDictionary<string, int> columns,
        DateTimeOffset fallback)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(columns);

        if (!columns.TryGetValue(HttpLastModified, out var at) || at >= fields.Length)
        {
            return fallback;
        }

        var value = fields[at];

        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public static OrderSnapshot Parse(string[] fields, IReadOnlyDictionary<string, int> columns)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(columns);

        return new OrderSnapshot(
            long.Parse(fields[columns[OrderId]], CultureInfo.InvariantCulture),
            int.Parse(fields[columns[TypeId]], CultureInfo.InvariantCulture),
            long.Parse(fields[columns[LocationId]], CultureInfo.InvariantCulture),
            bool.Parse(fields[columns[IsBuyOrder]]),
            IskPrice.FromIsk(decimal.Parse(fields[columns[Price]], NumberStyles.Float, CultureInfo.InvariantCulture)),
            long.Parse(fields[columns[VolumeRemain]], CultureInfo.InvariantCulture),
            long.Parse(fields[columns[VolumeTotal]], CultureInfo.InvariantCulture),
            short.Parse(fields[columns[Duration]], CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(
                fields[columns[Issued]],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds());
    }
}
