using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Реализация поверх <see cref="Meter" /> и <see cref="ActivitySource" />.
/// Счётчики создаются по требованию и переиспользуются: <see cref="Meter" />
/// не дедуплицирует инструменты сам, а воркер зовёт <see cref="Add" /> каждый цикл.
/// </summary>
public sealed class MeterDiagnosticSource : IDiagnosticSource, IDisposable
{
    public const string SourceName = "EveTrader";

    private readonly Meter meter = new(SourceName);
    private readonly ActivitySource activitySource = new(SourceName);
    private readonly ConcurrentDictionary<string, Counter<long>> counters = new(StringComparer.Ordinal);

    public IOperationScope Operation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ActivityOperationScope(activitySource.StartActivity(name));
    }

    public void Add(string counterName, long value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(counterName);

        Counter<long> counter = counters.GetOrAdd(counterName, static (name, owner) => owner.CreateCounter<long>(name), meter);
        counter.Add(value);
    }

    public void Dispose()
    {
        meter.Dispose();
        activitySource.Dispose();
    }

    private sealed class ActivityOperationScope(Activity? activity) : IOperationScope
    {
        public void Succeeded() => activity?.SetStatus(ActivityStatusCode.Ok);

        public void Failed(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            _ = (activity?.AddException(exception));
            _ = (activity?.SetStatus(ActivityStatusCode.Error, exception.Message));
        }

        public void Dispose() => activity?.Dispose();
    }
}
