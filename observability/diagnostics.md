---
description: when and how to add opentelemetry diagnostics (traces + metrics) to a module, engine worker, or cqrs handler. module-owned diagnostic interface pattern, naming, tags, anti-patterns.
globs: ["**/*.cs", "**/installers/**", "**/telemetry/**"]
always: true
---

# Diagnostics — when to add spans + metrics

The OTel abstraction is in `<Shared>.Telemetry` (Contracts / Null / OpenTelemetry
/ Testing). Every project gets a `IDiagnosticSource` + optional module-owned
`IXxxDiagnostics : IDiagnosticSource` with pre-declared counters / histograms.

## Decision: when is a module-owned diagnostics required?

A module-owned diagnostics interface (e.g. `ITenantsDiagnostics`,
`IEngineDiagnostics`) is **required** when a project emits any of the following:

1. **Hosted service / BackgroundService** that runs a long-lived loop
   (IngestJobRunner, AssemblyWorker, OutboxRelayWorker).
2. **Hot-path CQRS handler** that runs on every command (CreateAdvertiser,
   RenameCampaign, etc.) — covered by the kernel `TelemetryBehavior` (auto),
   but module-level counters are still required for custom signals
   (e.g. `AdvertisersCreated` for business reporting).
3. **External integration** with side effects (HTTP call to a DSP / exchange /
   billing provider) — span around the call + counter for success/fail.
4. **Bulk data flow** (ingest job, batched assembly, pre-aggregation) — histogram
   for per-record duration, counter for processed / failed.

A module-owned diagnostics is **NOT** required for:

- Pure read-only query handlers with no side effects beyond EF Core
  (EF Core instrumentation already emits `db.statement` spans).
- Simple CRUD endpoints that just call a `Repository.AddAsync`.
- One-off startup tasks.

When in doubt: add a `IXxxDiagnostics` with one or two counters. The cost of
an unused counter is zero (it's just a `Counter<T>` declaration); the cost of
a missing counter is "why is this metric zero in Grafana?".

## Pattern — module-owned diagnostics

```
src/modules/<M>/<M>.Application/
├── Telemetry/
│   ├── I<M>Diagnostics.cs         # interface: pre-declared metrics
│   └── <M>Diagnostics.cs          # OTel impl, singleton
```

### Interface (one file, public)

```csharp
public interface ITenantsDiagnostics : IDiagnosticSource
{
    public ITelemetryCounter AdvertisersCreated { get; }
    public ITelemetryHistogram AdvertiserCreationDuration { get; }
}
```

### Implementation (one file, sealed, primary constructor)

```csharp
public sealed class TenantsDiagnostics(
    ModuleTelemetryRegistration registration,
    IDiagnosticSource diagnosticSource) : ITenantsDiagnostics
{
    private readonly IDiagnosticSource @base = diagnosticSource;
    public string ModuleName => "Tenants";

    public ITelemetryCounter AdvertisersCreated { get; } =
        new OpenTelemetryCounter(
            registration.Meter.CreateCounter<double>(
                "tenants.advertisers.created", unit: "{advertisers}"));

    public ITelemetrySpan StartSpan(string name) => @base.StartSpan(name);
    public ITimedScope Timed(string metricName) => @base.Timed(metricName);
    public IOperationScopeBuilder Operation(string name) => @base.Operation(name);
}
```

### Registration (in module's `*ApplicationInstaller`)

```csharp
public static IServiceCollection AddTenantsApplicationCore(this IServiceCollection services)
{
    // Telemetry — registers the Tenants ActivitySource + Meter name with the
    // static ModuleTelemetryRegistry so the Host's OTel pipeline can pick it up.
    services.AddModuleTelemetry("Tenants");
    services.AddSingleton<ITenantsDiagnostics, TenantsDiagnostics>();
    // ... handlers, validators ...
}
```

**Order matters**: `AddModuleTelemetry(name)` must be called before any
`IXxxDiagnostics` registration that depends on `ModuleTelemetryRegistration`.

## Naming conventions

### Metric names

`{module}.{noun}.{verb-ed-or-quantity}`

| Pattern | Example |
|---|---|
| Counter — events occurred | `tenants.advertisers.created` |
| Counter — boolean state | `engine.affected_sets.empty` |
| Histogram — duration (ms) | `engine.assembly.duration` |
| Histogram — size | `reporting.clickhouse.rows_scanned` |

Unit suffix (`{banners}`, `{ms}`) — set in the instrument declaration, not the name.

### Tag keys

- `event.type` — domain event class name (e.g. `CampaignCreated`)
- `job.name` — ingest job name
- `command.name` — CQRS command type name
- `command.result` — `ok` | `failed` | `cancelled` | `short_circuited`.
  MCP-шлюз (`mcp.tools.invoked`) добавляет два значения отказа по правилам:
  `rejected` — консоль ответила 4xx (валидация, RBAC, нет объекта, конфликт);
  `bad_input` — шлюз сам не разобрал аргументы модели, до консоли не дошло.
  Оба НЕ аварии: сервис цел, будить дежурного не надо.
- `console.status` — HTTP-статус, которым ответила консоль (число). Ставится на
  `mcp.tools.invoked` при `command.result=rejected` и при `failed`, если ответ
  вообще был; отсутствие тега при `failed` значит «ответа не было» (консоль
  недоступна, таймаут). Кардинальность ограничена набором кодов HTTP.
- `status` — generic outcome tag (when command.result doesn't apply)
- `error.type` — exception type name on failure

### Счётчик ошибок считает только аварии

Счётчик `*.errors` модуля поднимается только на поломке системы. Отказ по
правилам — 4xx от вышестоящего сервиса, отвергнутый ввод — поломкой не является:
он идёт в счётчик ВЫЗОВОВ отдельным значением `command.result` и, когда статус
известен, с тегом `console.status`. Смешивать нельзя: счётчик, который растёт от
чужих 403, бесполезен для тревоги.

Для MCP-шлюза `mcp.tools.errors` считает: 5xx консоли
(`error.type=http_{status}`), недоступность консоли и таймаут
(`error.type=upstream_unreachable`), необработанное исключение
(`error.type={ИмяТипаИсключения}`) и `unclassified` — провал, до которого не
доехал статус (дефект шлюза, а не чужой отказ). 4xx, `bad_input` и отмена
клиентом сюда не попадают: оборванный клиентом вызов виден как
`command.result=cancelled` на счётчике вызовов, но сервис при этом цел.

Статус спана следует тому же делению, что у HTTP-сервера: 4xx — не ошибка
сервера (`Ok`), 5xx и отсутствие ответа — `Error`. `RecordException` на этом пути
неприменим: исключения нет, вышестоящий сервис ответил.

Do NOT tag with `user.id` / `workspace.id` / PII — use `tenant.id` or
`workspace.id` only when cardinality is bounded (≤ 1000 unique values).

### Span names

`{verb}.{subject}` — short, lowercase, dot-separated.

| Span | Example |
|---|---|
| Operation (CQRS) | `cqrs.CreateAdvertiserCommand` |
| Operation (engine) | `engine.assembly`, `engine.ingest.stats_ingest` |
| External call | `http.upsert_batch` (DSP client) |

## Usage in business code

### In a CQRS handler

The kernel `TelemetryBehavior` already wraps every command in a span +
histogram + counter. Module-owned diagnostics are for **additional business
signals**, not for command-level tracing:

```csharp
public sealed class CreateAdvertiserHandler(
    ITenantRepository repository,
    ITenantsDiagnostics diagnostics) : ICommandHandler<CreateAdvertiserCommand, AdvertiserId>
{
    public async ValueTask<AdvertiserId> HandleAsync(
        CreateAdvertiserCommand command, CancellationToken cancellationToken = default)
    {
        var advertiser = Advertiser.Create(/* ... */);
        await repository.AddAdvertiserAsync(advertiser, cancellationToken);
        diagnostics.AdvertisersCreated.WithTag("workspace.id", command.WorkspaceId.Value).Add(1);
        return advertiser.Id;
    }
}
```

### In a BackgroundService — prefer `RunAsync`

`IOperationScopeBuilder.RunAsync` owns the try/catch: it sets `Ok` on success and
records the exception + `Error` on failure automatically. Manual `using` +
`SetStatus(Ok)` + `SetStatus(Error, ...)` is **legacy** — only reach for it when
the scope lifetime does not line up with a single try/catch.

```csharp
public sealed class IngestJobRunner(
    /* ... */,
    IEngineDiagnostics diagnostics) : BackgroundService
{
    private async Task ExecuteOneAsync(IngestJobDescriptor descriptor, CancellationToken ct)
    {
        await diagnostics.Operation($"engine.ingest.{descriptor.Name}")
            .WithHistogram(diagnostics.IngestJobDuration)
            .WithTag("job.name", descriptor.Name)
            .RunAsync(
                async op =>
                {
                    await job.ExecuteAsync(ctx, ct);
                    op.WithTag("status", "ok");
                    diagnostics.IngestJobsSucceeded.WithTag("job.name", descriptor.Name).Add(1);
                },
                onError: (op, ex) =>
                {
                    op.WithTag("status", "failed");
                    diagnostics.IngestJobsFailed.WithTag("job.name", descriptor.Name).Add(1);
                });
    }
}
```

`RunAsync` **always rethrows** — it never swallows. To isolate one failing unit
from a loop (e.g. "one bad job must not crash the runner"), wrap the `RunAsync`
call in its own try/catch and swallow there. `onError` is the hook for
failure-only side effects (failure counter, log, status tag); it runs **before**
the rethrow.

Per-row/per-item swallowing that happens **inside** the happy path (e.g. an
outbox relay that logs+skips a poison row but keeps draining the batch) stays
inside the `RunAsync` body — the operation still ends `Ok` because the batch
itself succeeded.

## Anti-patterns

| Anti-pattern | Why | Fix |
|---|---|---|
| `diagnostics.Counter.Add(1, new Dictionary<string, object?> { [...] })` | `ITelemetryCounter.Add(double)` has no tags overload | `counter.WithTag("k", v).Add(1)` |
| Manual `using var op = Operation(...).Build()` + `try/catch` + `SetStatus(Ok)` / `SetStatus(Error, ...)` | Boilerplate; easy to forget status on one branch; `RecordException` (which writes the exception event to the trace) is missed | Use `Operation(...).RunAsync(body, onError)` — it sets `Ok`/records the exception + `Error` for you |
| `SetStatus(TelemetryStatus.Error, ex.Message)` in a catch instead of `RecordException(ex)` | Loses the exception type + stack from the trace (status alone carries no stack) | `op.RecordException(ex)` — sets `Error` **and** records the exception event in one call. `RunAsync` does this automatically |
| A hand-rolled `Stopwatch` field feeding a duration histogram | Allocates an object; duplicates work the scope already does | Bind the histogram via `.WithHistogram(h)` on the operation builder — the scope records elapsed ms on dispose (allocation-free `Stopwatch.GetTimestamp`) |
| `using var span = diagnostics.StartSpan("op"); ...` with no status set | Span ends as Unset on dispose — looks like a no-op in trace UI | Wrap in `Operation(...).RunAsync(...)`, or set `Ok`/`RecordException` explicitly |
| Calling `IDiagnosticSource` directly in business code | Bypasses module's pre-declared metrics | Inject `IXxxDiagnostics` and use its properties |
| Creating a new `Counter<T>` inside a method body | Counter is registered as a new instrument on every call | Declare it once on the `IXxxDiagnostics` and inject |
| Tagging with high-cardinality values (`user.id`, `banner.id`) | OOMs the OTel collector | Aggregate by `workspace.id` (bounded) or drop the tag |
| `Operation("...").WithCounter(c1).WithCounter(c2).Build()` | Both counters increment by 1 unconditionally on dispose | Use `WithCounter` for the success path only; add the fail counter in `onError` |
| `using var x = ...` and forget to dispose | Span never ends, activity leaks | Always `using var` (or let `RunAsync` own the scope); rely on dispose for auto-close |
| `SetStatus(TelemetryStatus.Ok)` on a failed operation | Trace UI shows green for a red outcome | Only set `Ok` on actual success |

## Unit testing diagnostics

Use `TestDiagnosticSource` (in `<Shared>.Telemetry.Testing`) to assert
emitted spans / counters in unit tests:

```csharp
public sealed class CreateAdvertiserHandlerShould
{
    [Fact]
    public async Task IncrementAdvertisersCreatedCounter()
    {
        var source = new TestDiagnosticSource { ModuleName = "Tenants" };
        var diagnostics = new TenantsDiagnostics(/* fake registration */, source);
        // ... call handler ...
        diagnostics.AdvertisersCreated.Total.ShouldBe(1);
    }
}
```

The full `TestDiagnosticSource` API:
- `source.Spans` — `ConcurrentBag<TestSpan>` of all started spans.
- `source.FindSpan("name")` — find span by name (exact or suffix).
- `source.GetCounter("name")` / `GetHistogram("name")` — get or create a recorder.
- `TestCounter.Total` / `Count` / `Calls` — assert counter values.
- `TestSpan.Tags` / `Events` / `Status` / `StatusDescription` / `RecordedException`.

## Related

- `<Shared>.Telemetry` — abstraction (Contracts / Null / OpenTelemetry / Testing).
- `<Host>.Telemetry.TelemetryInstaller` — Host-side OTel SDK wiring.
- `<Shared>.Kernel.Cqrs.Behaviors.TelemetryBehavior` — auto-instrumented
  CQRS pipeline (always innermost behavior in the default pipeline).
- `.claude/skills/telemetry-author` — helper skill for adding diagnostics to a
  new module or worker.
