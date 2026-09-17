using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Facts;

/// <summary>
/// Журнал наблюдений. Источник истины о том, что наблюдение состоялось — он, а не
/// файловая система: файл без записи покрытия фактом не стал.
/// </summary>
public interface ICoverageLog
{
    /// <summary>Записи покрытия за интервал; пустой набор регионов — все регионы.</summary>
    Task<IReadOnlyList<CoverageEntry>> ReadAsync(
        TimeRange observed,
        IReadOnlyCollection<RegionId> regions,
        CancellationToken cancellationToken);

    /// <summary>Подтверждённые идентификаторы наблюдений — основа идемпотентности и уборки.</summary>
    Task<IReadOnlySet<ObservationId>> ConfirmedObservationsAsync(CancellationToken cancellationToken);
}
