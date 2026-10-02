using EveTrader.Application.Facts;
using EveTrader.Application.Replay;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Series;

namespace EveTrader.Application.Series;

/// <summary>
/// Материализация рядов признаков: прочитать плоские строки, посчитать в домене,
/// записать отдельным набором с подтверждением покрытием.
///
/// Хранилище здесь только отбирает строки — по набору, интервалу и горизонту знания.
/// Значения рядов считаются в <see cref="SeriesComputation" />: признак, посчитанный в
/// SQL для обучения и в C# в бою, расходится, и расхождение выглядит на бэктесте как
/// отличная модель, а в бою как случайная.
///
/// Порции пишутся посуточно, по дате конца окна, и идентификатор порции выводится из
/// содержимого. Повторная материализация того же отрезка ничего не удваивает, а
/// перестройка после удаления производного пишет заново то же самое.
/// </summary>
public sealed class SeriesMaterialization(
    IFactRowReader rows,
    ICoverageLog coverage,
    IMaterializationRegistry registry,
    IFactWriter writer)
{
    /// <summary>Считает ряды за интервал, не записывая их.</summary>
    public async Task<SeriesComputed> ComputeAsync(SeriesRequest request, CancellationToken cancellationToken)
    {
        TimeSpan lookback = request.Definitions.Max(static definition => definition.Window);
        var read = TimeRange.Between(request.Interval.From - lookback, request.Interval.To);

        // Горизонт знания — конец интервала: позже него не известно ничего, что могло бы
        // войти хоть в одну точку. Внутри интервала каждая точка отсекается своим концом
        // окна — это уже в домене.
        List<FactRow> events = await ReadAsync(FactSet.OrderEvents, request, read, cancellationToken).ConfigureAwait(false);
        List<FactRow> features = await ReadAsync(FactSet.BookFeatures, request, read, cancellationToken).ConfigureAwait(false);
        List<FactRow> baselines = await ReadAsync(FactSet.OrderBaselines, request, read, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CoverageEntry> entries = await coverage
            .ReadAsync(read, [request.Region], cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<MaterializedInterval> materialized = await registry
            .ReadAsync(FactSet.OrderEvents, cancellationToken)
            .ConfigureAwait(false);

        var inputs = new SeriesInputs(
            request.Region,
            [.. events.Select(LakeFacts.Event)],
            [.. features.Select(row => SeriesFactRows.Features(row, request.Thresholds))],
            request.Thresholds,
            entries,
            baselines.Select(static row => row.Observation).ToHashSet(),
            materialized,
            request.Upstream);

        return SeriesComputation.Compute(inputs, request.Definitions, request.Interval);
    }

    public async Task<SeriesMaterializationReport> RunAsync(SeriesRequest request, CancellationToken cancellationToken)
    {
        SeriesComputed computed = await ComputeAsync(request, cancellationToken).ConfigureAwait(false);
        TimeSpan step = request.Definitions.Min(static definition => definition.Step);

        var written = 0;
        var alreadyPresent = 0;

        foreach (IGrouping<DateOnly, SeriesWindowVerdict> day in computed.Windows
            .GroupBy(static window => DateOnly.FromDateTime(window.Window.Range.To.UtcDateTime))
            .OrderBy(static day => day.Key))
        {
            DateTimeOffset first = day.Min(static window => window.Window.Range.To);
            DateTimeOffset last = day.Max(static window => window.Window.Range.To);
            ObservationId observation = SeriesFacts.ObservationFor(request.Region, request.Definitions, first, last);

            FactBatch batch = SeriesFacts.ToBatch(
                request.Region,
                observation,
                day.Key,
                [.. day],
                [.. computed.Points.Where(point => DateOnly.FromDateTime(point.Window.To.UtcDateTime) == day.Key)],
                request.StaticData);

            // Подтверждение описывает отрезок концов окон порции; его начало — партиция
            // порции, поэтому запись покрытия ложится в ту же суточную партицию.
            CoverageEntry confirmation = CoverageEntries.Derived(
                observation, request.Region, TimeRange.Between(first, last + step), SeriesFacts.Source, last);

            FactWriteOutcome outcome = await writer
                .WriteAsync(batch, [confirmation], cancellationToken)
                .ConfigureAwait(false);

            if (outcome == FactWriteOutcome.AlreadyPresent)
            {
                alreadyPresent++;
            }
            else
            {
                written++;
            }
        }

        return new SeriesMaterializationReport(
            computed.Windows.Count,
            computed.Windows.Count(static window => !window.Window.IsAdmitted),
            computed.Windows.Count(static window => window.Window.IsIncomplete),
            computed.Points.Count,
            written,
            alreadyPresent);
    }

    public async Task<List<FactRow>> ReadAsync(
        FactSet set,
        SeriesRequest request,
        TimeRange read,
        CancellationToken cancellationToken)
    {
        var collected = new List<FactRow>();

        await foreach (FactRow row in rows.ReadAsync(set, read, request.Interval.To, cancellationToken).ConfigureAwait(false))
        {
            if (row.Region == request.Region)
            {
                collected.Add(row);
            }
        }

        return collected;
    }
}
