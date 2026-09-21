using System.Runtime.CompilerServices;
using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Book;

/// <summary>
/// Архивные снимки как источник наблюдений.
///
/// Снимок у архива глобальный — все регионы разом; приём работает с наблюдением региона.
/// Разворачивание происходит здесь, и это и есть то единственное место, где
/// ветвление по типу источника допустимо.
///
/// Здесь же две заботы, которых нет у остальных источников: точка продолжения после
/// обрыва и отметка материализованных суток. Обе принадлежат добыче, а не приёму.
/// </summary>
public sealed class ArchiveObservationSource(
    IOrderBookSource archive,
    ICoverageLog coverage,
    IMaterializationRegistry registry,
    OrderBookScope scope,
    TimeProvider clock,
    ILogger<ArchiveObservationSource> logger) : IObservationSource
{
    public string Name => archive.Name;

    /// <summary>Сколько снимков публикует источник в запрошенном интервале.</summary>
    public int Published { get; private set; }

    /// <summary>Сколько снимков взято к свёртке после выбора точки продолжения.</summary>
    public int Wanted { get; private set; }

    /// <summary>С какого снимка продолжено.</summary>
    public DateTimeOffset? ResumedFrom { get; private set; }

    /// <summary>
    /// Сколько снимков источник отдал на самом деле. Меньше <see cref="Wanted" />, если
    /// какие-то файлы оказались не разобраны: такие сутки пропускаются, а не роняют прогон.
    /// </summary>
    public int Read { get; private set; }

    public int Days { get; private set; }

    public async IAsyncEnumerable<RegionObservation> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<DateTimeOffset> published = await archive
            .AvailableSnapshotsAsync(scope.Within, cancellationToken)
            .ConfigureAwait(false);

        Published = published.Count;

        // Что уже свёрнуто — вопрос к журналу покрытия, а не к вызывающему. Иначе
        // продолжение с обрыва держалось бы на дисциплине вызова.
        IReadOnlySet<ObservationId> confirmed = await coverage
            .ConfirmedObservationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var from = OrderBookResume.IndexOf(published, archive.KeyFor, confirmed);
        IReadOnlyList<DateTimeOffset> wanted = [.. published.Skip(from)];

        Wanted = wanted.Count;
        ResumedFrom = wanted.Count == 0 ? null : wanted[0];

        logger.LogInformation(
            "Источник {Source} публикует {Published} снимков; к свёртке {Wanted}, начиная с {From}",
            archive.Name, published.Count, wanted.Count, ResumedFrom);

        var regions = new HashSet<RegionId>();
        var seenDays = new HashSet<DateOnly>();
        DateOnly? openDay = null;

        await foreach (OrderBookObservation snapshot in
            archive.ObserveAsync(wanted, scope, cancellationToken).ConfigureAwait(false))
        {
            Read++;

            var day = DateOnly.FromDateTime(snapshot.SnapshotAt.UtcDateTime);

            // Реестр отмечается по закрытым суткам, а не по каждому снимку: он отвечает
            // на вопрос «есть ли данные локально», и запись на снимок сделала бы его в
            // полсотни раз больше без нового ответа.
            if (openDay is { } previous && previous != day)
            {
                await RecordDayAsync(registry, previous, regions, archive.Name, clock.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }

            openDay = day;
            _ = seenDays.Add(day);
            Days = seenDays.Count;

            foreach (RegionBook book in snapshot.Regions)
            {
                _ = regions.Add(book.Region);

                yield return new RegionObservation(
                    book.Region,
                    snapshot.ObservationFor(book.Region),
                    book.Collected,
                    CoverageOutcome.Success,
                    snapshot.Step,
                    book.Orders,
                    PagesReceived: 1,
                    PagesExpected: 1,
                    IsBaseline: false,
                    FailureReason: null);
            }
        }

        if (openDay is { } last)
        {
            await RecordDayAsync(registry, last, regions, archive.Name, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }
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
        ArgumentNullException.ThrowIfNull(regions);

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
