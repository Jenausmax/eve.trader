using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Signals;

/// <summary>
/// Станция, на которой правило станционной торговли рассматривает пары, — и тем самым
/// перечень пар, которые правило читает.
///
/// Перечень нужен не только правилу. Охват признаков стакана обязан иметь основание, и
/// основание здесь: правило читает все типы одной станции и ничего за её пределами,
/// значит признаки по прочим локациям ему не нужны (<see cref="FeatureCoverage" />).
/// </summary>
/// <param name="Region">Регион станции — партиция, из которой читаются факты.</param>
/// <param name="StationId">Локация станции.</param>
/// <param name="Name">Имя станции для оператора.</param>
public sealed record StationTradingScope(RegionId Region, long StationId, string Name)
{
    /// <summary>Jita IV - Moon 4 - Caldari Navy Assembly Plant — главный хаб станционной торговли.</summary>
    public static StationTradingScope Jita44 { get; } =
        new(RegionId.From(10000002), 60003760, "Jita 4-4");

    /// <summary>Входит ли пара в перечень, который читает правило.</summary>
    public bool Includes(long locationId) => locationId == StationId;

    /// <summary>
    /// Охват признаков, выведенный из перечня станций правила. Пары за пределами станций
    /// признаков не получают: правило их не читает, а признак, который никто не читает,
    /// платить за себя не должен.
    /// </summary>
    public static FeatureCoverage CoverageFor(IReadOnlyCollection<StationTradingScope> scopes) =>
        FeatureCoverage.Locations(
            "пары, которые читает правило станционной торговли: " + string.Join(", ", scopes.Select(static scope => scope.Name)),
            [.. scopes.Select(static scope => scope.StationId)]);
}
