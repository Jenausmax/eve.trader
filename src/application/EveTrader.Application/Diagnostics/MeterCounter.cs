using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Счётчик поверх <see cref="Counter{T}" />. Теги лежат в <see cref="TagList" /> —
/// структуре с местом под восемь тегов без выхода в кучу: счётчик зовётся на каждое
/// наблюдение, и словарь тегов на вызов был бы мусором в горячем цикле.
/// </summary>
internal sealed class MeterCounter(Counter<double> counter, TagList tags) : ITelemetryCounter
{
    public ITelemetryCounter WithTag(string key, object? value)
    {
        TagList extended = tags;
        extended.Add(key, value);

        return new MeterCounter(counter, extended);
    }

    public void Add(double value) => counter.Add(value, tags);
}
