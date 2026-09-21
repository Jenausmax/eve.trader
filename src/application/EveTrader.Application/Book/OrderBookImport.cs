using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Book;

/// <summary>
/// Конвертация архивных снимков стакана в факты — тем же конвейером свёртки, что у
/// живого сбора и у реплея.
///
/// Соблазн здесь — быстрый конвертер CSV в Parquet напрямую, мимо свёртки. Отвергнут:
/// это вторая реализация разметки, и расхождение между ней и живой свёрткой проявится
/// как отличный бэктест при случайном бое.
///
/// Сама свёртка живёт в <see cref="ObservationIntake" /> и общая на все источники;
/// здесь остаётся композиция: собрать источник и отдать его приёму.
/// </summary>
public sealed class OrderBookImport(
    ObservationIntake intake,
    ICoverageLog coverage,
    IMaterializationRegistry registry,
    TimeProvider clock,
    ILoggerFactory loggers)
{
    public async Task<OrderBookImportReport> RunAsync(
        IOrderBookSource archive,
        OrderBookScope scope,
        DiffOptions diffOptions,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(loggers);

        var source = new ArchiveObservationSource(
            archive, coverage, registry, scope, clock,
            loggers.CreateLogger<ArchiveObservationSource>());

        IntakeReport report = await intake
            .RunAsync(source, diffOptions, featureOptions, staticData, cancellationToken)
            .ConfigureAwait(false);

        return new OrderBookImportReport(
            report.Source,
            source.Published,
            source.Read,
            source.ResumedFrom,
            report.Written,
            report.AlreadyPresent,
            report.Events,
            report.SourceGaps,
            report.Regions,
            source.Days);
    }
}
