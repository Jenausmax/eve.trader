---
description: c# class member layout order, file organization, required tooling — incl. NO private methods, only file-static helpers
globs: ["**/*.cs"]
always: true
---

# Class layout, XML docs, required tooling

## 1. Member ordering — REQUIRED

Inside a class, members in this order:

1. **Static fields** (private, public)
2. **Static properties**
3. **Static factories / methods**
4. **Instance fields** (rare with primary ctor — only mutable state)
5. **Constructors** (only when primary ctor is insufficient)
6. **Properties**
7. **Public methods**

> **Private methods are BANNED** (see §1a). Helpers live in file-static
> classes or separate helper classes per the
> `folder-organization.md` 1-type-per-file rule.

```csharp
public sealed class ProductPriceCache(
    IOptions<CacheOptions> options,
    IMemoryCache cache)
{
    // 1. Static fields
    private static readonly ActivitySource Activity = new("App.Products.Cache");

    // 2. Static properties
    public static TimeSpan DefaultTtl { get; } = TimeSpan.FromSeconds(60);

    // 3. Static factories
    public static PriceCacheKey ForProduct(string productId) => new(productId);

    // 5. Constructors (only when primary ctor is insufficient)
    // (none — primary ctor covers it)

    // 6. Properties
    public bool IsEnabled => options.Value.Enabled;

    // 7. Public methods
    public async Task<ProductPrice?> GetOrLoadAsync(string productId, CancellationToken cancellationToken)
    {
        // ...delegates to file-static helper, never extract private method
    }
}
```

## 1a. NO private methods — extract helpers to file-static class

**Hard rule.** Every `private` method in production code (Repositories,
Controllers, Workers, Validators, etc.) is a violation. "Private" means
encapsulated logic on a single class — that's procedural style, banned.

Substitute with one of:

| Pattern | When | Where it lives |
|---------|------|----------------|
| **File-scoped `static class`** | helper logic that consumes types from the same feature | top-level file in `Mapping/` subfolder |
| **Named `internal static class`** | helper logic that crosses feature boundaries or is reused | dedicated file in `Mapping/` |
| **Extension method class** | adds behavior to an existing type | dedicated file in `Extensions/` |
| **Standalone helper class** | when state matters (rare) | regular file in `Helpers/` |

**Allowed exemptions** (framework-mandated overrides only):

```csharp
private bool Equals(MyType other) => ...;          // IEquatable override
public sealed class MyType : IDisposable
{
    private void Dispose(bool disposing) { ... }    // IDisposable pattern
}

// Minimal API endpoint handler — referenced as method group from MapGet/MapPost;
// `private static` keeps it off the public API surface. See api-design.md.
public static class ProductsEndpoint
{
    public static void MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/products").MapGet("/{id}", GetProductAsync);
    }
    private static async Task<...> GetProductAsync(...) { ... }
}
```

Even inside endpoint handlers, DTO mapping must delegate to a separate
file-scoped static class — see `mapper.md`.

### Explicit exceptions — limited, documented, no general permission

The hard rule has three narrow, framework-mandated exceptions. Each is
a **specific case where the rule cannot be applied without breaking the
framework contract** — not a general permission to relax §1a.

#### (a) EF Core parameterless constructor

```csharp
// ✅ Allowed — EF Core materialises entities via parameterless ctor.
//    Prefer `internal` over `private` (less restrictive; lets EF + test code call it).
internal OrderEntity() { }
// or, if reflection-friendly access is the only consumer:
private OrderEntity() { }
```

- **Why:** EF Core's `DbContext` materialises entities via the parameterless
  constructor. Without it, `query.ToListAsync()` throws at runtime.
- **When it applies:** EF entity types (`*Entity`) only. **Not** DTOs,
  request/response, value objects, or non-EF POCOs.
- **How to make it less bad:** prefer `internal` over `private` when test
  fixtures need to construct the entity directly. Mark the entity `sealed`
  (per `naming-and-types.md` §2) so the parameterless ctor isn't inherited
  into a wider surface.

#### (b) `protected` members on `abstract` base classes

```csharp
// ✅ Allowed — abstract base exposes protected hook for subclasses.
public abstract class TenantScopedRepository<T>(Repository<T> inner)
{
    protected TenantId CurrentTenantId { get; }   // subclass reads this
    public abstract Task<T?> GetAsync(EntityId id, CancellationToken ct);
}
```

- **Why:** when a base class provides an inheritance contract, `protected`
  members are how subclasses read shared state. This is the framework's
  extensibility mechanism — alternatives (e.g. making the field `internal`
  and using friend assemblies) leak access into the wrong scope.
- **When it applies:** only on `abstract` base classes where the protected
  member **is** the documented contract for subclasses. **Not** a general
  permission to add `protected` to any class — the class must be `abstract`
  with a documented extension point.
- **How to make it less bad:** document the protected member's contract in
  the base class XML doc. Prefer `protected` over `public` for state, and
  prefer `protected internal` only when assembly + subclass both need it.

#### (c) `Dispose(bool disposing)` pattern

```csharp
// ✅ Allowed — IDisposable.Dispose(bool) override is framework contract.
public sealed class MyType : IDisposable
{
    private bool disposed;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;
        if (disposing) { /* managed cleanup */ }
        disposed = true;
    }
}
```

- **Why:** `Dispose(bool)` is the standard pattern for `IDisposable` /
  `IAsyncDisposable` — the parameterless `Dispose()` calls
  `Dispose(true)`; the finalizer calls `Dispose(false)`. Without the
  protected hook, subclasses can't participate in deterministic cleanup.
- **When it applies:** only the `Dispose(bool disposing)` method itself,
  in a class implementing `IDisposable` / `IAsyncDisposable`. The
  parameterless `Dispose()` / `DisposeAsync()` is `public` (or
  `sealed override`); the `Dispose(bool)` is the only `protected virtual`
  hook.
- **How to make it less bad:** in sealed classes that don't need
  subclassing, mark the class `sealed` and the method
  `private void Dispose()` — no virtual dispatch needed. Drop the
  `Dispose(bool)` boilerplate entirely if no finalizer is required.

## 1b. Decomposition triggers — file length + extraction ladder

§1a covers *private methods* (banned); this covers **when a whole class/file
is too big** and **where the extracted code goes**.

**Trigger — a production `.cs` file over 300 lines** is a refactor signal.
Long files hide couplings and resist isolated testing. Split along the class's
actual responsibilities (a class with a command, a query, and a mapping helper
is three files), never along an arbitrary line count.

**Extraction ladder** — take the earliest that fits:

1. **Inject the collaborator** — a method that only touches one other class
   belongs on that class. Move it, call it.
2. **Extract a service** — logic with its own dependencies (repo, logger,
   clock) becomes a new injected class.
3. **File-static helper** — pure logic (no instance state, no I/O) → a
   `file static class <X>Helpers` in the same folder.
4. **Extension method** — behaviour on a type you don't own → `*Extensions.cs`
   as a `file static class`.

How many private methods are "allowed" is **not** a knob here — §1a is zero.
This ladder decides the *shape* of the extraction, not whether to extract.

**When NOT to count toward "too big":**

- Constructors (primary ctor + `Dispose`).
- Framework overrides (`override Configure`, etc.) — part of the base
  contract, not the class's own logic.
- Trivial one-liner accessors (`=> field;` is a property in disguise).
- Test code — test projects are allowed to grow; both §1a and this trigger
  exempt tests.
- **Files where the only public type is a Refit interface** — these are
  API surfaces whose size scales with the upstream API, not with the
  class's own logic. A `ICatalogClient` with 30 endpoints is naturally
  large; the file is one type, one concern (the wire contract). Splitting
  would scatter the contract surface. Example shape:
  ```csharp
  public interface ICatalogClient
  {
      [Get("/products/{id}")]            Task<ProductDto> GetProductAsync(...);
      [Get("/products")]                 Task<List<ProductDto>> ListProductsAsync(...);
      [Post("/products")]                Task<ProductDto> CreateProductAsync(...);
      [Put("/products/{id}")]            Task UpdateProductAsync(...);
      [Delete("/products/{id}")]         Task DeleteProductAsync(...);
      // ... 25 more endpoints, naturally 300+ lines for a real upstream
  }
  ```
  The same file should **not** contain DTO `record` types — extract those
  to a sibling `Models/` file (one record per file per `folder-organization.md`).
  Note: this exception does NOT extend to Refit interfaces that mix
  multiple distinct contracts (e.g. `ICatalogAndOrdersClient`); split those
  by upstream concern.
- **Files where the only public type is a plain interface** (not Refit) —
  these are contracts whose size scales with the contract surface, not
  with the class's own logic. A `IFooService` with 15 documented members
  is naturally large; the file is one type, one concern (the contract).
  Same caveat: split if the interface conflates multiple concerns.

**Self-audit**

```bash
# Changed/added files over 300 lines
git diff --name-only --diff-filter=AM | xargs wc -l | sort -rn | head
```

## 2. One type per file — REQUIRED

```csharp
// ❌ Wrong
// Order.cs
public sealed class Order { }
public sealed class LineItem { }

// ✅ Correct
// Order.cs
public sealed class Order { }
// LineItem.cs
public sealed class LineItem { }
```

**Exception:** nested types OK if logically tied
(`Result<T>.Ok` nested in `Result<T>`).

## 3. File name matches type name

```csharp
// Order.cs → public sealed class Order
// GetOrderHandler.cs → public sealed class GetOrderHandler
// <Solution>Options.cs → public sealed class <Solution>Options
// OrderEndpointMapper.cs → public static class OrderEndpointMapper
```

**Enforcement:** Roslynator convention + code review (file name must
equal the first public type name).

## 4. XML documentation — REQUIRED on public API

`<summary>` required on:
- **Interfaces**: all members.
- **Base classes** (inherited): all public members.
- **Public API** of concrete classes and records.

**Exempt:** `Dispose` / `DisposeAsync` — standard pattern.

**Enforcement:** `CS1591` (severity=error in `.editorconfig`).

## 5. Required tooling

### `dotnet format`

Runs in `dotnet build` via `VerifyFormatOnBuild` target in the build tools project.

```bash
dotnet format <solution>.slnx --severity hidden  # canonical fix
```

### Roslynator / VSTHRD analyzers

Enabled globally via `Directory.Build.props`. Severity per rule in
`.editorconfig`. See `analyzers.md`.

## 6. No nullable warnings, no analyzer warnings

Build fails on any warning (`TreatWarningsAsErrors=true`). Don't suppress
without rationale — see `analyzers.md`.

## 7. File headers

No required headers. Don't add `// Copyright (c) ...` boilerplate.

## Related rules

- `code-shape.md` — var, braces, file-scoped namespaces
- `constructors-and-fields.md` — primary ctor
- `naming-and-types.md` — type naming
- `analyzers.md` — analyzer configuration
- `folder-organization.md` — 1 type per file (helpers go in their own files)
- `../process/build-verification.md` — build gate

## Self-audit grep

```bash
# private methods in production code (excluding framework overrides)
rg -n "^\s*private\s+(static\s+)?(async\s+)?[A-Z]\w+\s+\w+\(" src/ tests/

# Should be empty except for IEquatable.Equals, Dispose(bool), or minimal API handler
```
