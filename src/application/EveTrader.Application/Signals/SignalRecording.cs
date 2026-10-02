using EveTrader.Application.Facts;
using EveTrader.Domain.Coverage;
using EveTrader.Domain.Facts;
using EveTrader.Domain.Signals;

namespace EveTrader.Application.Signals;

/// <summary>
/// Запись исходов рассмотрения в озеро — посуточно, с подтверждением покрытием.
///
/// Сигнал неизменяем. Повторное порождение того же отрезка тем же набором — та же
/// порция, и запись её не переписывает: прежний сигнал остаётся ровно таким, каким его
/// видел бы оператор. Другой набор параметров даёт другие ключи и ложится рядом.
/// </summary>
public sealed class SignalRecording(IFactWriter writer)
{
    /// <returns>Сколько порций записано и сколько уже было подтверждено.</returns>
    public async Task<SignalRecordingReport> WriteAsync(
        StationTradingParameters parameters,
        StationTradingScope scope,
        IReadOnlyList<StationTradingVerdict> verdicts,
        TimeSpan step,
        DateTimeOffset generatedAt,
        StaticDataVersion staticData,
        CancellationToken cancellationToken)
    {
        var written = 0;
        var alreadyPresent = 0;

        foreach (IGrouping<DateOnly, StationTradingVerdict> day in verdicts
            .GroupBy(static verdict => DateOnly.FromDateTime(verdict.Decision.UtcDateTime))
            .OrderBy(static day => day.Key))
        {
            DateTimeOffset first = day.Min(static verdict => verdict.Decision);
            DateTimeOffset last = day.Max(static verdict => verdict.Decision);
            ObservationId observation = SignalFacts.ObservationFor(scope.Region, parameters.Name, first, last);

            FactBatch batch = SignalFacts.ToBatch(
                scope.Region, observation, day.Key, parameters, [.. day], generatedAt, staticData);

            CoverageEntry confirmation = CoverageEntries.Derived(
                observation, scope.Region, TimeRange.Between(first, last + step), SignalFacts.Source, generatedAt);

            if (await writer.WriteAsync(batch, [confirmation], cancellationToken).ConfigureAwait(false)
                is FactWriteOutcome.AlreadyPresent)
            {
                alreadyPresent++;
            }
            else
            {
                written++;
            }
        }

        return new SignalRecordingReport(written, alreadyPresent);
    }
}
