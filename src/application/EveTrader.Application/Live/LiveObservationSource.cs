using System.Runtime.CompilerServices;
using EveTrader.Application.Intake;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;

namespace EveTrader.Application.Live;

/// <summary>
/// Живой ESI как источник наблюдений.
///
/// Протокол добычи здесь свой: снимков заранее нет, опрос идёт по истечении срока
/// годности, и какие регионы созрели — решает расписание. Всё это остаётся внутри
/// источника; наружу выходит то же наблюдение региона, что у архива и у реплея.
///
/// Один проход источника — один цикл диспетчера. Состояние сбора живёт снаружи, между
/// циклами: валидаторы кэша и отметки о входе в охват переживают цикл, иначе условные
/// запросы перестали бы работать после первого же тика.
/// </summary>
public sealed class LiveObservationSource(
    IRegionBookPoller poller,
    ScopeHistory scope,
    RegionViability viability,
    ObservationSchedule schedule,
    CollectorState state,
    IReadOnlyList<RegionId> hubs,
    TimeProvider clock) : IObservationSource
{
    public string Name => poller.Name;

    public async IAsyncEnumerable<RegionObservation> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        ScopePolicy? policy = scope.At(now);

        if (policy is null)
        {
            yield break;
        }

        IReadOnlyList<RegionId> inScope = policy.Resolve(viability.Viable, hubs);

        foreach (RegionDue due in schedule.Due(inScope, policy, now))
        {
            cancellationToken.ThrowIfCancellationRequested();

            RegionPoll poll = await poller
                .PollAsync(due.Region, state.Validators.GetValueOrDefault(due.Region), cancellationToken)
                .ConfigureAwait(false);

            schedule.SourceExpires(due.Region, poll.ExpiresAt);
            state.Validators[due.Region] = poll.Validator;

            if (poll.Outcome is RegionPollOutcome.BudgetExhausted)
            {
                // Не спрашивали — наблюдения не было. Расписание уже подвинуто на срок
                // паузы, а записывать отказ, которого источник не давал, нечестно.
                continue;
            }

            schedule.Observed(due.Region, poll.Collected.To);
            viability.Observed(due.Region, poll.Orders.Count);

            yield return new RegionObservation(
                due.Region,
                ObservationId.From($"{poller.Name}-{due.Region.Value}-{poll.Collected.From:yyyyMMddTHHmmssZ}"),
                poll.Collected,
                Outcome(poll.Outcome),
                policy.IntervalFor(due.Region),
                poll.Orders,
                poll.Pages.Count,
                poll.PagesExpected,
                state.IsFirstSinceScopeChange(
                    due.Region, scope.InScopeSince(due.Region, poll.Collected.From, viability.Viable, hubs)),
                poll.FailureReason);
        }
    }

    /// <summary>
    /// Исход опроса в исход покрытия.
    ///
    /// Все ветви перечислены явно, включая <see cref="RegionPollOutcome.BudgetExhausted" />,
    /// до которого здесь не доходит: исчерпанный бюджет отсеивается выше, потому что
    /// наблюдения не было вовсе. Неявная ветвь тут опасна — автофикс анализатора однажды
    /// уже дописал в неё исключение, и в бою это уронило бы обработку отказа источника.
    /// </summary>
    public static CoverageOutcome Outcome(RegionPollOutcome outcome) => outcome switch
    {
        RegionPollOutcome.Complete => CoverageOutcome.Success,
        RegionPollOutcome.NotModified => CoverageOutcome.NotModified,
        RegionPollOutcome.Partial => CoverageOutcome.Partial,
        RegionPollOutcome.Failed => CoverageOutcome.Failure,
        RegionPollOutcome.BudgetExhausted => CoverageOutcome.Failure,
        _ => CoverageOutcome.Failure,
    };
}
