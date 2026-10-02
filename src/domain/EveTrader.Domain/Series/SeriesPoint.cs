using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Точка ряда производного признака.
/// </summary>
/// <param name="Definition">Определение ряда: окно, шаг, источники.</param>
/// <param name="Region">Регион.</param>
/// <param name="TypeId">Тип предмета.</param>
/// <param name="LocationId">Локация.</param>
/// <param name="Side">Сторона стакана, к которой относится величина.</param>
/// <param name="Window">Окно, за которое посчитана величина.</param>
/// <param name="Value">Значение.</param>
/// <param name="Incomplete">
/// Окно содержало частичное наблюдение. Точка порождена, но в обучающую выборку не
/// попадает: часть стакана на этот момент не была получена, и величина занижена на
/// неизвестную долю.
/// </param>
public sealed record SeriesPoint(
    SeriesDefinition Definition,
    RegionId Region,
    int TypeId,
    long LocationId,
    SeriesSide Side,
    TimeRange Window,
    double Value,
    bool Incomplete)
{
    /// <summary>
    /// Ключ факта. Определение ряда входит целиком — иначе тот же признак с другим окном
    /// перезаписывал бы этот как уточнённую версию самого себя.
    /// </summary>
    public string FactKey =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"series/{Definition.Key}/{Region.Value}/{TypeId}/{LocationId}/{(int)Side}/{Window.To:yyyyMMddTHHmmssZ}");
}
