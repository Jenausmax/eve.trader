using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EveTrader.Application.Diagnostics;

/// <summary>Датчик поверх <see cref="Gauge{T}" />: экспортируется последнее записанное значение.</summary>
internal sealed class MeterGauge(Gauge<double> gauge, TagList tags) : ITelemetryGauge
{
    public ITelemetryGauge WithTag(string key, object? value)
    {
        TagList extended = tags;
        extended.Add(key, value);

        return new MeterGauge(gauge, extended);
    }

    public void Record(double value) => gauge.Record(value, tags);
}
