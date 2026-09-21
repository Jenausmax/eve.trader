using EveTrader.Application.Book;
using EveTrader.Application.Diagnostics;
using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Intake;

/// <summary>
/// Приём наблюдений: свёртка в события и запись в озеро.
///
/// Единственное место, где наблюдение превращается в факты, — и оно одно на все
/// источники. Наблюдатели держатся по региону и живут весь прогон: исчезновение
/// выводится не из пары снимков, а из нескольких подряд.
/// </summary>
public sealed class ObservationIntake(
    ObservationDerivation derivation,
    IObservationDiagnostics diagnostics,
    ILogger<ObservationIntake> logger)
{
    public async Task<IntakeReport> RunAsync(
        IObservationSource source,
        DiffOptions diffOptions,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var observers = new Dictionary<RegionId, RegionObserver>();
        var regions = new HashSet<RegionId>();
        var written = 0;
        var alreadyPresent = 0;
        var unchanged = 0;
        var partial = 0;
        var failed = 0;
        var events = 0;
        var gaps = 0;

        await foreach (RegionObservation observation in
            source.ObserveAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            _ = regions.Add(observation.Region);

            if (!observation.CarriesBook)
            {
                // «Не изменилось» и отказ: наблюдение состоялось (или не состоялось, и
                // это тоже надо знать), а событий в нём нет. Без записи покрытия пробел
                // выглядел бы рыночным фактом.
                _ = await derivation
                    .WriteCoverageOnlyAsync(CoverageFor(observation, source.Name), cancellationToken)
                    .ConfigureAwait(false);

                if (observation.Outcome == CoverageOutcome.NotModified)
                {
                    unchanged++;
                }
                else
                {
                    failed++;
                }

                continue;
            }

            if (!observers.TryGetValue(observation.Region, out RegionObserver? observer))
            {
                observer = new RegionObserver(observation.Region, diffOptions, featureOptions);
                observers[observation.Region] = observer;
            }

            // Массив уже на руках у источника — копировать его ради Span незачем:
            // это миллион с лишним структур на наблюдение.
            OrderSnapshot[] orders = observation.Orders as OrderSnapshot[] ?? [.. observation.Orders];

            var meta = new ObservationMeta(
                observation.Region,
                observation.Observation,
                observation.Collected,
                observation.IsComplete,
                observation.Step,
                observation.IsBaseline || !observer.HasBaseline);

            ObservationOutcome outcome = observer.Observe(orders, meta);

            FactWriteOutcome result = await derivation.WriteAsync(
                meta, outcome, orders, featureOptions, staticData, source.Name, cancellationToken,
                (observation.PagesReceived, observation.PagesExpected))
                .ConfigureAwait(false);

            if (result == FactWriteOutcome.AlreadyPresent)
            {
                alreadyPresent++;

                continue;
            }

            written++;
            events += outcome.Events.Count;
            gaps += outcome.SourceGaps;

            // Доля изменившихся ордеров считается здесь, а не у источника: только после
            // свёртки известно, сколько ордеров наблюдение реально затронуло. По этому
            // числу и сходится бюджет объёма — оценка была 5–15 %.
            diagnostics.ChangedOrderFraction
                .WithTag("source", source.Name)
                .Record(outcome.Events.Count / (double)Math.Max(1, outcome.OrdersSeen));

            if (outcome.SourceGaps > 0)
            {
                diagnostics.SourceGaps.WithTag("source", source.Name).Add(outcome.SourceGaps);
            }

            if (observation.Outcome == CoverageOutcome.Partial)
            {
                partial++;
            }

            if (written % 100 == 0)
            {
                logger.LogInformation(
                    "Приём из {Source}: {Written} наблюдений, {Events} событий", source.Name, written, events);
            }
        }

        return new IntakeReport(
            source.Name, written, alreadyPresent, unchanged, partial, failed, events, gaps, regions.Count);
    }

    /// <summary>Запись покрытия для наблюдения без строк данных.</summary>
    public static CoverageEntry CoverageFor(RegionObservation observation, string source)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return observation.Outcome == CoverageOutcome.NotModified
            ? CoverageEntries.NotModified(
                observation.Observation, observation.Region, observation.Collected, source,
                observation.Step, observation.Collected.To)
            : CoverageEntries.Failed(
                observation.Observation, observation.Region, observation.Collected, source,
                observation.Step, observation.FailureReason ?? "источник не ответил", observation.Collected.To);
    }
}
