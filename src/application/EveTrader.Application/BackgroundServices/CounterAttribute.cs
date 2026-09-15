namespace EveTrader.Application.BackgroundServices;

/// <summary>
/// Помечает свойство результата цикла как счётчик. База читает такие свойства
/// рефлексией и эмитит <c>worker.{WorkerName}.{name}</c> — воркер не зовёт
/// счётчики руками.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CounterAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
