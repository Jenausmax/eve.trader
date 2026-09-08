---
description: time — one wire format (UTC unix ms for time-stamped upstreams), DateTimeOffset internally, TimeProvider not DateTime.Now, unit conversion only at the mapper edge
globs: ["**/*.cs"]
always: true
---

# Time & wire format

Timezone bugs are the #1 footgun when a service bridges backends that each speak
a different native time format. Pin one wire format and convert only at the edge.

## 1. One wire format — UTC unix milliseconds (`long`)

For a service fronting time-stamped upstreams, every HTTP field and every domain query
carries time as **unix epoch milliseconds, UTC** — `long`, named `*UnixMs`.
(It matches the backends' native format and removes tz ambiguity.)

```csharp
public sealed record ListOrdersRequest(long StartUnixMs, long EndUnixMs, ...);
public sealed record TimeRange(long StartUnixMs, long EndUnixMs);
```

Never put `DateTime` / ISO strings on the wire. Never mix seconds or microseconds
into the wire type.

## 2. Internally — `DateTimeOffset` (UTC)

When you need a real instant, use `DateTimeOffset` (UTC); convert at the edge:

```csharp
var start = DateTimeOffset.FromUnixTimeMilliseconds(request.StartUnixMs);
long ms   = instant.ToUnixTimeMilliseconds();
```

`DateTime` (no offset) is banned in new code — timezone-ambiguous.

## 3. Clock — `TimeProvider`, never `DateTime.UtcNow`

Read "now" from an injected `TimeProvider` (singleton `AddSingleton(TimeProvider.System)`
— `di-lifetimes.md`). Direct `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in logic
is banned: it makes time-dependent behavior untestable.

```csharp
// ❌ untestable
var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);

// ✅ inject TimeProvider
public sealed class Foo(TimeProvider clock)
{
    public DateTimeOffset Cutoff() => clock.GetUtcNow().AddMinutes(-5);
}
```

## 4. Convert units at the mapper, not the domain

Each backend has its own native format. Convert **only** inside the provider's
mapper (`mapper.md`), never let a native unit reach a feature module:

| Backend | Native | Convert to |
|---------|--------|-----------|
| Upstream A — `_created` | RFC3339 string | `DateTimeOffset` → unix ms |
| Upstream A — query `start`/`end` | ISO8601 string | from unix ms |
| Upstream B — `capturedAt`/`elapsed` | **microseconds** | ÷1000 → unix ms |

An upstream's µs vs the domain's ms is a real trap — the conversion lives in one
mapper, with a test.

## Anti-patterns / self-audit

```bash
rg -n 'DateTime\.(Now|UtcNow)|DateTimeOffset\.UtcNow' src/ --type cs   # use injected TimeProvider
```

- ❌ `long` on the wire that's actually seconds or microseconds.
- ❌ ISO/RFC3339 strings crossing the HTTP boundary.
- ❌ mixing microseconds and milliseconds outside the mapper.

## Related

- `di-lifetimes.md` — `TimeProvider.System` singleton
- `mapper.md` — unit conversion at the mapper
- `json-and-ndjson.md` — timestamp field parsing
