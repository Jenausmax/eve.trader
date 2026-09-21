namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Точка эмиссии телеметрии по контракту <c>.agents/rules/observability/diagnostics.md</c>:
/// области операций и заранее объявленные инструменты.
///
/// Инструменты берутся отсюда один раз — при сборке модульной диагностики
/// (<see cref="IObservationDiagnostics" />), а не по месту вызова: счётчик, созданный
/// в теле метода, регистрируется новым инструментом на каждом вызове.
/// </summary>
public interface IDiagnosticSource
{
    /// <summary>Имя модуля; попадает в имя <c>Meter</c> и в теги.</summary>
    string ModuleName { get; }

    IOperationScopeBuilder Operation(string name);

    ITelemetryCounter Counter(string name, string unit);

    ITelemetryHistogram Histogram(string name, string unit);

    ITelemetryGauge Gauge(string name, string unit);
}
