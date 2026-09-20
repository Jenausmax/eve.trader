namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Настройки источника EVE Ref. Параллелизм и представление себя — не удобство, а
/// условие: лицензия архива не открытая, и массовое скачивание разрешено автором под
/// условие вежливости.
///
/// Настройки общие на дневную историю и на снимки стакана, потому что хост один. Предел
/// вежливости объявлен источнику, а не набору: два набора с отдельными пределами дали бы
/// вчетверо больше запросов при тех же «двух потоках» на бумаге.
/// </summary>
public sealed class EveRefOptions
{
    public const string SectionName = "EveRef";

    /// <summary>Корень набора дневной истории.</summary>
    public Uri HistoryBaseAddress { get; init; } = new("https://data.everef.net/market-history/");

    /// <summary>
    /// Корень архива снимков стакана. Набор разложен по суткам:
    /// <c>&lt;YYYY&gt;/&lt;YYYY-MM-DD&gt;/market-orders-&lt;YYYY-MM-DD_HH-mm-ss&gt;.v3.csv.bz2</c>.
    /// </summary>
    public Uri OrdersBaseAddress { get; init; } = new("https://data.everef.net/market-orders/history/");

    /// <summary>
    /// Сколько файлов тянуть одновременно. Два — объявленный предел вежливости;
    /// поднимать его без спроса у автора источника нельзя.
    /// </summary>
    public int MaxParallelDownloads { get; init; } = 2;

    /// <summary>
    /// Сколько раз повторить файл при обрыве загрузки. На тысячах файлов обрыв не
    /// исключение, а норма, и ронять из-за него весь прогон нельзя.
    /// </summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>Пауза перед повтором; удваивается с каждой попыткой.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Шаг публикации снимков стакана. Источник снимает рынок дважды в час, на 15-й и
    /// 45-й минуте, — и это свойство данных, а не наша настройка опроса: объявлять его
    /// надо потребителю, а не источнику.
    /// </summary>
    public TimeSpan OrdersStep { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Чем система представляется источнику. Третьи стороны в EVE обязаны называть себя
    /// и оставлять контакт, иначе источник вправе не отвечать.
    /// </summary>
    public string UserAgent { get; init; } = "eve.trader/0.1 (+https://github.com/Jenausmax/eve.trader)";
}
