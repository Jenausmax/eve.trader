using EveTrader.Domain.Facts;

namespace EveTrader.Application.Reporting;

/// <summary>
/// Порт агрегатов — только для отчётов оператору. Агрегация средствами хранилища здесь
/// разрешена, потому что расхождения обучения с боем для отчёта не существует по
/// определению.
///
/// Домену и коду вычисления признаков этот порт недоступен: закрепляется тестом
/// архитектуры, чтобы посчитать признак запросом было нельзя случайно.
/// </summary>
public interface IOperationalReportReader
{
    /// <summary>Отчёт о покрытии за интервал: доля покрытого времени, пропуски, разбивка по состояниям.</summary>
    Task<IReadOnlyList<CoverageReportRow>> CoverageReportAsync(
        TimeRange observed,
        CancellationToken cancellationToken);

    /// <summary>Отчёт о материализации: что лежит локально и по какому набору.</summary>
    Task<IReadOnlyList<MaterializationReportRow>> MaterializationReportAsync(CancellationToken cancellationToken);
}
