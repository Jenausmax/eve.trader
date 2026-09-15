namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Точка эмиссии телеметрии. Контракт умышленно узкий: ровно то, что нужно
/// <see cref="BackgroundServices.ScheduledWorkerBase" />. Полный контракт из
/// <c>.agents/rules/observability/diagnostics.md</c> добирается в
/// <c>add-pipeline-acceptance</c>, где по метрикам начинают принимать решения.
/// </summary>
public interface IDiagnosticSource
{
    IOperationScope Operation(string name);

    void Add(string counterName, long value);
}
