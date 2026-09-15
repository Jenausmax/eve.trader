using Microsoft.Extensions.DependencyInjection;

namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Контекст одного цикла. Несёт свежий DI-scope: база создаёт его на входе в цикл
/// и закрывает на выходе, поэтому scoped-зависимости не протекают между циклами.
/// </summary>
public sealed class WorkerContext(IServiceProvider services)
{
    public IServiceProvider Services { get; } = services;

    public T GetService<T>()
        where T : notnull => Services.GetRequiredService<T>();
}
