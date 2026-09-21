using System.Runtime.CompilerServices;
using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Replay;

/// <summary>
/// Реплей собственного озера: восстановленные наблюдения подаются на тот же вход, что и
/// живой сбор.
///
/// Вход реплея — чекпойнт плюс события, а не сырые страницы. Хранить страницы вечно
/// означало бы терабайты в год ради данных, которые полностью выводимы; окно сырых
/// страниц на 48 часов существует только для разбора дефектов парсера.
///
/// Отсюда ограничение, которое надо знать заранее: в цепочку восстановления входят
/// только полные наблюдения. Частичные воспроизведены быть не могут — по ним неизвестно,
/// чего в стакане не было, — и сверка ведётся с точностью до помеченных неполными.
/// </summary>
public sealed class LakeReplaySource(
    ICoverageLog coverage,
    IFactRowReader rows,
    TimeRange within,
    IReadOnlyList<RegionId> regions,
    ILogger<LakeReplaySource> logger) : IObservationSource
{
    public string Name => "replay";

    /// <summary>Все строки набора за интервал реплея.</summary>
    public async Task<IReadOnlyList<FactRow>> ReadAsync(FactSet set, CancellationToken cancellationToken)
    {
        var collected = new List<FactRow>();

        await foreach (FactRow row in rows.ReadAsync(set, within, null, cancellationToken).ConfigureAwait(false))
        {
            collected.Add(row);
        }

        return collected;
    }

    public async IAsyncEnumerable<RegionObservation> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<CoverageEntry> entries = await coverage
            .ReadAsync(within, regions, cancellationToken)
            .ConfigureAwait(false);

        // Только полные наблюдения: по частичному состав стакана не восстановим, а
        // подать его как полное значило бы соврать о том, чего в стакане не было.
        List<CoverageEntry> replayable = [.. entries
            .Where(entry => entry.Outcome is CoverageOutcome.Success)
            .OrderBy(entry => entry.Region.Value)
            .ThenBy(entry => entry.Collected.To)];

        logger.LogInformation(
            "Реплей: {Replayable} полных наблюдений из {Total} записей покрытия",
            replayable.Count, entries.Count);

        // Читается последняя известная версия каждого факта — то же, что увидел бы
        // любой другой потребитель озера. Реплей не привилегированный читатель.
        IReadOnlyList<FactRow> checkpoints = await ReadAsync(FactSet.BookCheckpoints, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<FactRow> events = await ReadAsync(FactSet.OrderEvents, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<FactRow> baselines = await ReadAsync(FactSet.OrderBaselines, cancellationToken)
            .ConfigureAwait(false);

        foreach (IGrouping<RegionId, CoverageEntry> region in replayable.GroupBy(entry => entry.Region))
        {
            var timeline = RegionTimeline.Of(
                region.Key, checkpoints, events, baselines);

            foreach (CoverageEntry entry in region)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<OrderSnapshot> book = timeline.BookAt(entry.Collected.To);

                yield return new RegionObservation(
                    entry.Region,
                    entry.Observation,
                    entry.Collected,
                    CoverageOutcome.Success,
                    entry.ObservationStep,
                    book,
                    PagesReceived: entry.PagesReceived,
                    PagesExpected: entry.PagesExpected,
                    IsBaseline: timeline.IsBaseline(entry.Collected.To),
                    FailureReason: null);
            }
        }
    }
}
