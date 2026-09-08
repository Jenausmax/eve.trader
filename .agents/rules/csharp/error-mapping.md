---
description: error boundary — providers throw typed ProviderException, one handler maps to ProblemDetails; throw for invariant + infra failure, nullable<T> for expected miss
globs: ["**/*.cs"]
always: true
---

# Error mapping (upstream → endpoint)

For a proxy/gateway service, **translating upstream failures into stable client
responses is core logic**, not an afterthought. One boundary contract, end to end.

## 1. The contract

```
upstream (backend) failure / mapping failure
   │  provider catches, wraps
   ▼
ProviderException (typed, stable Code + inner)
   │  one exception handler maps
   ▼
ProblemDetails (RFC 9457 — status + title + code)
```

- **Providers** own *upstream → typed exception*. They never leak
  `HttpRequestException` / `RefitException` / raw upstream JSON past their boundary.
- **The host** owns *typed exception → ProblemDetails* via a single
  `IExceptionHandler` (`problem-details.md`) — not try/catch in every endpoint.

## 2. Typed exceptions

| Exception | Meaning | Maps to |
|-----------|---------|---------|
| `ProviderException(code, message, inner?)` | base — network / mapping / auth failure | **502** |
| `ProviderTimeoutException` | upstream timed out | **504** |
| `ProviderNotFoundException` | resource absent upstream | **404** |

`Code` is stable dot.case (`provider.network_error`, `provider.timeout`,
`order.not_found`) — clients branch on `Code`, never on `Message`.

## 3. Expected misses vs invariants

Domain factories throw `IdentityException` (or `DomainException`) for invariant
violations; `Nullable<T>` handles "expected miss" cases (e.g. `T? FindById(...)`).
No `Result<T>` in user code — verbose, no value, clashes with `throw` discipline.

## 4. Status-code map

| Situation | HTTP |
|-----------|------|
| `ProviderNotFoundException` / null result | 404 |
| Validation (FluentValidation filter) | 400 |
| Semantic / unprocessable | 422 |
| `ProviderTimeoutException` | 504 |
| `ProviderException` (network / upstream 5xx / mapping) | 502 |
| Unhandled | 500 |

Log the full exception with `Code`; return only `title` + `detail` + `code` —
never a stack trace (`exceptions.md` §4).

## 5. Translate at the boundary — don't leak `EnsureSuccessStatusCode`

```csharp
// ❌ Bad — raw HttpRequestException escapes; the handler can't map it deterministically
response.EnsureSuccessStatusCode();

// ✅ Good — translate to a typed exception the handler understands
if (response.StatusCode is HttpStatusCode.NotFound)
{
    throw new ProviderNotFoundException("order.not_found", $"order {orderId} not found");
}
if (!response.IsSuccessStatusCode)
{
    throw new ProviderException("provider.network_error", $"upstream returned {(int)response.StatusCode}");
}
```

## Related

- `problem-details.md` — the shape + the one handler
- `exceptions.md` — throwing/catching discipline
- `http-resilience-refit.md` — where timeouts/retries originate
