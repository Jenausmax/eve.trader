using EveTrader.Application.Book;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Live;

/// <summary>
/// Живой сбор: опрашивает регионы, у которых истёк срок, сворачивает наблюдения в
/// события и пишет их в озеро.
///
/// Наблюдатели держатся по региону и живут между циклами: исчезновение выводится не из
/// пары снимков, а из нескольких подряд, и наблюдатель, созданный заново на каждый цикл,
/// не подтвердил бы ни одного.
/// </summary>
public sealed class LiveCollector(
    IRegionBookPoller poller,
    ObservationDerivation derivation,
    ScopeHistory scope,
    RegionViability viability,
    ObservationSchedule schedule,
    IReadOnlyList<RegionId> hubs,
    TimeProvider clock,
    ILogger<LiveCollector> logger)
{
    private CollectorState? state;

    /// <summary>Один цикл диспетчера: взять созревшие регионы и наблюдать их.</summary>
    public async Task<CollectionCycle> RunCycleAsync(
        DiffOptions diffOptions,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        state ??= new CollectorState(diffOptions, featureOptions);

        DateTimeOffset now = clock.GetUtcNow();
        ScopePolicy? policy = scope.At(now);

        if (policy is null)
        {
            return new CollectionCycle(0, 0, 0, 0, 0);
        }

        IReadOnlyList<RegionId> inScope = policy.Resolve(viability.Viable, hubs);
        IReadOnlyList<RegionDue> due = schedule.Due(inScope, policy, now);

        var observed = 0;
        var partial = 0;
        var unchanged = 0;
        var failed = 0;
        var events = 0;

        foreach (RegionDue region in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RegionPoll poll = await poller
                .PollAsync(region.Region, state.Validators.GetValueOrDefault(region.Region), cancellationToken)
                .ConfigureAwait(false);

            schedule.SourceExpires(region.Region, poll.ExpiresAt);
            state.Validators[region.Region] = poll.Validator;

            if (poll.Outcome is RegionPollOutcome.BudgetExhausted)
            {
                // Не спрашивали — значит и наблюдения не было. Покрытие об этом узнает
                // из записи отказа, а расписание уже подвинуто на срок паузы.
                failed++;

                continue;
            }

            schedule.Observed(region.Region, poll.Collected.To);
            viability.Observed(region.Region, poll.Orders.Count);

            events += await ObservationWrite.WriteAsync(
                poll,
                policy,
                state,
                derivation,
                poller.Name,
                state.IsFirstSinceScopeChange(
                    region.Region, scope.InScopeSince(region.Region, poll.Collected.From, viability.Viable, hubs)),
                featureOptions,
                staticData,
                cancellationToken).ConfigureAwait(false);

            switch (poll.Outcome)
            {
                case RegionPollOutcome.Complete:
                    observed++;

                    break;

                case RegionPollOutcome.NotModified:
                    unchanged++;

                    break;

                case RegionPollOutcome.Partial:
                    partial++;

                    break;
                case RegionPollOutcome.Failed:
                    break;
                case RegionPollOutcome.BudgetExhausted:
                    break;
                default:
                    failed++;

                    break;
            }
        }

        if (due.Count > 0)
        {
            logger.LogInformation(
                "Цикл сбора: {Observed} наблюдений, {Unchanged} без изменений, {Partial} частичных, {Events} событий",
                observed, unchanged, partial, events);
        }

        return new CollectionCycle(observed, unchanged, partial, failed, events);
    }
}
