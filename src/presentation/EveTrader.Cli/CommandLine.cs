using System.Globalization;

namespace EveTrader.Cli;

/// <summary>Разбор аргументов. Набор узкий намеренно — команды оператора приедут своим чейнджем.</summary>
public sealed record CommandLine(
    string Command,
    string Lake,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? KnownSince,
    string StaticData,
    IReadOnlyList<int> Regions)
{
    public const string Usage = """
        EveTrader.Cli <команда> [параметры]

        Команды:
          import-history    импорт дневной истории из архива EVE Ref
          import-orderbook  конвертация архивных снимков стакана в факты
          history-stats     замеры по импортированной дневной истории

        Параметры:
          --lake <путь>         корень озера (обязательно)
          --from <дата>         начало интервала; YYYY-MM-DD или YYYY-MM-DDTHH:MM
          --to <дата>           конец интервала; YYYY-MM-DD или YYYY-MM-DDTHH:MM
          --known-since <дата>  брать только узнанное источником позже этого момента
          --sde <версия>        версия статических данных
          --regions <id,...>    регионы для import-orderbook; без него — все
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
            RegionsOf(values));
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
