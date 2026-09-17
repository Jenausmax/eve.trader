using EveTrader.Application.Facts;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;

namespace EveTrader.Infrastructure.Facts.Lake;

/// <summary>
/// Запись наблюдения в озеро в три шага: данные во временный файл, атомарное
/// переименование, запись покрытия последней.
///
/// Порядок шагов — и есть определение факта. Прерывание после второго шага оставляет
/// файл, на который не ссылается покрытие: он невидим читателям и подлежит уборке.
/// Прерывания после третьего шага не бывает — он атомарен.
/// </summary>
public sealed class ParquetFactWriter(LakeLayout layout, ParquetCoverageLog coverage) : IFactWriter
{
    public async Task<FactWriteOutcome> WriteAsync(
        FactBatch batch,
        CoverageEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(entry);

        if (batch.Observation != entry.Observation)
        {
            throw new ArgumentException(
                $"Порция принадлежит наблюдению '{batch.Observation}', а покрытие — '{entry.Observation}'",
                nameof(entry));
        }

        if (batch.Region != entry.Region)
        {
            throw new ArgumentException(
                $"Порция по региону {batch.Region}, а покрытие по {entry.Region}",
                nameof(entry));
        }

        DateOnly observedDate = ObservedDateOf(entry);

        if (batch.ObservedDate != observedDate)
        {
            throw new ArgumentException(
                $"Партиция порции {batch.ObservedDate:yyyy-MM-dd} не совпадает с датой наблюдения {observedDate:yyyy-MM-dd}",
                nameof(batch));
        }

        if (coverage.IsConfirmed(observedDate, entry.Observation))
        {
            return FactWriteOutcome.AlreadyPresent;
        }

        var file = layout.FileFor(batch.Set, batch.ObservedDate, batch.Region, batch.Observation);

        await AtomicParquet
            .WriteAsync(file, FactFileSchema.For(batch.Columns), FactFileSchema.Rows(batch), cancellationToken)
            .ConfigureAwait(false);

        await coverage.AppendAsync(entry, cancellationToken).ConfigureAwait(false);

        return FactWriteOutcome.Written;
    }

    public async Task<FactWriteOutcome> WriteCoverageOnlyAsync(
        CoverageEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (coverage.IsConfirmed(ObservedDateOf(entry), entry.Observation))
        {
            return FactWriteOutcome.AlreadyPresent;
        }

        await coverage.AppendAsync(entry, cancellationToken).ConfigureAwait(false);

        return FactWriteOutcome.Written;
    }

    public static DateOnly ObservedDateOf(CoverageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return DateOnly.FromDateTime(entry.Collected.From.UtcDateTime);
    }
}
