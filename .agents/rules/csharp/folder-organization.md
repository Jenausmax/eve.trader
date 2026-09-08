---
description: file and folder organization inside a project — 1 type per file, namespace mirrors folder path, max 3 files per folder (split into subfolders otherwise)
globs: ["**/*.cs"]
always: true
---

# Folder organization (per project)

Структура файлов и папок **внутри одного проекта** (.csproj). Layer overview —
в правиле слоёв конкретного проекта (project-local). Naming/setup — в
`project-naming-and-setup.md`. Этот файл — про `.cs`-файлы, не про `.csproj`;
если в проекте есть отдельный cap на число проектов в папке, он живёт в
правилах проекта.

## 1. One public type per file — REQUIRED

```csharp
// ❌ Wrong — multiple public types in one file
// Order.cs
public sealed class Order { }
public sealed class LineItem { }   // separate file: LineItem.cs

// ✅ Correct
// Order.cs
public sealed class Order { }

// LineItem.cs
public sealed class LineItem { }
```

**Exception:** nested types are OK if logically tied (e.g. `Result<T>.Ok`
nested in `Result<T>`).

**Exception — paired `internal static class` helpers.** Two related
`internal static class` helpers in one file is OK when they're tightly
coupled and only used together (e.g. `EnumLabelCache<T>` + `EnumLabelReader`).
The exception does NOT extend to `public` types — those always get their
own file.

**Enforcement:** Roslyn `IDE0005` (single-type rule) + code review.

## 2. File name matches type name

```csharp
// Order.cs            → public sealed class Order / public sealed record Order
// GetOrderHandler.cs  → public sealed class GetOrderHandler
// <Solution>Options.cs → public sealed class <Solution>Options
```

**Enforcement:** Roslynator convention + code review (file name
must equal the first public type name).

## 3. Namespace mirrors folder path

Final namespace segment = deepest folder in path. File path → namespace:

```text
src/shared/<Solution>.Shared.Kernel/Results/Result.cs     → namespace <Solution>.Shared.Kernel.Results
src/shared/<Solution>.Shared.Kernel/Exceptions/ProviderException.cs
                                                       → namespace <Solution>.Shared.Kernel.Exceptions
src/modules/<Solution>.Modules.Orders/Handlers/GetOrderHandler.cs
                                                       → namespace <Solution>.Modules.Orders.Handlers
src/providers/<Solution>.Providers.X/Clients/I<X>Client.cs
                                                       → namespace <Solution>.Providers.X.Clients
```

`namespace <Solution>.Shared.Kernel;` (root, no subfolder) is also valid for
types living at the project root.

## 4. Cap: max 3 .cs files per folder

If a folder holds > 3 files, **split into subfolders by category**. One folder
= one concept.

```text
// ❌ Wrong — 11 files in one folder
src/shared/<Solution>.Shared.Kernel/Primitives/
├── Customer.cs
├── OrderId.cs
├── LineItemId.cs
├── LogLevel.cs
├── TimeRange.cs
├── Page.cs
├── Result.cs
├── Error.cs
├── ProviderException.cs
├── ProviderNotFoundException.cs
└── ProviderTimeoutException.cs

// ✅ Correct — 5 subfolders, 1-3 files each
src/shared/<Solution>.Shared.Kernel/
├── Identifiers/    (3 files): Customer, OrderId, LineItemId
├── Time/           (2 files): TimeRange, LogLevel
├── Pagination/     (1 file):  Page<T>
├── Results/        (2 files): Result<T>, Error
└── Exceptions/     (3 files): ProviderException + 2 subclasses
```

**Why:**
- **Visual scan:** count files in folder without thinking.
- **IDE tree view:** folders with ≤ 4 files collapse cleanly in IDE tree;
  > 4 explodes the view.
- **Cognitive load:** folder with 10 files demands "what's common here";
  folder with 3 is namespace-like.
- **Diff readability:** PR touching one folder of 3 files = one coherent
  slice; PR touching one folder of 10 files = grab-bag.

**Reference shape:** `<Solution>.Shared.Capabilities/` has 3 root types
+ 1 subfolder (`Defaults/`); `<Solution>.Shared.Contracts/` has 2 subfolders
(`Pagination/`, `Routes/`), each 1 file.

## 5. Single file at project root = exception

Single-file projects (1 .cs file at project root, no subfolder) are
acceptable. The cap applies when there's > 1 file. Examples:
- `<Solution>.Shared.Kernel/Pagination/Page.cs` — alone in folder
- `<Solution>.Modules.Orders/Endpoints/OrdersEndpoint.cs` — alone in folder

## 6. Folder name = category, not type name

Folder names describe the **category** of types inside, not a specific type:

```text
// ❌ Wrong — folder named after the type
src/.../OrderIds/OrderId.cs   (only OrderId here, weird)

// ✅ Correct — folder named after the category
src/.../Identifiers/OrderId.cs    (alongside Customer, LineItemId)
src/.../Results/Result.cs         (alongside Error)
src/.../Exceptions/ProviderException.cs   (alongside NotFound, Timeout)
```

## 7. One subfolder per concept — no nesting by feature flag

Don't split a single concern across multiple subfolders. Example:
- `Exceptions/` contains all provider exceptions together
- NOT: `Exceptions/Provider/`, `Exceptions/NotFound/`, `Exceptions/Timeout/`

Each subfolder = one cohesive concept that fits in your head.

## Self-audit grep

```bash
# .cs files per folder (warning if > 3 in src/, > 5 in tests/)
for d in $(find src -type d -not -path "*/obj/*" -not -path "*/bin/*" -not -path "*/.git/*"); do
    count=$(find "$d" -maxdepth 1 -name "*.cs" | wc -l)
    [ "$count" -gt 3 ] && echo "WARN: $d has $count .cs files"
done

# Wrong namespace (namespace doesn't end with deepest folder name)
rg -n "^namespace " src --type cs | rg -v "^namespace [A-Z][\w]+(\.[A-Z]\w+)*;" -P
```

## Связанные правила

- правило слоёв проекта (project-local) — обзор слоёв (host/shared/modules/providers)
- `project-naming-and-setup.md` — naming, decision tree
- `class-layout-and-tooling.md` — class member ordering, file headers, one type per file
- `naming-and-types.md` — sealed, record vs class, type references
- `code-shape.md` — var, braces, ns
