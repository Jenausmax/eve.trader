namespace EveTrader.Application.Workers;

/// <summary>Настройки досинхронизации дневной истории.</summary>
public sealed class DailyHistoryOptions
{
    public const string SectionName = "DailyHistory";

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// На сколько назад переспрашивать. Источник дополняет ранее опубликованные сутки
    /// задним числом, поэтому смотреть только на вчера недостаточно.
    /// </summary>
    public TimeSpan Lookback { get; init; } = TimeSpan.FromDays(14);

    public string StaticData { get; init; } = "unknown";
}
