using EveTrader.Application.Diagnostics;
using EveTrader.Application.Live;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Infrastructure.Esi.Orders;

/// <summary>
/// Опрос стакана региона у публичного ESI.
///
/// Буфер ордеров переиспользуется между наблюдениями, поэтому опросчик заводится один на
/// регион, а не на запрос: новый массив на миллион структур каждые пять минут
/// фрагментирует кучу больших объектов.
///
/// Вызов внешнего источника обёрнут областью операции с гистограммой длительности —
/// по ней и видно, влезает ли наблюдение крупнейшего региона в пятиминутный шаг.
/// </summary>
public sealed class EsiRegionBookPoller(
    HttpClient client,
    EsiOptions options,
    ErrorBudget budget,
    TimeProvider clock,
    IRawPageArchive rawPages,
    IObservationDiagnostics diagnostics,
    ILogger<EsiRegionBookPoller> logger) : IRegionBookPoller
{
    private readonly EsiPollContext context =
        new(client, options, budget, clock, rawPages, diagnostics, new OrderBuffer(), logger);

    public string Name => "esi";

    public Task<RegionPoll> PollAsync(
        RegionId region,
        string? validator,
        CancellationToken cancellationToken)
    {
        BudgetVerdict verdict = budget.Check();

        if (!verdict.IsAllowed)
        {
            logger.LogWarning("Регион {Region} не опрошен: {Reason}", region.Value, verdict.Reason);

            // Ни запроса, ни области: наблюдения не было вовсе, и длительность
            // невыполненного наблюдения испортила бы гистограмму.
            return Task.FromResult(RegionPolls.Skipped(region, verdict.Reason, verdict.ResumeAt));
        }

        return diagnostics.Operation("observe.region")
            .WithHistogram(diagnostics.RegionObservationDuration.WithTag("region.id", region.Value))
            .WithTag("region.id", region.Value)
            .WithTag("source", Name)
            .RunAsync(scope => EsiRegionPolling.PagesAsync(context, scope, region, validator, cancellationToken));
    }
}
