namespace EveTrader.Application.Workers;

/// <summary>Настройки обслуживания озера.</summary>
public sealed class CompactionOptions
{
    public const string SectionName = "Compaction";

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Глубина окна сырых страниц. Сорок восемь часов — ровно столько, сколько нужно,
    /// чтобы разобрать дефект парсера, замеченный на следующие сутки. Дольше хранить
    /// незачем: страницы полностью выводимы из чекпойнтов и событий.
    /// </summary>
    public TimeSpan RawPageWindow { get; init; } = TimeSpan.FromHours(48);
}
