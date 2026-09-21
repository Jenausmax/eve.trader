using EveTrader.Application.Facts;
using EveTrader.Application.Intake;
using EveTrader.Application.Replay;
using EveTrader.Domain.Book;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using Microsoft.Extensions.Logging;

namespace EveTrader.Application.Acceptance;

/// <summary>
/// Приёмка конвейера: перестроить признаки за материализованный интервал из сырья и
/// сверить с записанными.
///
/// Перестройка идёт тем же приёмом, что и живой сбор, — через реплей озера. Отдельного
/// пути «пересчитать признаки» здесь нет и не должно быть: он был бы второй реализацией
/// вычисления, и совпадение с первой доказывало бы только то, что обе написаны одной
/// рукой.
///
/// Перестроенное пишется в отдельное озеро, а не в то же: идемпотентность отбросила бы
/// всё по совпадению идентификаторов наблюдений, и сверять было бы нечего.
/// </summary>
public sealed class FeatureRebuild(
    ICoverageLog coverage,
    IFactRowReader recorded,
    ObservationIntake rebuildInto,
    IFactRowReader rebuilt,
    ILoggerFactory loggers)
{
    public async Task<AcceptanceReport> RunAsync(
        TimeRange within,
        IReadOnlyList<RegionId> regions,
        DiffOptions diffOptions,
        FeatureOptions featureOptions,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(loggers);

        IReadOnlyList<CoverageEntry> entries = await coverage
            .ReadAsync(within, regions, cancellationToken)
            .ConfigureAwait(false);

        var source = new LakeReplaySource(
            coverage, recorded, within, regions, loggers.CreateLogger<LakeReplaySource>());

        _ = await rebuildInto
            .RunAsync(source, diffOptions, featureOptions, staticData, cancellationToken)
            .ConfigureAwait(false);

        // В сверку входят только признаки полных наблюдений: частичные по построению
        // не воспроизводятся — по ним неизвестно, чего в стакане не было. И только те,
        // чьё окно подтверждения исчезновений закрылось внутри интервала: см.
        // <see cref="FactShapes.WindowClosed" />.
        HashSet<ObservationId> baselines =
            [.. (await source.ReadAsync(FactSet.OrderBaselines, cancellationToken).ConfigureAwait(false))
                .Select(static row => row.Envelope.Observation)];

        List<FactRow> before = await ReadAsync(recorded, within, cancellationToken).ConfigureAwait(false);
        List<FactRow> after = await ReadAsync(rebuilt, within, cancellationToken).ConfigureAwait(false);

        // Сверка считается по наблюдениям, давшим признаки: суточная история наблюдает
        // те же регионы, но признаков стакана не порождает, и в знаменателе сверки ей
        // делать нечего.
        HashSet<ObservationId> featured = [.. before.Select(static row => row.Observation)];
        HashSet<ObservationId> closed = FactShapes.WindowClosed(
            entries, baselines, diffOptions.DisappearanceWindow);
        HashSet<ObservationId> compared = [.. featured.Where(closed.Contains)];

        // Отбор по наблюдению, а не по интервалу: отсечение партиций работает посуточно,
        // и в выдачу за два часа приезжают все сутки. Сверять с ними нечего — их
        // наблюдений в проверяемом интервале нет.
        List<FactRow> expected = [.. before.Where(row => compared.Contains(row.Observation))];
        List<FactRow> produced = [.. after.Where(row => compared.Contains(row.Observation))];

        IReadOnlyList<FeatureMismatch> mismatches = FactShapes.Compare(expected, produced);

        return new AcceptanceReport(
            within,
            entries.Count,
            entries.Count(static entry => entry.Outcome is CoverageOutcome.Success),
            compared.Count,
            featured.Count - compared.Count,
            entries.Count(static entry => entry.Outcome is CoverageOutcome.Partial),
            entries.Count(static entry => entry.Outcome is CoverageOutcome.NotModified),
            entries.Count(static entry => entry.Outcome is CoverageOutcome.Failure),
            entries.Sum(static entry => entry.SourceGaps),
            entries.Count(FactShapes.IsBackdated),
            [.. expected.Select(static row => row.Envelope.StaticData.Value).Distinct().Order(StringComparer.Ordinal)],
            expected.Count,
            produced.Count,
            mismatches.Sum(static mismatch => mismatch.Missing),
            mismatches.Sum(static mismatch => mismatch.Extra),
            FactShapes.GapsIn(entries),
            [.. mismatches.Where(static mismatch => mismatch.Missing > 0 || mismatch.Extra > 0)]);
    }

    /// <summary>Признаки стакана за интервал — последняя известная версия каждого факта.</summary>
    public static async Task<List<FactRow>> ReadAsync(
        IFactRowReader rows,
        TimeRange within,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var collected = new List<FactRow>();

        await foreach (FactRow row in rows
            .ReadAsync(FactSet.BookFeatures, within, null, cancellationToken)
            .ConfigureAwait(false))
        {
            collected.Add(row);
        }

        return collected;
    }
}
