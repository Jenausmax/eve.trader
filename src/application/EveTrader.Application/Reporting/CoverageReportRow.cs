using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Reporting;

/// <summary>Строка отчёта о покрытии по региону.</summary>
/// <param name="Region">Регион.</param>
/// <param name="Observations">Сколько наблюдений записано.</param>
/// <param name="CoveredFraction">Доля покрытого времени.</param>
/// <param name="SourceGaps">Сколько ордеров пропало и вернулось.</param>
/// <param name="PartialObservations">Сколько наблюдений неполные.</param>
/// <param name="FailedObservations">Сколько попыток завершились отказом.</param>
/// <param name="State">Состояние покрытия за интервал.</param>
public sealed record CoverageReportRow(
    RegionId Region,
    long Observations,
    double CoveredFraction,
    long SourceGaps,
    long PartialObservations,
    long FailedObservations,
    CoverageState State);
