using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;

namespace EveTrader.Application.Series;

/// <summary>Что материализовать: регион, интервал концов окон и определения рядов.</summary>
/// <param name="Region">Регион.</param>
/// <param name="Interval">Интервал, в который попадают концы окон.</param>
/// <param name="Definitions">Определения рядов.</param>
/// <param name="Thresholds">Пороги, с которыми записаны признаки стакана.</param>
/// <param name="StaticData">Версия статических данных — несётся на каждой строке.</param>
/// <param name="Upstream">Что публикует источник; пустой каталог — любой пробел безвозвратен.</param>
public sealed record SeriesRequest(
    RegionId Region,
    TimeRange Interval,
    IReadOnlyList<SeriesDefinition> Definitions,
    IReadOnlyList<int> Thresholds,
    StaticDataVersion StaticData,
    UpstreamCatalog Upstream);
