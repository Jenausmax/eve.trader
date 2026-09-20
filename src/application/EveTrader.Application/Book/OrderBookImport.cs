using EveTrader.Application.Facts;
using EveTrader.Domain.Book;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Book;

/// <summary>
/// Конвертация архивных снимков стакана в факты — тем же конвейером свёртки, что у
/// живого сбора.
///
/// Соблазн здесь — быстрый конвертер CSV в Parquet напрямую, мимо свёртки. Отвергнут:
/// это вторая реализация разметки, и расхождение между ней и живой свёрткой проявится
/// как отличный бэктест при случайном бое.
///
/// Единица работы — сутки: они совпадают и с партицией озера, и с шагом чекпойнтов,
/// поэтому обрыв посреди конвертации стоит не больше суток работы, а продолжение не
/// требует отдельного состояния — точка продолжения выводится из журнала покрытия.
/// </summary>
public sealed class OrderBookImport(
    ObservationDerivation derivation,
    IMaterializationRegistry registry,
    ICoverageLog coverage,
    TimeProvider clock,
    ILogger<OrderBookImport> logger)
{
    public async Task<OrderBookImportReport> RunAsync(
        IOrderBookSource source,
        OrderBookScope scope,
        DiffOptions diffOptions,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scope);

        IReadOnlyList<DateTimeOffset> published = await source
            .AvailableSnapshotsAsync(scope.Within, cancellationToken)
            .ConfigureAwait(false);

        // Что уже свёрнуто — вопрос к журналу покрытия, а не к вызывающему. Иначе
        // продолжение с обрыва держалось бы на дисциплине вызова.
        IReadOnlySet<ObservationId> confirmed = await coverage
            .ConfirmedObservationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var from = OrderBookResume.IndexOf(published, source.KeyFor, confirmed);
        IReadOnlyList<DateTimeOffset> wanted = [.. published.Skip(from)];

        logger.LogInformation(
            "Источник {Source} публикует {Published} снимков; к свёртке {Wanted}, начиная с {From}",
            source.Name, published.Count, wanted.Count, wanted.Count == 0 ? null : wanted[0]);

        var observers = new Dictionary<RegionId, RegionObserver>();
        var regions = new HashSet<RegionId>();
        var days = new HashSet<DateOnly>();
        var read = 0;
        var written = 0;
        var alreadyPresent = 0;
        long events = 0;
        long gaps = 0;
        DateOnly? openDay = null;

        await foreach (OrderBookObservation snapshot in
            source.ObserveAsync(wanted, scope, cancellationToken).ConfigureAwait(false))
        {
            read++;

            var day = DateOnly.FromDateTime(snapshot.SnapshotAt.UtcDateTime);

            // Реестр отмечается по закрытым суткам, а не по каждому снимку: он отвечает
            // на вопрос «есть ли данные локально», и запись на снимок сделала бы его в
            // полсотни раз больше без нового ответа.
            if (openDay is { } previous && previous != day)
            {
                await RecordDayAsync(registry, previous, regions, source.Name, clock.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }

            openDay = day;
            _ = days.Add(day);

            foreach (RegionBook book in snapshot.Regions)
            {
                _ = regions.Add(book.Region);

                if (!observers.TryGetValue(book.Region, out RegionObserver? observer))
                {
                    observer = new RegionObserver(book.Region, diffOptions, featureOptions);
                    observers[book.Region] = observer;
                }

                // Массив уже на руках у источника — копировать его ради Span незачем:
                // это миллион с лишним структур на снимок.
                OrderSnapshot[] orders = book.Orders as OrderSnapshot[] ?? [.. book.Orders];

                var meta = new ObservationMeta(
                    book.Region,
                    snapshot.ObservationFor(book.Region),
                    book.Collected,
                    IsComplete: true,
                    snapshot.Step,
                    IsBaseline: !observer.HasBaseline);

                ObservationOutcome outcome = observer.Observe(orders, meta);

                FactWriteOutcome result = await derivation
                    .WriteAsync(meta, outcome, orders, featureOptions, staticData, source.Name, cancellationToken)
                    .ConfigureAwait(false);

                if (result == FactWriteOutcome.AlreadyPresent)
                {
                    alreadyPresent++;
                    continue;
                }

                written++;
                events += outcome.Events.Count;
                gaps += outcome.SourceGaps;
            }

            if (read % 48 == 0)
            {
                logger.LogInformation(
                    "Свёрнуто {Read} снимков из {Wanted}: {Events} событий, {Gaps} пропусков источника",
                    read, wanted.Count, events, gaps);
            }
        }

        if (openDay is { } last)
        {
            await RecordDayAsync(registry, last, regions, source.Name, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }

        return new OrderBookImportReport(
            source.Name,
            published.Count,
            read,
            wanted.Count == 0 ? null : wanted[0],
            written,
            alreadyPresent,
            events,
            gaps,
            regions.Count,
            days.Count);
    }

    /// <summary>Отмечает сутки материализованными по всем регионам, попавшим в прогон.</summary>
    public static Task RecordDayAsync(
        IMaterializationRegistry registry,
        DateOnly day,
        IReadOnlyCollection<RegionId> regions,
        string source,
        DateTimeOffset loadedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (regions.Count == 0)
        {
            return Task.CompletedTask;
        }

        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Отмечается набор событий: остальные наборы стакана приезжают тем же
        // наблюдением и порознь не бывают, а признаки производны и в реестре сырья
        // числиться не должны.
        return registry.RecordAsync(
            FactSet.OrderEvents,
            TimeRange.Between(from, from.AddDays(1)),
            regions,
            source,
            loadedAt,
            cancellationToken);
    }
}
