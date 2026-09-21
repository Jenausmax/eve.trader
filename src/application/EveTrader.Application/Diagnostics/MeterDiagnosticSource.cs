using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Реализация поверх <see cref="Meter" /> и <see cref="ActivitySource" />.
///
/// Инструменты создаются по требованию и переиспользуются: <see cref="Meter" /> не
/// дедуплицирует их сам, а второй инструмент с тем же именем экспортируется как
/// отдельный ряд — то есть метрика молча раздваивается.
///
/// Экспортёра здесь нет и быть не должно: без подписчика инструмент почти бесплатен,
/// и хост, который никуда не шлёт телеметрию, работает так же, просто молча.
/// </summary>
public sealed class MeterDiagnosticSource : IDiagnosticSource, IDisposable
{
    public const string SourceName = "EveTrader";

    private readonly Meter meter;
    private readonly ActivitySource activitySource;
    private readonly ConcurrentDictionary<string, Counter<double>> counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Histogram<double>> histograms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Gauge<double>> gauges = new(StringComparer.Ordinal);

    public MeterDiagnosticSource(string moduleName = SourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        ModuleName = moduleName;
        meter = new Meter(moduleName);
        activitySource = new ActivitySource(moduleName);
    }

    public string ModuleName { get; }

    public IOperationScopeBuilder Operation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ActivityScopeBuilder(activitySource, name);
    }

    public ITelemetryCounter Counter(string name, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new MeterCounter(
            counters.GetOrAdd(name, static (key, state) => state.Owner.CreateCounter<double>(key, state.Unit), (Owner: meter, Unit: unit)),
            default);
    }

    public ITelemetryHistogram Histogram(string name, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new MeterHistogram(
            histograms.GetOrAdd(name, static (key, state) => state.Owner.CreateHistogram<double>(key, state.Unit), (Owner: meter, Unit: unit)),
            default);
    }

    public ITelemetryGauge Gauge(string name, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new MeterGauge(
            gauges.GetOrAdd(name, static (key, state) => state.Owner.CreateGauge<double>(key, state.Unit), (Owner: meter, Unit: unit)),
            default);
    }

    public void Dispose()
    {
        meter.Dispose();
        activitySource.Dispose();
    }
}
