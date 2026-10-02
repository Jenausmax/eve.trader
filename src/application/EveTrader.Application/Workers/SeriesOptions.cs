using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Workers;

/// <summary>Настройки материализации рядов признаков.</summary>
public sealed class SeriesOptions
{
    public const string SectionName = "Series";

    /// <summary>Как часто материализовать: не чаще шага рядов — новых концов окон раньше не появится.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>На сколько назад досчитать при первом цикле после старта.</summary>
    public TimeSpan Lookback { get; init; } = TimeSpan.FromDays(1);

    /// <summary>Станции правила: из них берутся регионы, по которым считаются ряды.</summary>
    public IReadOnlyList<StationTradingScope> Stations { get; init; } = [StationTradingScope.Jita44];

    /// <summary>Окно рядов, которые читает правило.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromHours(2);

    /// <summary>Шаг рядов — он же шаг решений правила.</summary>
    public TimeSpan Step { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Полоса конкурентов; обязана быть одним из порогов признаков.</summary>
    public int BandBasisPoints { get; init; } = 100;

    /// <summary>Пороги, с которыми записаны признаки стакана.</summary>
    public IReadOnlyList<int> Thresholds { get; init; } = FeatureOptions.DefaultThresholds;

    public string StaticData { get; init; } = "unknown";

    public IReadOnlyList<SeriesDefinition> Definitions =>
        StationTradingSeries.Definitions(Window, Step, BandBasisPoints);

    public IReadOnlyList<RegionId> Regions =>
        [.. Stations.Select(static station => station.Region).Distinct().Order()];
}
