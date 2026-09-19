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
    public Task<FactWriteOutcome> WriteAsync(
        FactBatch batch,
        IReadOnlyList<CoverageEntry> entries,
        CancellationToken cancellationToken) =>
        WriteAllAsync([batch], entries, cancellationToken);

    public async Task<FactWriteOutcome> WriteAllAsync(
        IReadOnlyList<FactBatch> batches,
        IReadOnlyList<CoverageEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);

        if (batches.Count == 0)
        {
            return await WriteCoverageOnlyAsync(entries, cancellationToken).ConfigureAwait(false);
        }

        DateOnly observedDate = Validate(batches[0], entries);

        foreach (FactBatch batch in batches)
        {
            _ = Validate(batch, entries);

            if (batch.Observation != batches[0].Observation)
            {
                throw new ArgumentException("Все порции принадлежат одному наблюдению", nameof(batches));
            }
        }

        if (coverage.IsConfirmed(observedDate, batches[0].Observation))
        {
            return FactWriteOutcome.AlreadyPresent;
        }

        // Сначала все данные, покрытие последним: прерывание посередине оставляет
        // неподтверждённые файлы, а их читатель не видит и уборка сметёт.
        foreach (FactBatch batch in batches)
        {
            var file = layout.FileFor(batch.Set, batch.ObservedDate, batch.Region, batch.Observation);

            await AtomicParquet
                .WriteAsync(file, FactFileSchema.For(batch.Columns), FactFileSchema.Rows(batch), cancellationToken)
                .ConfigureAwait(false);
        }

        await coverage.AppendAsync(entries, cancellationToken).ConfigureAwait(false);

        return FactWriteOutcome.Written;
    }

    public async Task<FactWriteOutcome> WriteCoverageOnlyAsync(
        IReadOnlyList<CoverageEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            throw new ArgumentException("Наблюдение несёт хотя бы одну запись покрытия", nameof(entries));
        }

        if (coverage.IsConfirmed(ObservedDateOf(entries[0]), entries[0].Observation))
        {
            return FactWriteOutcome.AlreadyPresent;
        }

        await coverage.AppendAsync(entries, cancellationToken).ConfigureAwait(false);

        return FactWriteOutcome.Written;
    }

    /// <summary>
    /// Проверяет, что порция и покрытие описывают одно и то же наблюдение. Расхождение
    /// здесь означало бы факты, подтверждённые чужой записью, — то есть неверные по
    /// построению.
    /// </summary>
    public static DateOnly Validate(FactBatch batch, IReadOnlyList<CoverageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            throw new ArgumentException("Наблюдение несёт хотя бы одну запись покрытия", nameof(entries));
        }

        CoverageEntry? foreign = entries.FirstOrDefault(entry => entry.Observation != batch.Observation);
        if (foreign is not null)
        {
            throw new ArgumentException(
                $"Порция принадлежит наблюдению '{batch.Observation}', а запись покрытия — '{foreign.Observation}'",
                nameof(entries));
        }

        // У наборов, партиционируемых по региону, покрытие описывает ровно этот регион.
        // У прочих одно наблюдение накрывает все регионы глобального файла источника.
        if (batch.Region is { } region && entries.Any(entry => entry.Region != region))
        {
            throw new ArgumentException(
                $"Порция по региону {region}, а покрытие описывает другой регион",
                nameof(entries));
        }

        DateOnly observedDate = ObservedDateOf(entries[0]);

        return batch.ObservedDate == observedDate
            ? observedDate
            : throw new ArgumentException(
                $"Партиция порции {batch.ObservedDate:yyyy-MM-dd} не совпадает с датой наблюдения {observedDate:yyyy-MM-dd}",
                nameof(batch));
    }

    public static DateOnly ObservedDateOf(CoverageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return DateOnly.FromDateTime(entry.Collected.From.UtcDateTime);
    }
}
