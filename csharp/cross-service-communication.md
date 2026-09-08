---
description: >-
  cross-service communication between deploy-apps of one solution — Refit HTTP
  contract in the shared contracts assembly, no peer infrastructure in the
  caller, W3C trace + metrics per call, IApiResponse<T> failure semantics,
  mandatory resilience handler.
globs:
  - src/**/I*Api.cs
  - src/**/Refit*.cs
  - src/**/Ports/**/I*.cs
priority: high
interactive: false
always: false
---

# Cross-service communication (host ↔ host)

> **Scope**: synchronous calls **between** deploy-apps of the same solution
> (`<Solution>.Host` → `<Solution>.Host.<Peer>`). Integrations with **external**
> vendors live in dedicated adapter modules and are not covered here.
>
> Every cross-service call MUST go through the public contract in the shared
> contracts assembly — never a raw HTTP client, never a private in-process call
> into a peer's module.

## When this rule applies

- Adding a new Refit interface for a peer host (e.g. `IReportingApi`, `IAuditApi`).
- Calling an existing peer host from any module.
- Wiring the HTTP client in DI (base address, resilience handler, diagnostics).
- Designing the request/response shape for a peer contract.
- Debating whether business logic belongs in the caller module or the peer.

## Pattern (canonical)

```
   Caller module                   Peer host
   ─────────────                   ──────────
   public sealed class FooService(
       IFooApi foo,            ←── Refit-generated proxy
       ILogger<FooService> logger)
   {
       public async Task<X> DoIt(...)
       {
           var response = await foo.PostSomethingAsync(req, ct);
           return response.IsSuccessStatusCode
               ? response.Content
               : /* fallback / exception */;
       }
   }
```

DI registration (in the shared composition, **one** per contract):

```csharp
services.AddHttpClient<IFooApi, FooApiClient>(static (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<FooApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
})
.AddStandardResilienceHandler();   // Polly: retry + circuit breaker + timeout

public sealed class FooApiOptions
{
    public const string SectionName = "Foo";
    public string BaseUrl { get; set; } = "http://localhost:5100";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
```

**Caller NEVER** builds an `HttpClient` directly; **caller NEVER** injects the
peer's `DbContext` / query executor / any other infrastructure type. The contract
is the only surface.

## Rules

### 1. The contract lives in the shared contracts assembly

Every cross-service contract is a `public interface` in
`src/shared/<Solution>.Shared.Contracts/...` (usually a `<Domain>/` subfolder).
Caller module and peer module both depend on the same contract assembly. **No**
module owns the contract. Caller-side DI registers the Refit-generated client in
a dedicated `Installers/*ClientExtensions.cs`.

```csharp
// Refit attributes on the contract, not on the impl — the Refit source generator
// creates the proxy from the contract alone.
public interface IReportingApi
{
    [Get("/api/v1/reporting/campaigns/{campaignId}/daily")]
    public Task<IApiResponse<StatsReport>> GetCampaignDailyAsync(
        string campaignId, [AliasAs("from")] DateOnly from, [AliasAs("to")] DateOnly to,
        CancellationToken cancellationToken = default);
}
```

### 2. The caller never sees the peer's infrastructure

A caller does **not** inject the peer's query executor, rendered-query types,
table descriptors, or any `DbContext`. The query goes through the public
contract — full stop. SQL strings are owned by the peer; the caller never writes
them. This is the same rule as `di-lifetimes.md` §"Layer dependencies", but for
**cross-host** instead of **cross-module**.

If a caller needs data that isn't in the public contract → add the method to the
contract first, then implement it in the peer. No bypassing.

### 3. Auth between peers — a project decision

<!-- ПРОЕКТНОЕ РЕШЕНИЕ: конкретный ответ объявляется в правилах проекта. -->

The auth model between peers depends on the deployment topology and MUST be
declared by the project, not improvised per call site:

- **Closed internal network** (no public ingress on peer hosts, auth at the
  edge) → peer-to-peer calls are unauthenticated. Do **not** add JWT / API-key /
  mTLS at the inter-service layer "just in case".
- **Any peer reachable from outside** → peer auth is mandatory.

When auth is added later, the migration is:
1. A new `IAuthenticatedPeerClient` marker interface in the shared contracts.
2. A per-host service account in the identity module (an existing API-key scheme
   is usually enough; no new auth module).
3. `AddHttpClient<IFooApi, FooApiClient>` gains
   `.AddHttpMessageHandler(sp => new ApiKeyHandler(...))` — the *contract* is
   unchanged.
4. The peer host reads its own service-account key from its config and validates
   the inbound header.
5. Test matrix: 2xx with key, 401 without key, 401 with wrong key, 503 when the
   peer is down.

### 4. Versioning — lock-step deploy unless the project says otherwise

<!-- ПРОЕКТНОЕ РЕШЕНИЕ: зависит от того, бывают ли одновременно живы две версии пира. -->

When only one version of a peer runs per environment, inter-service contracts
need no `/api/v2/` URL versioning. A breaking change is then:

- update the contract in the shared contracts assembly;
- update the caller module(s);
- update the peer host;
- deploy in lock-step (peer first, then caller, same release window);
- remove the old contract in the same release.

If mixed-version deployment is ever possible (rolling upgrade), the change is
additive instead: new optional field / new method → both shapes coexist → switch
callers atomically → remove the old shape. The user-facing HTTP surface follows
`api-design.md` §versioning; the principle is identical.

### 5. W3C trace + metrics per call

Every cross-service call MUST be observable:

```csharp
public sealed class FooService(
    IFooApi foo,
    IPeerDiagnostics diagnostics)
{
    public async Task<X> DoIt(FooRequest req, CancellationToken ct)
    {
        using var op = diagnostics.Operation("foo.peer.call")
                .WithTag("peer", "host.Foo")
                .WithTag("operation", "PostSomething")
                .WithHistogram(diagnostics.CallDuration)
                .Build();
        try
        {
            var response = await foo.PostSomethingAsync(req, ct);
            var outcome = response.IsSuccessStatusCode ? "success" : "failed";
            op.WithTag("outcome", outcome);
            diagnostics.CallCount.WithTag("peer", "host.Foo")
                                 .WithTag("outcome", outcome)
                                 .Add(1);
            return response.IsSuccessStatusCode
                ? response.Content
                : throw new PeerUnavailableException("foo", response.StatusCode);
        }
        catch (Exception ex)
        {
            op.RecordException(ex);
            throw;
        }
    }
}
```

The OTel `ActivitySource` registered in the shared telemetry project already
propagates W3C `traceparent` + `tracestate` over the Refit-generated
`HttpClient` — no extra config needed. Per `observability/diagnostics.md`,
**every component that talks to a peer MUST emit a counter with `peer` and
`outcome` tags**. If existing code emits a different tag set — migrate it, don't
skip the `peer` tag.

### 6. Failure semantics: `IApiResponse<T>`, not `T`

The Refit method signature is **`Task<IApiResponse<T>>`**, not `Task<T>`. The
default Refit signature throws `ApiException` on non-2xx, which crashes a worker
loop. The caller must **inspect the status** and degrade gracefully (skip, retry
later, surface a metric):

```csharp
var response = await foo.PostSomethingAsync(req, ct);
if (response.IsSuccessStatusCode) { return response.Content; }
diagnostics.CallCount.WithTag("peer", "host.Foo").WithTag("outcome", "http_error").Add(1);
logger.LogWarning("peer foo returned {StatusCode}: {Error}", response.StatusCode, response.Error?.Content);
// Decide: rethrow, return null, or throw a domain-specific exception
throw new PeerUnavailableException("foo", response.StatusCode);
```

`PeerUnavailableException` lives in the shared kernel (or a dedicated shared
communication project); a host may subclass it with a richer payload.

### 7. Resilience: `AddStandardResilienceHandler` is mandatory

Every peer-client registration includes Polly's standard resilience handler:

```csharp
services.AddHttpClient<IFooApi, FooApiClient>(...)
    .AddStandardResilienceHandler();
```

The default `Microsoft.Extensions.Http.Resilience` policy covers retry (3
attempts, exponential backoff with jitter) + circuit breaker (5 failures in 30s
opens for 30s) + total request timeout (10s default). **Do not** customise these
without a documented reason in the PR.

`AddStandardResilienceHandler` is applied AFTER `BaseAddress` configuration; the
handler is per-client, not global — two clients keep independent retry / breaker
state.

### 8. Test pattern (unit, no real host)

Every cross-service caller has a unit test that **fakes the Refit client via
NSubstitute**, not a real HTTP server: `Substitute.For<IFooApi>()` whose method
returns an `IApiResponse<T>` built for the status under test.

For integration tests:
- spin up the peer host in-process via `WebApplicationFactory<Program>`;
- use the real Refit client pointed at `factory.CreateClient()`;
- test 4xx / 5xx / timeout scenarios;
- keep them out of the unit suites (`testing-integration.md`).

## Anti-patterns (rejected by this rule)

| Anti-pattern | Why rejected |
|--------------|--------------|
| Caller injects the peer's query executor / `DbContext` / rendered-query type | Breaks the "no SQL in the caller" rule. Per-host infrastructure stays in the host. |
| Caller builds a `new HttpClient()` for a peer | Bypasses the Refit proxy and the resilience handler. Always `AddHttpClient<IFooApi, FooApiClient>`. |
| Caller adds `services.AddHttpClient(...)` for a raw URL | The Refit proxy is the contract; raw URLs are not. Need a one-off endpoint? Add a method to the contract first. |
| Caller adds an auth header by hand | Peer auth is a declared project decision (§3), not a per-call-site improvisation. |
| Caller uses `IApiResponse.Error.Content` as a happy path | Treat non-2xx as failure. `Error` is for logging only. |
| Caller catches `ApiException` from a `Task<T>` method | Use the `IApiResponse<T>` signature. Catching exceptions is for **infrastructure** errors (DNS, TLS, timeout) only. |
| Caller hard-codes `http://localhost:5100` | Use `IOptions<FooApiOptions>` bound from configuration. |
| Caller adds `cancellationToken.ThrowIfCancellationRequested()` on top of passing `ct` | `ct` already propagates cancellation; the manual check duplicates it. |
| Caller adds cross-service tracing via a custom `ActivitySource` instead of the `Operation` scope | The `Operation` scope already wraps `StartActivity` + `RecordException` + the standard spans. |
| Caller registers the same peer client twice (once per module) | **One** registration per contract, in shared composition. Modules consume the contract, not the registration. |

## What is **not** covered by this rule

| Concern | Covered by |
|---------|-----------|
| User-facing HTTP API (controllers, OpenAPI) | `api-design.md` |
| DI lifetimes and per-request scope | `di-lifetimes.md` |
| `IOptions<T>` pattern and validation | `di-options.md` |
| Outbox events (kernel-to-kernel via DB, not HTTP) | `anti-patterns.md` §Outbox + the project's outbox design |
| External vendor integrations | a dedicated adapter module + `http-resilience-refit.md` |
| In-process module-to-module (same host) | `di-lifetimes.md` + the project's layer rules |
| Async messaging (Kafka, RabbitMQ) | not covered — outbox only until a project adds a rule |

## Enforcement

- **Code review**: reject any change that introduces a raw `HttpClient` for a
  peer host, a private peer call (registering a peer's `DbContext` in a non-peer
  module), or a hand-rolled `Task<HttpResponseMessage>` instead of a Refit
  interface.
- **Architecture test**: search for `AddHttpClient<` in module projects and
  assert the type parameter is a `public interface` from the shared contracts
  assembly, not from the module's own namespace.

## See also

- `di-lifetimes.md` §"HTTP clients" — base pattern (this rule is a strict superset)
- `http-resilience-refit.md` — Refit registration + resilience for outbound HTTP
- `api-design.md` — user-facing controller design (related but different scope)
- `observability/diagnostics.md` — per-component diagnostics surface
