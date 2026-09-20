using EveTrader.Application.Book;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Scope;

namespace EveTrader.Application.Live;

/// <summary>Свёртка и запись одного опрошенного региона.</summary>
internal static class ObservationWrite
{
    /// <summary>
    /// Пишет наблюдение и возвращает число порождённых событий.
    ///
    /// «Не изменилось» и отказ записываются покрытием без строк данных: наблюдение
    /// состоялось (или не состоялось, и это тоже надо знать), а событий в нём нет. Без
    /// такой записи пробел выглядел бы рыночным фактом.
    /// </summary>
    public static async Task<int> WriteAsync(
        RegionPoll poll,
        ScopePolicy policy,
        CollectorState state,
        ObservationDerivation derivation,
        string source,
        bool isBaseline,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(derivation);

        var observation = ObservationId.From(
            $"{source}-{poll.Region.Value}-{poll.Collected.From:yyyyMMddTHHmmssZ}");

        TimeSpan step = policy.IntervalFor(poll.Region);

        if (poll.Outcome is RegionPollOutcome.NotModified or RegionPollOutcome.Failed)
        {
            CoverageEntry entry = poll.Outcome is RegionPollOutcome.NotModified
                ? CoverageEntries.NotModified(
                    observation, poll.Region, poll.Collected, source, step, poll.Collected.To)
                : CoverageEntries.Failed(
                    observation, poll.Region, poll.Collected, source, step,
                    poll.FailureReason ?? "источник не ответил", poll.Collected.To);

            _ = await derivation.WriteCoverageOnlyAsync(entry, cancellationToken).ConfigureAwait(false);

            return 0;
        }

        var meta = new ObservationMeta(
            poll.Region, observation, poll.Collected, poll.IsComplete, step, isBaseline);

        ObservationOutcome outcome = state.ObserverFor(poll.Region).Observe([.. poll.Orders], meta);

        _ = await derivation.WriteAsync(
            meta, outcome, poll.Orders, featureOptions, staticData, source, cancellationToken,
            (poll.Pages.Count, poll.PagesExpected))
            .ConfigureAwait(false);

        return outcome.Events.Count;
    }
}
