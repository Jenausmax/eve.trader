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
/// <param name="Values">
/// Все параметры по именам — для команд со своим набором ключей (правило сигнала,
/// бэктест), которым общий набор выше тесен.
/// </param>
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
    string? Scratch,
    IReadOnlyDictionary<string, string> Values)
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
          series            материализация рядов признаков правила за интервал
          signals           сигналы станционной торговли за интервал с обоснованием
          backtest          прогон правила на истории и отчёт о качестве
          backtest-reports  записанные отчёты о прогонах и их сравнение

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
          --features <охват>    охват признаков стакана: rule (пары правила — Jita 4-4),
                                all (все пары), none, station:<id,...>; без него
                                import-orderbook и collect отклоняются

        Параметры правила (series, signals, backtest) — значений по умолчанию у ставок нет:
          --broker <доля>       брокерская комиссия, обе стороны (например 0.015)
          --tax <доля>          налог с продажи
          --relist <доля>       плата за перестановку
          --label <метка>       читаемая часть имени набора параметров (по умолчанию jita)
          --window <часы>       окно рядов признаков (по умолчанию 2)
          --step <минуты>       шаг решений и рядов (по умолчанию 30)
          --min-margin <доля>   минимальная маржа после комиссий (по умолчанию 0.02)
          --min-turnover <шт>   минимальный наблюдённый оборот каждой стороны (по умолчанию 1)
          --band <б.п.>         полоса конкурентов от лучшей цены (по умолчанию 100)
          --max-competitors <n> терпимое число конкурентов в полосе (по умолчанию 10)
          --max-relists <n>     терпимое число перестановок за окно (по умолчанию 20)
          --buy-relists <n>     перестановок, закладываемых в маржу покупки (по умолчанию 1)
          --sell-relists <n>    перестановок, закладываемых в маржу продажи (по умолчанию 1)
          --horizon <часы>      горизонт оценки исхода в бэктесте (по умолчанию 2)
          --max-unknown <доля>  порог доли неизвестных исходов (по умолчанию 0.5)
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
            values.GetValueOrDefault("scratch"),
            values);
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
