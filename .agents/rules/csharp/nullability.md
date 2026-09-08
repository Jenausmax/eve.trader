---
description: nullable is on — trust the signature; ban `!` null-forgiving except a proven boundary, ban #nullable disable, prefer required over nullable-with-default
globs: ["**/*.cs"]
always: true
---

# Nullability

`<Nullable>enable</Nullable>` is global (`Directory.Build.props`). The compiler
**is** the null checker — lean on it, don't silence it. Same principle as the
`ThrowIf*` ban (`code-shape.md` §11): trust the signature.

## 1. No `!` (null-forgiving) except a proven boundary

`x!` asserts "I know better than the compiler". Allowed **only** where the
compiler genuinely can't see the invariant, and only with a comment:

- reflection / `Activator` / serializer output
- test arrange where the value is guaranteed by setup

Everywhere else, express non-null through type + flow, not `!`.

```csharp
// ❌ Wrong — hides a real possible null
var name = order.Customer!.Name;

// ✅ Correct — pattern-match the nullable away (code-shape §1)
if (order.Customer is { } customer)
{
    var name = customer.Name;
}

// ✅ Allowed — boundary, with reason
// boundary: bound by STJ; property is [Required] and validated on start
var token = options.CurrentValue.AuthToken!;
```

## 2. No `#nullable disable` / `restore`

Never opt a file out. If a dependency is nullable-oblivious, annotate at the call
boundary — don't disable the whole file.

## 3. `required` over nullable-with-default

If a value must be present, model it with `required` (compile-time), not
`string? X` plus a runtime check (`di-options.md` §3, `naming-and-types.md` §3).

```csharp
// ❌ Wrong — nullable + hidden invariant
public string? TracesUrl { get; init; }

// ✅ Correct — required is the contract
public required Uri TracesUrl { get; init; }
```

## 4. Nullability lives at the edge

Nullable is a boundary concern (HTTP input, external DTO, config). Resolve it to
non-null (or fail) at the edge; types flowing through the domain are non-null.

```csharp
// ✅ Nullable at the parse edge; resolved value flows on non-null
public static TraceId? ToTraceId(string value) =>
    value.Length is >= 16 and <= 32 ? new TraceId(value) : null;
```

## Anti-patterns / self-audit

```bash
rg -n '\w+!\.' src/ --type cs          # null-forgiving deref — each needs a boundary comment
rg -n '#nullable disable' src/ tests/  # must be empty
rg -n '\?\?\s*throw' src/ --type cs    # `x ?? throw` substituting for `required` — reconsider
```

## Related

- `code-shape.md` §11 — no `ThrowIf*` (same "trust the signature")
- `di-options.md` — `required` on options
