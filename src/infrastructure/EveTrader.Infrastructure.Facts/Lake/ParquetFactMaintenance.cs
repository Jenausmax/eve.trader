using EveTrader.Application.Facts;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Обслуживание озера. Всё, что здесь разрешено, разрешено потому, что не трогает факты:
/// убирается неподтверждённое (фактами не стало) и производное (перестраивается из сырья).
/// </summary>
public sealed class ParquetFactMaintenance(LakeLayout layout, ICoverageLog coverage) : IFactMaintenance
{
    public async Task<int> SweepUnconfirmedAsync(CancellationToken cancellationToken)
    {
        IReadOnlySet<ObservationId> confirmed = await coverage.ConfirmedObservationsAsync(cancellationToken).ConfigureAwait(false);
        var removed = 0;

        // Недописанные файлы прерванной записи: под своим именем они так и не появились.
        foreach (var temporary in layout.TempFiles().ToList())
        {
            File.Delete(temporary);
            removed++;
        }

        foreach (var file in layout.DataFiles().ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (confirmed.Contains(LakeLayout.ObservationOf(file)))
            {
                continue;
            }

            File.Delete(file);
            removed++;
        }

        return removed;
    }

    public Task ApplyRetentionAsync(FactSet set, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FactSets.IsRaw(set))
        {
            throw FactsAreImmutableException.ForRetention(set);
        }

        // Производное подчиняется сроку хранения: оно выводимо из сырья.
        return DropDerivedAsync(
            set,
            TimeRange.Between(DateTimeOffset.MinValue, DateTimeOffset.MaxValue - maxAge),
            cancellationToken);
    }

    /// <summary>
    /// Удаляет файлы производного набора за интервал — и подтверждения тех порций, что
    /// подтверждают только производное.
    ///
    /// Признаки стакана подтверждены записью наблюдения, которая подтверждает и сырьё:
    /// её трогать нельзя. Ряды и сигналы подтверждены своими записями «производное», и
    /// они уходят вместе с данными: иначе перестройка того же отрезка наткнулась бы на
    /// «уже подтверждено» и не записала бы ничего, оставив дыру на месте удалённого.
    /// </summary>
    public async Task<int> DropDerivedAsync(FactSet set, TimeRange observed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FactSets.IsRaw(set))
        {
            throw FactsAreImmutableException.ForDrop(set);
        }

        var root = layout.SetRoot(set);

        if (!Directory.Exists(root))
        {
            return 0;
        }

        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*" + LakeLayout.ParquetExtension, SearchOption.AllDirectories).ToList())
        {
            if (!PartitionPaths.InRange(file, observed))
            {
                continue;
            }

            File.Delete(file);
            removed++;

            if (PartitionPaths.ObservedDateOf(file) is { } date
                && layout.CoverageFileFor(date, LakeLayout.ObservationOf(file)) is var confirmation
                && File.Exists(confirmation)
                && (await ParquetCoverageLog.ReadFileAsync(confirmation, cancellationToken).ConfigureAwait(false))
                    .Select(CoverageSchema.FromRow)
                    .All(static entry => !entry.IsObservation))
            {
                File.Delete(confirmation);
            }
        }

        return removed;
    }
}
