using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.History;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Workers;

/// <summary>
/// Досинхронизация дневной истории — отдельный воркер, а не довесок к диспетчеру
/// наблюдения.
///
/// Правило запрещает склеивать разные задачи в один воркер, и причина видна здесь: у
/// истории свой темп (раз в сутки против пяти минут), свой источник и свой набор
/// счётчиков. Склеенные задачи дали бы счётчики, по которым нельзя сказать, что именно
/// отказало.
/// </summary>
public sealed class DailyHistoryWorker(
    IServiceProvider services,
    DailyHistoryImport import,
    IMarketHistorySource source,
    DailyHistoryOptions options,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    ILogger<DailyHistoryWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    protected override string WorkerName => "DailyHistory";

    protected override WorkerSchedule Schedule { get; } = new IntervalSchedule(options.Interval);

    /// <summary>
    /// Часы берутся у базы, а не из захваченного параметра: два поля с одним и тем же
    /// <see cref="TimeProvider" /> разошлись бы при подмене в тесте, и разошлись бы молча.
    /// </summary>
    protected override async Task<object?> ExecuteCycleAsync(
        WorkerContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = Clock.GetUtcNow();

        DailyHistoryImportReport report = await import.RunAsync(
            source,
            MarketHistoryScope.Fresh(TimeRange.Between(now - options.Lookback, now.AddDays(1))),
            StaticDataVersion.From(options.StaticData),
            cancellationToken).ConfigureAwait(false);

        return new DailyHistoryCycle(report.DaysWritten, report.DaysUnchanged, (int)report.RowsWritten);
    }
}
