using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Настройки озера. Путь и глубина окна — именно настройки, а не константы: окно
/// двигают по результатам замеров, а корень отличается между прогоном оператора и тестом.
/// </summary>
public sealed class LakeOptions
{
    public const string SectionName = "Lake";

    /// <summary>Корень озера на диске.</summary>
    public required string Root { get; init; }

    /// <summary>Глубина локально хранимого ордербука.</summary>
    public TimeSpan OrderBookWindow { get; init; } = TimeSpan.FromDays(730);

    public MaterializationWindow Window() => MaterializationWindow.Of(OrderBookWindow);
}
