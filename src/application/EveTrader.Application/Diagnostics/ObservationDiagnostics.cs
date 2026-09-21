namespace EveTrader.Application.Diagnostics;

/// <summary>
/// Инструменты наблюдения, объявленные один раз на процесс.
///
/// Объявление в конструкторе, а не по месту вызова: инструмент, созданный в теле метода,
/// регистрируется заново на каждом вызове, и экспорт получает столько рядов, сколько было
/// наблюдений.
/// </summary>
public sealed class ObservationDiagnostics(IDiagnosticSource source) : IObservationDiagnostics
{
    public string ModuleName => source.ModuleName;

    public ITelemetryCounter SourceRequests { get; } = source.Counter("observation.source.requests", "{requests}");

    public ITelemetryCounter SourceBytes { get; } = source.Counter("observation.source.bytes", "By");

    public ITelemetryCounter NotModifiedResponses { get; } =
        source.Counter("observation.source.not_modified", "{responses}");

    public ITelemetryCounter SourceGaps { get; } = source.Counter("observation.source.gaps", "{orders}");

    public ITelemetryHistogram RegionObservationDuration { get; } =
        source.Histogram("observation.region.duration", "ms");

    public ITelemetryHistogram ChangedOrderFraction { get; } =
        source.Histogram("observation.orders.changed_fraction", "1");

    public ITelemetryGauge ErrorBudgetRemaining { get; } =
        source.Gauge("observation.source.error_budget_remaining", "{errors}");

    public IOperationScopeBuilder Operation(string name) => source.Operation(name);

    public ITelemetryCounter Counter(string name, string unit) => source.Counter(name, unit);

    public ITelemetryHistogram Histogram(string name, string unit) => source.Histogram(name, unit);

    public ITelemetryGauge Gauge(string name, string unit) => source.Gauge(name, unit);
}
