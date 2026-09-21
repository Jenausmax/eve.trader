using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Workers;

/// <summary>Настройки диспетчера наблюдения.</summary>
public sealed class ObservationOptions
{
    public const string SectionName = "Observation";

    /// <summary>
    /// Торговые хабы — то, с чего начинается сбор.
    ///
    /// Список настраиваемый, а не зашитый: хаб перестаёт быть хабом по решению игроков,
    /// а не по нашему коду. Значения по умолчанию — пять исторических хабов: Forge
    /// (Jita), Domain (Amarr), Sinq Laison (Dodixie), Heimatar (Rens), Metropolis (Hek).
    /// </summary>
    public IReadOnlyList<RegionId> Hubs { get; init; } =
    [
        RegionId.From(10000002),
        RegionId.From(10000043),
        RegionId.From(10000032),
        RegionId.From(10000030),
        RegionId.From(10000042),
    ];

    /// <summary>Пауза, когда созревших регионов нет.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Пауза после отказа цикла.</summary>
    public TimeSpan ErrorDelay { get; init; } = TimeSpan.FromSeconds(60);

    public DiffOptions Diff { get; init; } = DiffOptions.Default;

    public FeatureOptions Features { get; init; } = FeatureOptions.Default;

    public string StaticData { get; init; } = "unknown";
}
