using EveTrader.Domain.Facts;
using EveTrader.Domain.History;

namespace EveTrader.Application.History;

/// <summary>
/// Наблюдение дневной истории за одни календарные сутки — то, что источник отдал за один
/// раз. Форма одна у архива и у живого ESI: стадии после приёма не должны различать,
/// откуда пришло.
/// </summary>
/// <param name="MarketDate">Календарные сутки, к которым относится наблюдение.</param>
/// <param name="Observation">Идентификатор наблюдения; он же ключ идемпотентности.</param>
/// <param name="Rows">Строки истории.</param>
/// <param name="Step">Объявленный источником шаг между наблюдениями.</param>
/// <param name="Regions">Регионы, попавшие в наблюдение.</param>
public sealed record MarketHistoryObservation(
    DateOnly MarketDate,
    ObservationId Observation,
    IReadOnlyList<MarketHistoryRow> Rows,
    TimeSpan Step,
    IReadOnlyList<RegionId> Regions);
