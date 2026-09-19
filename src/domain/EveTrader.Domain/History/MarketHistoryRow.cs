using EveTrader.Domain.Facts;

namespace EveTrader.Domain.History;

/// <summary>
/// Строка дневной истории: агрегат по паре «тип и регион» за календарные сутки.
/// Ордеров не содержит, поэтому события жизни ордера из неё не выводятся ни при каком
/// усилии — её ценность в глубине.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="MarketDate">Календарные сутки, к которым относится агрегат.</param>
/// <param name="Average">Средняя цена.</param>
/// <param name="Highest">Максимальная цена.</param>
/// <param name="Lowest">Минимальная цена.</param>
/// <param name="OrderCount">Число сделок.</param>
/// <param name="Volume">Объём.</param>
/// <param name="KnownAt">Когда строка стала известна системе.</param>
public sealed record MarketHistoryRow(
    RegionId Region,
    int TypeId,
    DateOnly MarketDate,
    decimal Average,
    decimal Highest,
    decimal Lowest,
    long OrderCount,
    long Volume,
    DateTimeOffset KnownAt)
{
    /// <summary>
    /// Ключ факта. Уточнённая версия тех же суток несёт тот же ключ и отличается только
    /// временем получения — на этом стоит би-темпоральное чтение.
    /// </summary>
    public string FactKey =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"history/{Region.Value}/{TypeId}/{MarketDate:yyyy-MM-dd}");
}
