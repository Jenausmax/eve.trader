namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Область одной операции: живёт от начала до конца работы и несёт её исход.
/// Исход выставляется явно — область, закрытая без исхода, в трассировке выглядит
/// как операция, о которой ничего не известно.
/// </summary>
public interface IOperationScope : IDisposable
{
    /// <summary>Тег на саму операцию; читается в трассировке рядом с исходом.</summary>
    IOperationScope WithTag(string key, object? value);

    void Succeeded();

    /// <summary>
    /// Отказ: исключение попадает и в статус, и в событие трассировки. Одного статуса
    /// мало — в нём нет ни типа исключения, ни стека.
    /// </summary>
    void Failed(Exception exception);
}
