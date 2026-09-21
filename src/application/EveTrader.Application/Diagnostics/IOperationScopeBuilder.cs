namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Сборка области операции: теги, гистограмма длительности и счётчик успеха
/// объявляются до начала работы.
///
/// <see cref="RunAsync{T}" /> владеет try/catch: успех — <c>Ok</c>, отказ — записанное
/// исключение и <c>Error</c>, и всегда проброс дальше. Ручная сборка через
/// <see cref="Build" /> остаётся для случаев, где время жизни области не совпадает с
/// одним try/catch, — например когда отмена не должна считаться отказом.
/// </summary>
public interface IOperationScopeBuilder
{
    IOperationScopeBuilder WithTag(string key, object? value);

    /// <summary>Длительность области пишется сюда при её закрытии.</summary>
    IOperationScopeBuilder WithHistogram(ITelemetryHistogram histogram);

    /// <summary>Счётчик успешного завершения; на отказе не растёт.</summary>
    IOperationScopeBuilder WithCounter(ITelemetryCounter counter);

    IOperationScope Build();

    /// <summary>
    /// Выполняет работу внутри области. <paramref name="onError" /> — крючок для
    /// побочных действий отказа (счётчик, тег): вызывается перед пробросом.
    /// </summary>
    Task<T> RunAsync<T>(Func<IOperationScope, Task<T>> body, Action<IOperationScope, Exception>? onError = null);
}
