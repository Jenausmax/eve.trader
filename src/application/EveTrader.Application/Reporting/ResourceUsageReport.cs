using EveTrader.Domain.Facts;

namespace EveTrader.Application.Reporting;

/// <summary>
/// Сборка отчёта о расходе ресурсов из двух следов: журнала покрытия и окна сырых
/// страниц.
///
/// Метрики OTel сюда не годятся и не должны: они живут в процессе сбора, а вопрос
/// «сколько израсходовано за прошедшие сутки» задаётся другому процессу и после
/// перезапуска. Отвечать на него можно только по тому, что осталось на диске.
/// </summary>
public sealed class ResourceUsageReport(IOperationalReportReader reports, IRawTrafficMeter traffic)
{
    public async Task<ResourceUsage> ForAsync(TimeRange within, CancellationToken cancellationToken)
    {
        SourceUsageCounts counts = await reports
            .SourceUsageAsync(within, cancellationToken)
            .ConfigureAwait(false);

        RawPageUsage received = await traffic
            .MeasureAsync(within, cancellationToken)
            .ConfigureAwait(false);

        return new ResourceUsage(
            within,
            counts.Observations,
            counts.Requests,
            counts.NotModified,
            counts.Failed,
            received.Bytes,
            received.Pages,
            received.Earliest);
    }
}
