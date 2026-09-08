---
description: system.text.json — no inline options, [JsonPropertyName] for external DTOs, source-gen context, NDJSON parsed line-by-line in the mapper (not as a JSON array)
globs: ["**/*.cs"]
always: true
---

# JSON & NDJSON

A service (de)serializes external payloads on every request — vendor JSON, NDJSON
export streams, upstream JSON. Keep it consistent and cheap.

## 1. `System.Text.Json` only, no inline options

Per `anti-patterns.md` §6: never `new JsonSerializerOptions(...)` at a call site.
Use `JsonSerializerOptions.Web` (frozen, camelCase) for standard cases. For
custom converters, configure via DI (`Configure<JsonOptions>` /
`services.AddJsonOptions(...)`) — never hand-roll a project singleton.
No `Newtonsoft.Json`.

## 2. External DTOs map wire names with `[JsonPropertyName]`

External field names (an upstream's `paymentID`, or `_id` / `_links` / `_created`)
don't match C# PascalCase. Map them explicitly; keep the property idiomatic. A
snake-case `PropertyNamingPolicy` does **not** cover leading-underscore fields
like `_id` — you need the attribute.

```csharp
public sealed record ItemDto(
    [property: JsonPropertyName("_id")]      string Id,
    [property: JsonPropertyName("_links")]   string Links,
    [property: JsonPropertyName("_created")] DateTimeOffset Created,
    [property: JsonExtensionData]            IDictionary<string, object?> Fields);
```

DTOs live in `Dto/`, one type per file, `sealed record`, mapped to domain in the
mapper — never exposed from a feature module.

## 3. Source-generated context (perf + trimming)

External DTOs are hot (every query). Prefer a source-generated
`JsonSerializerContext` over reflection serialization — faster and safe under
`InvariantGlobalization=true` + container/AOT publish.

```csharp
[JsonSerializable(typeof(UpstreamResponse))]
[JsonSerializable(typeof(ItemDto))]
internal sealed partial class UpstreamJsonContext : JsonSerializerContext;
```

## 4. NDJSON — parse line by line in the mapper

An export / streaming endpoint returns **newline-delimited JSON**, not an
array. Read the body and parse per line in the mapper — do **not**
`Deserialize<T[]>` the whole body.

```csharp
// ✅ provider does I/O; mapper owns NDJSON parsing
foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
    if (TryDeserialize(line, out var dto)) { entries.Add(Map(dto)); }
    // a single malformed line is logged + skipped, never fails the whole page
}

// ❌ Bad — NDJSON is not a JSON array
JsonSerializer.Deserialize<ItemDto[]>(body);
```

- Skip blank lines; one bad line must not fail the page (log/drop, or surface as
  a `warning`).
- Respect the result cap (e.g. 500 default / 1000 max) — stop past the limit and
  log if truncated.
- Stream the response when practical rather than buffering the full body.

## Anti-patterns / self-audit

```bash
rg -n 'new JsonSerializerOptions' src/ --type cs          # only in Program.cs (ConfigureHttpJsonOptions)
rg -n 'Deserialize<\w+\[\]>' src/ --type cs               # NDJSON parsed as array?
```

## Related

- `anti-patterns.md` §6 — JsonSerializerOptions singletons
- `mapper.md` — mappers own DTO → domain + NDJSON
- `time-and-wire-format.md` — timestamp parsing
