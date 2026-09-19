using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Application.Book;

/// <summary>
/// Запись свёрнутого наблюдения в озеро: события, базовые линии, чекпойнт и признаки
/// одним наблюдением и одной записью покрытия.
///
/// Наборы разные, наблюдение одно. Подтверждать их порознь нельзя — часть осталась бы
/// невидимой, а «факт — строка, подтверждённая покрытием» перестало бы выполняться.
/// </summary>
public sealed class ObservationDerivation(IFactWriter writer, DailyCheckpointPolicy checkpoints)
{
    public async Task<FactWriteOutcome> WriteAsync(
        ObservationMeta meta,
        ObservationOutcome outcome,
        IReadOnlyList<OrderSnapshot> book,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(featureOptions);

        var observedDate = DateOnly.FromDateTime(meta.Collected.From.UtcDateTime);
        var batches = new List<FactBatch>(4);

        foreach (IGrouping<FactSet, OrderEvent> group in
            outcome.Events.GroupBy(moment => OrderEventFacts.SetFor(moment.Kind)))
        {
            batches.Add(OrderEventFacts.ToBatch(
                group.Key, meta.Region, meta.Observation, observedDate, [.. group], staticData));
        }

        if (outcome.Features.Count > 0)
        {
            batches.Add(BookFeatureFacts.ToBatch(
                meta.Region, meta.Observation, observedDate, outcome.Features,
                featureOptions.DepthThresholdsBasisPoints, staticData));
        }

        // Чекпойнт снимается только по полному наблюдению: по частичному состав стакана
        // неизвестен, и записывать его как состав значило бы соврать.
        var checkpointed = meta.IsComplete && checkpoints.ShouldCheckpoint(meta.Region, meta.Collected.To);

        if (checkpointed)
        {
            batches.Add(BookCheckpointFacts.ToBatch(
                meta.Region, meta.Observation, observedDate, book.ToArray(), meta, staticData));
        }

        IReadOnlyList<CoverageEntry> coverage = [Coverage(meta, outcome)];

        FactWriteOutcome written = await writer
            .WriteAllAsync(batches, coverage, cancellationToken)
            .ConfigureAwait(false);

        if (checkpointed && written == FactWriteOutcome.Written)
        {
            checkpoints.Recorded(meta.Region, meta.Collected.To);
        }

        return written;
    }

    public static CoverageEntry Coverage(ObservationMeta meta, ObservationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(outcome);

        return meta.IsComplete
            ? CoverageEntries.Success(
                meta.Observation, meta.Region, meta.Collected, pages: 1, orderCount: outcome.OrdersSeen,
                source: "observer", observationStep: meta.Step, knownAt: meta.Collected.To,
                sourceGaps: outcome.SourceGaps)
            : CoverageEntries.Partial(
                meta.Observation, meta.Region, meta.Collected, pagesReceived: 1, pagesExpected: 2,
                orderCount: outcome.OrdersSeen, source: "observer", observationStep: meta.Step,
                knownAt: meta.Collected.To);
    }
}
