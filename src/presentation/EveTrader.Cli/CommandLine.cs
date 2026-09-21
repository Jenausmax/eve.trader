using System.Globalization;

namespace EveTrader.Cli;

/// <summary>
/// Разбор аргументов оператора.
///
/// Свой разбор, а не библиотека: набор параметров узкий и общий почти на все команды,
/// и зависимость ради десятка ключей окупалась бы только при подкомандах со своими
/// наборами — которых здесь нет.
/// </summary>
/// <param name="Command">Команда.</param>
/// <param name="Lake">Корень озера.</param>
/// <param name="From">Начало интервала.</param>
/// <param name="To">Конец интервала.</param>
/// <param name="KnownSince">Брать только узнанное источником позже этого момента.</param>
/// <param name="StaticData">Версия статических данных.</param>
/// <param name="Regions">Регионы; пусто — все.</param>
/// <param name="Cycles">Сколько циклов сбора выполнить; ноль — без предела.</param>
/// <param name="Set">Набор фактов для осмотра.</param>
/// <param name="Limit">Сколько строк показать.</param>
/// <param name="RawPages">Корень окна сырых страниц.</param>
/// <param name="Scratch">Куда писать перестроенное при приёмке и реплее.</param>
public sealed record CommandLine(
    string Command,
    string Lake,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? KnownSince,
    string StaticData,
    IReadOnlyList<int> Regions,
    int Cycles,
    string Set,
    int Limit,
    string? RawPages,
    string? Scratch)
{
    public const string Usage = """
        EveTrader.Cli <команда> [параметры]

        Команды:
          collect           живой сбор с ESI; по умолчанию один цикл
          import-history    импорт дневной истории из архива EVE Ref
          import-orderbook  конвертация архивных снимков стакана в факты
          replay            реплей собственного озера в отдельное озеро
          accept            приёмка: перестроить признаки из сырья и сверить
          coverage          отчёт о покрытии за интервал
          materialization   отчёт о материализации
          facts             осмотр фактов набора
          resource-usage    расход ресурсов за интервал
          history-stats     замеры по импортированной дневной истории

        Параметры:
          --lake <путь>         корень озера (обязательно)
          --from <дата>         начало интервала; YYYY-MM-DD или YYYY-MM-DDTHH:MM
          --to <дата>           конец интервала; YYYY-MM-DD или YYYY-MM-DDTHH:MM
          --known-since <дата>  брать только узнанное источником позже этого момента
          --sde <версия>        версия статических данных
          --regions <id,...>    регионы; без него — все
          --cycles <n>          сколько циклов сбора выполнить; 0 — без предела
          --set <имя>           набор фактов: order-events, book-features, ...
          --limit <n>           сколько строк показать (по умолчанию 20)
          --raw-pages <путь>    корень окна сырых страниц
          --scratch <путь>      куда писать перестроенное (по умолчанию рядом с озером)
        """;

    public static CommandLine? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 1; index < args.Length - 1; index += 2)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                values[args[index][2..]] = args[index + 1];
            }
        }

        return !values.TryGetValue("lake", out var lake)
            ? null
            : new CommandLine(
            args[0],
            lake,
            Date(values, "from") ?? new DateTimeOffset(2003, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Date(values, "to") ?? new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Date(values, "known-since"),
            values.GetValueOrDefault("sde", "unknown"),
            RegionsOf(values),
            Number(values, "cycles", 1),
            values.GetValueOrDefault("set", "order-events"),
            Number(values, "limit", 20),
            values.GetValueOrDefault("raw-pages"),
            values.GetValueOrDefault("scratch"));
    }

    /// <summary>
    /// Регионы через запятую. Пустой список — все, какие даёт источник; для архива
    /// стакана это миллион шестьсот тысяч ордеров на снимок, поэтому сужение названо
    /// отдельным параметром, а не выводится из чего-то.
    /// </summary>
    public static IReadOnlyList<int> RegionsOf(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.TryGetValue("regions", out var text)
            ? [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => int.Parse(part, CultureInfo.InvariantCulture))]
            : [];
    }

    public static int Number(IReadOnlyDictionary<string, string> values, string name, int fallback)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.TryGetValue(name, out var text)
            ? int.Parse(text, CultureInfo.InvariantCulture)
            : fallback;
    }

    /// <summary>
    /// Граница интервала. Сутками для дневной истории и с точностью до минуты для
    /// стакана: снимков там сорок восемь в сутки, и «взять два часа» — обычная просьба,
    /// а не экзотика.
    /// </summary>
    public static DateTimeOffset? Date(IReadOnlyDictionary<string, string> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);

        return !values.TryGetValue(name, out var text)
            ? null
            : DateTime.TryParseExact(
            text,
            ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : throw new FormatException($"Не разобрана дата '{text}' у параметра --{name}");
    }
}
