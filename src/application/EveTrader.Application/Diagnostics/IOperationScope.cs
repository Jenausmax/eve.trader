namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Область одной операции: живёт от начала до конца работы и несёт её исход.
/// Исход выставляется явно — область, закрытая без исхода, в трассировке выглядит
/// как операция, о которой ничего не известно.
/// </summary>
public interface IOperationScope : IDisposable
{
    void Succeeded();

    void Failed(Exception exception);
}
