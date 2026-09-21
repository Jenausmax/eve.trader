using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Подписчик на инструменты процесса — тот самый экспорт, без которого метрика есть
/// только на бумаге.
///
/// Полноценного экспортёра OTel здесь нет намеренно: коллектора, куда слать, у проекта
/// пока нет, а объявить метрику и не суметь её прочитать — ровно тот случай, ради
/// которого этот тип и заведён. Оператор видит числа после прогона, тест — в
/// утверждении.
/// </summary>
public sealed class MetricExport : IDisposable
{
    private readonly MeterListener listener = new();

    private readonly ConcurrentDictionary<string, MetricTally> tallies = new(StringComparer.Ordinal);

    public MetricExport(string meterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meterName);

        listener.InstrumentPublished = (instrument, subscriber) =>
        {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                subscriber.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
                tallies.GetOrAdd(instrument.Name, static _ => new MetricTally()).Record(measurement));

        listener.Start();
    }

    /// <summary>Инструменты, по которым прошёл хотя бы один замер, по алфавиту.</summary>
    public IReadOnlyList<string> Names => [.. tallies.Keys.Order(StringComparer.Ordinal)];

    public MetricTally? Of(string name) => tallies.GetValueOrDefault(name);

    public double TotalOf(string name) => Of(name)?.Total ?? 0d;

    public long CountOf(string name) => Of(name)?.Count ?? 0L;

    public double LastOf(string name) => Of(name)?.Last ?? 0d;

    public void Dispose() => listener.Dispose();
}
