using EveTrader.Domain.Facts;

namespace EveTrader.Application.Reporting;

/// <summary>Строка отчёта о материализации.</summary>
/// <param name="Set">Набор фактов.</param>
/// <param name="Regions">Сколько регионов материализовано.</param>
/// <param name="Earliest">Начало самого раннего материализованного интервала.</param>
/// <param name="Latest">Конец самого позднего.</param>
public sealed record MaterializationReportRow(
    FactSet Set,
    long Regions,
    DateTimeOffset? Earliest,
    DateTimeOffset? Latest);
