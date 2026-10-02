using EveTrader.Application.BackgroundServices;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Series;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Workers;

/// <summary>
/// Материализация рядов признаков — отдельный воркер, а не довесок к наблюдению.
///
/// Темп у неё свой (шаг рядов против секунд у наблюдения), вход свой (факты озера, а не
/// источник), и счётчики свои: «сколько окон отклонено» и «сколько наблюдений записано»
/// отвечают на разные вопросы. Склеенные в один воркер, они не дали бы ответа ни на
/// один.
///
/// Каждый цикл досчитывает концы окон с прошлого цикла до текущего момента. Первый цикл
/// после старта досчитывает глубину <see cref="SeriesOptions.Lookback" />: повтор уже
/// посчитанного отрезка ничего не удваивает, идентификатор порции выводится из
/// содержимого.
/// </summary>
public sealed class SeriesMaterializationWorker(
    IServiceProvider services,
    SeriesMaterialization materialization,
    SeriesOptions options,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    ILogger<SeriesMaterializationWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    private DateTimeOffset? materializedThrough;

    protected override string WorkerName => "SeriesMaterialization";

    protected override WorkerSchedule Schedule { get; } = new IntervalSchedule(options.Interval);

    protected override async Task<object?> ExecuteCycleAsync(
        WorkerContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = SeriesGrid.Floor(Clock.GetUtcNow(), options.Step);
        DateTimeOffset from = materializedThrough ?? (now - options.Lookback);

        if (now <= from)
        {
            return new SeriesCycle(0, 0, 0);
        }

        var windows = 0;
        var refused = 0;
        var points = 0;

        foreach (RegionId region in options.Regions)
        {
            SeriesMaterializationReport report = await materialization.RunAsync(
                new SeriesRequest(
                    region,
                    TimeRange.Between(from, now),
                    options.Definitions,
                    options.Thresholds,
                    StaticDataVersion.From(options.StaticData),
                    UpstreamCatalog.Empty),
                cancellationToken).ConfigureAwait(false);

            windows += report.Windows;
            refused += report.Refused;
            points += report.Points;
        }

        materializedThrough = now;

        if (refused > 0)
        {
            logger.LogInformation(
                "Ряды: {Refused} окон из {Windows} отклонено — внутри был ненаблюдавшийся интервал", refused, windows);
        }

        return new SeriesCycle(windows, refused, points);
    }
}
