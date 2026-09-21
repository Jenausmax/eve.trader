using System.Diagnostics;

namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Область операции поверх <see cref="Activity" />. Длительность берётся из
/// <see cref="Stopwatch.GetTimestamp" />, а не из отдельного объекта-секундомера:
/// область создаётся на каждое наблюдение региона.
/// </summary>
internal sealed class ActivityOperationScope(
    Activity? activity,
    ITelemetryHistogram? duration,
    ITelemetryCounter? succeeded) : IOperationScope
{
    private readonly long startedAt = Stopwatch.GetTimestamp();

    public IOperationScope WithTag(string key, object? value)
    {
        _ = activity?.SetTag(key, value);

        return this;
    }

    public void Succeeded()
    {
        _ = activity?.SetStatus(ActivityStatusCode.Ok);
        succeeded?.Add(1);
    }

    public void Failed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _ = activity?.AddException(exception);
        _ = activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    public void Dispose()
    {
        duration?.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        activity?.Dispose();
    }
}
