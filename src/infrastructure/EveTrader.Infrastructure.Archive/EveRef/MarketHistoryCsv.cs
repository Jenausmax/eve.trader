using System.Globalization;
using EveTrader.Domain.History;

namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Разбор CSV дневной истории EVE Ref.
///
/// Колонка <c>http_last_modified</c> — это когда источник узнал строку от ESI, то есть
/// готовое время получения. Она же делает досинхронизацию видимой: строка за январь
/// может нести получение в августе, и именно поэтому время получения живёт на строке,
/// а не на файле.
///
/// Формат менялся за двадцать три года, и разбор идёт по именам колонок, а не по их
/// местам: в файлах 2019 года <c>highest</c> и <c>lowest</c> стоят в обратном порядке,
/// и позиционный разбор молча поменял бы максимум с минимумом. До 2022 года колонки
/// времени получения нет вовсе — тогда его источником служит время изменения файла.
/// </summary>
public static class MarketHistoryCsv
{
    public const string Average = "average";
    public const string Date = "date";
    public const string Highest = "highest";
    public const string Lowest = "lowest";
    public const string OrderCount = "order_count";
    public const string Volume = "volume";
    public const string HttpLastModified = "http_last_modified";
    public const string RegionId = "region_id";
    public const string TypeId = "type_id";

    /// <summary>
    /// Читает строки потоком. Файл распакованных суток — десятки тысяч строк, и держать
    /// его целиком в памяти незачем.
    /// </summary>
    /// <param name="reader">Распакованный CSV.</param>
    /// <param name="knownAtFallback">
    /// Время получения для поколений формата без колонки <c>http_last_modified</c>:
    /// время изменения файла у источника.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async IAsyncEnumerable<MarketHistoryRow> ReadAsync(
        TextReader reader,
        DateTimeOffset knownAtFallback,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (header is null)
        {
            yield break;
        }

        Dictionary<string, int> columns = Columns(header);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            MarketHistoryRow row;

            try
            {
                row = Parse(line.Split(','), columns, knownAtFallback);
            }
            catch (Exception failure) when (failure is FormatException or IndexOutOfRangeException or OverflowException)
            {
                throw new ArchiveFormatException($"Строка не разобрана: {line}", failure);
            }

            yield return row;
        }
    }

    public static Dictionary<string, int> Columns(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = header.Split(',');

        for (var index = 0; index < names.Length; index++)
        {
            columns[names[index].Trim()] = index;
        }

        // http_last_modified обязательным не объявлен: в поколениях до 2022 года его нет.
        var missing = new[] { Average, Date, Highest, Lowest, OrderCount, Volume, RegionId, TypeId }
            .Where(required => !columns.ContainsKey(required))
            .ToList();

        return missing.Count == 0
            ? columns
            : throw new ArchiveFormatException(
                $"В заголовке CSV нет колонок {string.Join(", ", missing.Select(name => $"'{name}'"))}: {header}");
    }

    /// <summary>
    /// Время получения строки.
    ///
    /// Колонка бывает отсутствующей (поколения до 2022 года) и присутствующей, но
    /// пустой: в файлах середины 2020 года таких строк сотни на сутки. Оба случая
    /// означают одно — источник не сказал, когда узнал строку, — и оба сводятся к
    /// времени изменения файла как честной верхней оценке.
    /// </summary>
    public static DateTimeOffset KnownAt(
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
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public static MarketHistoryRow Parse(
        string[] fields,
        IReadOnlyDictionary<string, int> columns,
        DateTimeOffset knownAtFallback)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(columns);

        return new MarketHistoryRow(
            Domain.Facts.RegionId.From(int.Parse(fields[columns[RegionId]], CultureInfo.InvariantCulture)),
            int.Parse(fields[columns[TypeId]], CultureInfo.InvariantCulture),
            DateOnly.ParseExact(fields[columns[Date]], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal.Parse(fields[columns[Average]], CultureInfo.InvariantCulture),
            decimal.Parse(fields[columns[Highest]], CultureInfo.InvariantCulture),
            decimal.Parse(fields[columns[Lowest]], CultureInfo.InvariantCulture),
            long.Parse(fields[columns[OrderCount]], CultureInfo.InvariantCulture),
            long.Parse(fields[columns[Volume]], CultureInfo.InvariantCulture),
            KnownAt(fields, columns, knownAtFallback));
    }
}
