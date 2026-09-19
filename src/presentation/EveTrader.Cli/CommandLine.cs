using System.Globalization;

namespace EveTrader.Cli;

/// <summary>Разбор аргументов. Набор узкий намеренно — команды оператора приедут своим чейнджем.</summary>
public sealed record CommandLine(
    string Command,
    string Lake,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? KnownSince,
    string StaticData)
{
    public const string Usage = """
        EveTrader.Cli <команда> [параметры]

        Команды:
          import-history    импорт дневной истории из архива EVE Ref
          history-stats     замеры по импортированной дневной истории

        Параметры:
          --lake <путь>         корень озера (обязательно)
          --from <YYYY-MM-DD>   начало интервала рыночных дат
          --to <YYYY-MM-DD>     конец интервала рыночных дат
          --known-since <дата>  брать только узнанное источником позже этого момента
          --sde <версия>        версия статических данных
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
            values.GetValueOrDefault("sde", "unknown"));
    }

    public static DateTimeOffset? Date(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var text)
            ? new DateTimeOffset(
                DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero)
            : null;
}
