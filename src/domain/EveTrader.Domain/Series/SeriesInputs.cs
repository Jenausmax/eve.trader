using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Domain.Series;

/// <summary>
/// Всё, из чего считаются ряды региона за интервал: плоские строки, уже приведённые к
/// доменным типам. Хранилище их отобрало, считается — здесь.
/// </summary>
/// <param name="Region">Регион.</param>
/// <param name="Events">События жизни ордера.</param>
/// <param name="Features">Признаки стакана.</param>
/// <param name="Thresholds">Пороги, с которыми признаки посчитаны.</param>
/// <param name="Coverage">Записи покрытия региона.</param>
/// <param name="Baselines">Наблюдения, записавшие базовую линию.</param>
/// <param name="Materialized">Материализованные интервалы сырья.</param>
/// <param name="Upstream">Что публикует источник — отличает восполнимый пробел от утраченного.</param>
public sealed record SeriesInputs(
    RegionId Region,
    IReadOnlyList<OrderEvent> Events,
    IReadOnlyList<BookFeatures> Features,
    IReadOnlyList<int> Thresholds,
    IReadOnlyList<CoverageEntry> Coverage,
    IReadOnlySet<ObservationId> Baselines,
    IReadOnlyList<MaterializedInterval> Materialized,
    UpstreamCatalog Upstream);
