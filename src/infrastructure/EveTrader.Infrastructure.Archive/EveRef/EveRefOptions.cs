namespace EveTrader.Infrastructure.Archive.EveRef;

/// <summary>
/// Настройки источника EVE Ref. Параллелизм и представление себя — не удобство, а
/// условие: лицензия архива не открытая, и массовое скачивание разрешено автором под
/// условие вежливости.
/// </summary>
public sealed class EveRefOptions
{
    public const string SectionName = "EveRef";

    /// <summary>Корень набора дневной истории.</summary>
    public Uri BaseAddress { get; init; } = new("https://data.everef.net/market-history/");

    /// <summary>
    /// Сколько файлов тянуть одновременно. Два — объявленный предел вежливости;
    /// поднимать его без спроса у автора источника нельзя.
    /// </summary>
    public int MaxParallelDownloads { get; init; } = 2;

    /// <summary>
    /// Сколько раз повторить сутки при обрыве загрузки. На восьми тысячах файлов обрыв
    /// не исключение, а норма, и ронять из-за него весь прогон нельзя.
    /// </summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>Пауза перед повтором; удваивается с каждой попыткой.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Чем система представляется источнику. Третьи стороны в EVE обязаны называть себя
    /// и оставлять контакт, иначе источник вправе не отвечать.
    /// </summary>
    public string UserAgent { get; init; } = "eve.trader/0.1 (+https://github.com/Jenausmax/eve.trader)";
}
