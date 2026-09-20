using EveTrader.Domain.Book;

namespace EveTrader.Application.Workers;

/// <summary>Настройки диспетчера наблюдения.</summary>
public sealed class ObservationOptions
{
    public const string SectionName = "Observation";

    /// <summary>Пауза, когда созревших регионов нет.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Пауза после отказа цикла.</summary>
    public TimeSpan ErrorDelay { get; init; } = TimeSpan.FromSeconds(60);

    public DiffOptions Diff { get; init; } = DiffOptions.Default;

    public FeatureOptions Features { get; init; } = FeatureOptions.Default;

    public string StaticData { get; init; } = "unknown";
}
