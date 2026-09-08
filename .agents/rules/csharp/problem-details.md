---
description: problem details — RFC 9457 for every error response, one IExceptionHandler maps typed exceptions, stable type/title/code, never leak internals or return 200-with-error
globs: ["**/*.cs"]
always: true
---

# ProblemDetails (RFC 9457)

Every error response is `application/problem+json`. `error-mapping.md` owns
*which* status; this owns the **shape** + wiring.

## 1. One handler, not per-endpoint try/catch

Register `AddProblemDetails()` + a single `IExceptionHandler` (or exception
middleware) that maps typed exceptions → ProblemDetails. Endpoints stay clean of
error plumbing.

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProviderExceptionHandler>();
// ...
app.UseExceptionHandler();
```

## 2. Shape

| Member | Content |
|--------|---------|
| `type` | stable URI/urn per error class (not `about:blank` for known errors) |
| `title` | short, stable, human |
| `status` | per `error-mapping.md` table |
| `detail` | safe human message — **no** stack / secret / PII (`exceptions.md` §4) |
| `code` (extension) | the machine `ProviderException.Code` / `Error.Code` |

Build with `TypedResults.Problem(...)` / `TypedResults.ValidationProblem(...)` —
never hand-write the JSON.

```csharp
return TypedResults.Problem(
    title: "Upstream unavailable",
    detail: "the upstream service did not respond",
    statusCode: StatusCodes.Status502BadGateway,
    extensions: new Dictionary<string, object?> { ["code"] = "provider.network_error" });
```

## 3. Validation → ValidationProblem (400)

The request-validation filter returns `TypedResults.ValidationProblem(errors)` —
a ProblemDetails with the per-field `errors` dictionary. Never `ModelState`
plumbing, never a custom error envelope.

## 4. Upstream failures

Map to 502/503/504 with a `code` (`provider.network_error`, `provider.timeout`);
`detail` says "upstream unavailable", never the raw upstream body.

## Anti-patterns / self-audit

```csharp
❌ return Results.Json(new { error = "..." });   // ad-hoc envelope, not problem+json
❌ detail: ex.ToString()                          // leaks stack/internals
❌ 200 OK with an error-shaped body               // errors carry an error status
```

```bash
rg -n 'new \{ error' src/ --type cs           # anonymous error envelopes
rg -n 'ex\.(ToString|StackTrace)' src/ --type cs
```

## Related

- `error-mapping.md` — exception → status
- `exceptions.md` — no PII in `detail`
