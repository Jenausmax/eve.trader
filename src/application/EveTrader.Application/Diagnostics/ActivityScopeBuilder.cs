using System.Diagnostics;

namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Сборка области поверх <see cref="ActivitySource" />. Теги накапливаются до старта
/// <see cref="Activity" />: тег, поставленный после старта, не попадёт в сэмплирующее
/// решение экспортёра.
/// </summary>
internal sealed class ActivityScopeBuilder(ActivitySource source, string name) : IOperationScopeBuilder
{
    private readonly List<KeyValuePair<string, object?>> tags = [];

    private ITelemetryHistogram? duration;

    private ITelemetryCounter? succeeded;

    public IOperationScopeBuilder WithTag(string key, object? value)
    {
        tags.Add(new KeyValuePair<string, object?>(key, value));

        return this;
    }

    public IOperationScopeBuilder WithHistogram(ITelemetryHistogram histogram)
    {
        duration = histogram;

        return this;
    }

    public IOperationScopeBuilder WithCounter(ITelemetryCounter counter)
    {
        succeeded = counter;

        return this;
    }

    public IOperationScope Build() =>
        new ActivityOperationScope(
            source.StartActivity(name, ActivityKind.Internal, parentContext: default, tags),
            duration,
            succeeded);

    public async Task<T> RunAsync<T>(
        Func<IOperationScope, Task<T>> body,
        Action<IOperationScope, Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        using IOperationScope scope = Build();

        try
        {
            T result = await body(scope).ConfigureAwait(false);
            scope.Succeeded();

            return result;
        }
        catch (Exception exception)
        {
            scope.Failed(exception);
            onError?.Invoke(scope, exception);

            throw;
        }
    }
}
