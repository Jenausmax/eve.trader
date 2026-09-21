using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EveTrader.Application.Diagnostics;

/// <summary>Гистограмма поверх <see cref="Histogram{T}" />.</summary>
internal sealed class MeterHistogram(Histogram<double> histogram, TagList tags) : ITelemetryHistogram
{
    public ITelemetryHistogram WithTag(string key, object? value)
    {
        TagList extended = tags;
        extended.Add(key, value);

        return new MeterHistogram(histogram, extended);
    }

    public void Record(double value) => histogram.Record(value, tags);
}
