using EveTrader.Domain.Facts;

namespace EveTrader.Application.Book;

/// <summary>
/// Один снимок стакана — то, что источник отдал за один раз. Форма одна у архива и у
/// живого сбора: стадии после приёма не должны различать, откуда пришло.
/// </summary>
/// <param name="SnapshotAt">Момент снимка по объявлению источника; по нему снимки упорядочены.</param>
/// <param name="Key">
/// Имя снимка у источника, уникальное и устойчивое между прогонами. Из него собирается
/// идентификатор наблюдения, он же ключ идемпотентности.
/// </param>
/// <param name="Step">Объявленный источником шаг между наблюдениями.</param>
/// <param name="Regions">Стаканы регионов, попавших в снимок.</param>
public sealed record OrderBookObservation(
    DateTimeOffset SnapshotAt,
    string Key,
    TimeSpan Step,
    IReadOnlyList<RegionBook> Regions)
{
    /// <summary>
    /// Идентификатор наблюдения по региону.
    ///
    /// Регион входит в идентификатор, и это не украшение: покрытие подтверждает
    /// наблюдение региона, а записи покрытия лежат по имени наблюдения вперемешку.
    /// Один идентификатор на весь глобальный снимок означал бы, что подтверждён ровно
    /// один регион из сотни, а остальные девяносто девять — молча «уже записаны».
    /// </summary>
    public ObservationId ObservationFor(RegionId region) =>
        ObservationId.From($"{Key}{OrderBookResume.RegionMarker}{region.Value}");
}
