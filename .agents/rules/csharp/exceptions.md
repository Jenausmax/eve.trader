---
description: exceptions — not for control flow, no catch-all except the top-level handler, one exception hierarchy, no PII in messages, rethrow correctly
globs: ["**/*.cs"]
always: true
---

# Exceptions

`error-mapping.md` owns the boundary contract (upstream failure → typed
exception → ProblemDetails). This is the general throwing/catching discipline.

## 1. Not for control flow

Exceptions are for exceptional, non-local failure — not expected branches.
Expected outcomes are values: pattern match / `TryParse` / `T?` (`code-shape.md` §1).

```csharp
// ❌ Wrong — exception as a branch
try { return Parse(s); } catch (FormatException) { return Default; }

// ✅ Correct — expected outcome is a value
return TryParse(s, out var v) ? v : Default;
```

## 2. No catch-all except the top-level handler

`catch (Exception)` is allowed **only** in the single composition-root
`IExceptionHandler` / exception middleware that maps to ProblemDetails
(`problem-details.md`). Everywhere else catch the specific type you can act on.
**Never** an empty `catch {}` or a catch-log-swallow that hides a bug.

```csharp
// ✅ Boundary-narrow — translate the one thing this layer expects
catch (HttpRequestException ex)
{
    throw new ProviderException("provider.network_error", "upstream unreachable", ex);
}
```

Documented exception: a best-effort per-item skip (e.g. one malformed NDJSON line)
catches narrow, logs/drops, keeps draining — the batch still succeeds
(`json-and-ndjson.md` §4).

## 3. One hierarchy

Boundary failures derive from a single base (e.g. `ProviderException`) carrying a
stable `Code`. Don't scatter ad-hoc exception types — add a subclass
(`ProviderTimeoutException`, `ProviderNotFoundException`) or use a `Code`.

## 4. No PII / secrets in messages

Exception `Message` and ProblemDetails `detail` must never include tokens, auth
headers, or user/log content. Log full context structured (`logging.md`); return
`code` + `title` + a safe `detail`.

## 5. Rethrow correctly

```csharp
throw;                                                   // ✅ preserves stack
throw ex;                                                // ❌ resets the stack
throw new ProviderException("code", "msg", inner: ex);   // ✅ wrap — keep the inner
```

## Anti-patterns / self-audit

```bash
rg -n 'catch \(Exception' src/ --type cs      # only the top-level handler
rg -n 'catch\s*\{\s*\}' src/ --type cs        # empty catch — banned
rg -n 'throw ex;' src/ --type cs              # stack reset — use `throw;`
```

## Related

- `error-mapping.md` — exception → HTTP status
- `problem-details.md` — the one handler that catches broadly
- `code-shape.md` §1 — pattern match for expected outcomes
