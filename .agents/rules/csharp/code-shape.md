---
description: c# code shape — pattern matching, var, file-scoped namespaces, braces, comments, collections, interface organization, no #region, no ThrowIf* (argument checks)
globs: ["**/*.cs"]
always: true
---

# Code shape

Этот файл — правила формы кода: pattern matching, var, скобки, namespace,
комментарии, коллекции, организация интерфейсов, запрет `#region`, запрет
`ThrowIfNull` в nullable-контексте.

Naming/sealed — в `naming-and-types.md`. Constructors/fields — в `constructors-and-fields.md`.

## 1. Pattern matching — минимальность через синтаксический сахар

**Главный принцип:** не разделяй присвоение и проверку. Если C# позволяет
уложить объявление переменной + условие в одно выражение — делай это.

### Inline assignment + check

```csharp
// ❌ Wrong — две строки
var user = await repository.GetByIdAsync(userId, cancellationToken);
if (user == null) return NotFound();

// ✅ Correct — одна строка
if (await repository.GetByIdAsync(userId, cancellationToken) is not { } user)
{
    return NotFound();
}
```

### Get-or-return-existing — merge обязателен в `is { } x`

Самый частый паттерн, где LLM ошибается: "получил сущность, если есть —
вернул её". Здесь `var + if is not null + return` — **всегда** неправильно,
даже если переменная формально упоминается и в `if`, и в `return`. Тело без
сайд-эффектов, только проброс значения → merge обязателен.

```csharp
// ✅ Correct — assign + check слиты
if (await repository.GetByIdAsync(userId, cancellationToken) is { } existing)
{
    return existing;
}

// ❌ Wrong — assign и check разделены
var existing = await repository.GetByIdAsync(userId, cancellationToken);
if (existing is not null)
{
    return existing;
}
```

**Merge не требуется** когда внутри ветки есть сайд-эффект или нетривиальное
использование: логирование, маппинг, повторное чтение поля, ветвление по
свойствам.

### `is var x and > N`

```csharp
// ✅ Присвоить и проверить одним выражением
if (await registry.MarkHungTasksAsync(timeout, cancellationToken) is var hungCount and > 0)
{
    logger.LogInformation("Marked {Count} tasks as Hung", hungCount);
}
```

### Switch expressions

```csharp
// ❌ Wrong
JobStatus status;
if (state == JobState.Running) status = JobStatus.Running;
else if (state == JobState.Disconnected) status = JobStatus.Disconnected;
else status = JobStatus.Waiting;

// ✅ Correct
var status = state switch
{
    JobState.Running       => JobStatus.Running,
    JobState.Disconnected  => JobStatus.Disconnected,
    _                      => JobStatus.Waiting,
};
```

### Inline single-use variables

Если переменная используется **один раз** — инлайн её.

```csharp
// ❌ Wrong
var invoiceData = await invoiceService.GetInvoiceDataAsync(invoiceId, cancellationToken);
return Ok(invoiceData);

// ✅ Correct
return Ok(await invoiceService.GetInvoiceDataAsync(invoiceId, cancellationToken));
```

### Guard-only locals — сливаются в `if` (формы A и B)

Локальная, объявленная присвоением и проверенная **сразу следующим** оператором,
не существует отдельно от проверки — она и есть проверка. Две формы нарушения:

**Форма A — одноразовая локальная.** `var x = <expr>;` где `x` используется
только в непосредственно следующем `if` (в условии или теле) — переносится в
проверку паттерном:

```csharp
// ❌ Wrong — присвоение живёт одним if
var parameters = Parameters;

if (registry is not null && parameters.Count > 0)
{
    registry.DeclareParameters(WorkerName, parameters);
}

// ✅ Correct — property-pattern съедает и null, и Count
if (Parameters is { Count: > 0 } parameters)
{
    registry.DeclareParameters(WorkerName, parameters);
}
```

**Форма B — повторная проверка на null.** Одна и та же локальная
null-проверяется в двух и более соседних `if` — резолв и проверка сливаются
в один внешний `if`:

```csharp
// ❌ Wrong — гейт дублируется
var registry = services.GetService<IWorkerParameterRegistry>();

if (registry is not null && Parameters.Count > 0) { registry.DeclareParameters(WorkerName, Parameters); }
if (registry is not null && Types.Count > 0) { registry.DeclareHandledEvents(WorkerName, Types); }

// ✅ Correct — один резолв, один гейт
if (services.GetService<IWorkerParameterRegistry>() is { } registry)
{
    if (Parameters is { Count: > 0 } parameters)
    {
        registry.DeclareParameters(WorkerName, parameters);
    }

    if (Types is { Count: > 0 } types)
    {
        registry.DeclareHandledEvents(WorkerName, types);
    }
}
```

**Граница применения:** `<expr>` в присвоении обязан быть чистым — свойство,
`GetService<T>()`, другой идемпотентный резолв. Выражение с побочным эффектом
или `await` остаётся отдельным присвоением: перенос в условие не должен менять
число вычислений. `GetService` (не `GetRequiredService`) в этой роли — не
нарушение DI-дисциплины, а opt-in порт, отсутствие которого легитимно.

**Self-audit grep** (грубый, ловит смежность — точный ловец это AST, см. regent):

```bash
# Форма A/B: присвоение, чья локальная проверяется следующим же if
rg -nU 'var (\w+) = [^;]+;\r?\n(?:\s*//[^\n]*\r?\n)*\s*if \(\1\b' src/ --type cs -P
# Форма B: одна локальная null-гейтится в соседних if
rg -nU 'if \((\w+) is not null[^{]*\{[^}]*\}\s*(?://[^\n]*\r?\n\s*)*if \(\1 is not null' src/ --type cs -P
```

### Inline в `foreach`

```csharp
// ❌ Wrong
var items = await ComputeAsync(cancellationToken);
foreach (var item in items) Process(item);

// ✅ Correct
foreach (var item in await ComputeAsync(cancellationToken))
{
    Process(item);
}
```

### Ternary в return

```csharp
// ❌ Wrong
var dataJson = await db.HashGetAsync(metaKey, field);
if (dataJson.IsNullOrEmpty) return null;
return JsonSerializer.Deserialize<ManagedProxy>(dataJson.ToString());

// ✅ Correct
return await db.HashGetAsync(metaKey, field) is { } dataJson
    ? JsonSerializer.Deserialize<ManagedProxy>(dataJson.ToString())
    : null;
```

### Когда промежуточная переменная нужна

- Значение используется дважды и более.
- Имя переменной добавляет смысл.
- Выражение слишком сложное для inline.

---

## 2. Async / await

- Весь IO — через `async`/`await`.
- Запрещено: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`.
- Async suffix — всегда (см. `naming-and-types.md` §1).
- `CancellationToken` последним параметром со значением по умолчанию.
- Прокидывай `cancellationToken` во все вложенные async-вызовы.
- Никаких `Task.Delay(...)` без токена.

**Enforcement:** VSTHRD103 (severity=error).

---

## 3. `var` vs explicit type

**Всегда `var`** когда тип выводится из правой части.

```csharp
// ✅ Correct
var user = await repository.GetUserByIdAsync(userId, cancellationToken);
var skus = new List<string>();
var count = items.Count;

// ❌ Wrong (избыточно)
List<string> skus = new List<string>();
User user = await repository.GetUserByIdAsync(userId, cancellationToken);
```

**Enforcement:** IDE0007 / IDE0008 (severity=error в `.editorconfig`).

---

## 4. File-scoped namespaces — REQUIRED

**Всегда `namespace Foo;` (file-scoped). Block-scoped запрещён.**

```csharp
// ✅ Correct
namespace MyProject.Services;

public sealed class UserService { }

// ❌ Wrong
namespace MyProject.Services
{
    public sealed class UserService { }
}
```

**Enforcement:** IDE0161 (severity=error).

---

## 5. Braces — REQUIRED всегда

Фигурные скобки требуются для **всех** control flow statements: `if`, `else`,
`for`, `foreach`, `while`, `do`, `using`, `lock`, `fixed`.

```csharp
// ✅ Correct
if (user is null)
{
    return NotFound();
}

foreach (var item in items)
{
    Process(item);
}

// ❌ Wrong — однострочник без скобок
if (user is null) return NotFound();

foreach (var item in items) Process(item);
```

Применимо и к `else`.

```csharp
// ✅ Correct
if (proxy.IsHealthy)
{
    await repository.MarkHealthyAsync(proxy.Id, cancellationToken);
}
else
{
    logger.LogWarning("Proxy {ProxyId} failed health check", proxy.Id);
}
```

**Минимальность через синтаксический сахар (раздел 1)** + **braces (этот раздел)**
работают вместе: первое — отсутствие промежуточных шагов; второе — форма
control flow.

**Enforcement:** csharp_prefer_braces = true (severity=error в `.editorconfig`).

### Expression-bodied methods — ЗАПРЕЩЕНЫ

**Методы — только block-body `{ }`. Expression-bodied методы запрещены.**

- **Свойства, accessors, индексаторы, операторы, лямбды** — expression-bodied разрешены.
- **Switch expressions** — не control statement, `=>` часть синтаксиса, braces не нужны.

```csharp
// ✅ Method — block body
public Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
{
    return repository.GetByIdAsync(userId, cancellationToken);
}

// ❌ Wrong — expression-bodied method, IDE0022 fail
public Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    => repository.GetByIdAsync(userId, cancellationToken);

// ✅ Property — expression-bodied OK
public string FullName => $"{FirstName} {LastName}";
```

**Enforcement:** IDE0022 (severity=error).

---

## 6. Inline-комментарии — только когда они ВАЖНЫ

Запрещены комментарии, дублирующие имя метода или операцию.
Разрешены комментарии, объясняющие **"почему"**, особенно если без
комментария код выглядит "неправильным" и его захочется "починить".

```csharp
// ❌ Wrong — дублирует имя
// increment counter
counter++;

// ❌ Wrong — описывает "что", это видно из кода
// loop through items
foreach (var item in items) ...

// ✅ Correct — объясняет "почему", предотвращает регрессию
// the catalog API throttles at 1200 req/min — увеличение приведёт к 429
private const int MaxRequestsPerMinute = 1100;

// ✅ Correct — workaround
// EF Core 8 теряет точность для decimal при ToList() — материализуем вручную
var rates = await query.AsAsyncEnumerable().ToListAsync(cancellationToken);

// ✅ TODO/HACK с автором или ссылкой
// TODO(ANL-1234): убрать после миграции на новый API каталога
```

Правило ревьюера: комментарий принимается, если без него существует реальный
риск того, что следующий читатель сломает код или потратит время на
понимание.

---

## 7. Collections — минимально достаточный тип

| Что нужно потребителю | Тип |
|----------------------|-----|
| Только итерация | `IEnumerable<T>` (с осторожностью) |
| Итерация + `Count` | `IReadOnlyCollection<T>` |
| Итерация + `Count` + индекс | `IReadOnlyList<T>` |
| Membership check | `HashSet<T>` / `IReadOnlySet<T>` |
| Фиксированный набор, max perf | `T[]` |

### Default для public API — `IReadOnlyCollection<T>`

`List<T>` / `IList<T>` в публичном API не возвращаем.

```csharp
// ✅ Default
public IReadOnlyCollection<string> ActiveSkus => activeSkus;

// ✅ Нужен индексный доступ
public IReadOnlyList<Order> RecentOrders => recentOrders;

// ✅ Фиксированный, часто итерируется
public string[] SupportedMarketplaces { get; } = ["Amazon", "Ebay", "Shopify"];
```

### `HashSet<T>` для membership — O(1)

```csharp
// ✅ Fast lookups
private readonly HashSet<string> activeSkus = new(StringComparer.OrdinalIgnoreCase);

// ❌ Wrong — O(n)
private readonly List<string> activeSkus = new();
```

### `List<T>` / `IList<T>` — только локально для мутации

```csharp
// ✅ Локальная мутация — List
var skus = new List<string>();
foreach (var product in products)
{
    if (product.IsActive) skus.Add(product.Sku);
}
return skus.ToArray();

// ❌ Wrong — List в публичном API
public List<string> Skus { get; }
```

### Pure-map `foreach` → `Select` (manual map-loop ban)

If a `foreach` body is **only** `result.Add(<map of item>)` — no filter,
no side effects, no early `continue`/`break` — it is a pure projection.
Write it as LINQ `Select`, not an imperative allocate-loop.

Empty early-return before such a loop is also redundant: `Select` on an
empty source already yields an empty sequence (prefer `[]` /
`Array.Empty<T>()` / `.ToArray()` at the return edge).

```csharp
// ❌ Wrong — manual map-loop + empty early-return
var list = await checkpointer.ListAsync(threadId, cancellationToken);
if (list.Count == 0)
{
    return [];
}

var result = new List<ThreadState>(list.Count);
foreach (var snapshot in list)
{
    result.Add(ThreadStateMapping.FromSnapshot(snapshot));
}

return result;

// ✅ Correct — pure projection
var list = await checkpointer.ListAsync(threadId, cancellationToken);
return list
    .Select(static snapshot => ThreadStateMapping.FromSnapshot(snapshot))
    .ToArray();
```

**When `foreach` stays:**

| Body | Keep `foreach`? |
|------|-----------------|
| Only `result.Add(Map(item))` | ❌ → `Select` |
| `if (…) result.Add(…)` filter | ✅ (or `Where` + `Select` if both pure) |
| Side effects (log, mutate, I/O) | ✅ |
| `break` / `continue` / multi-statement | ✅ |
| Building non-list structure (dict, tree) | ✅ |

**Materialize once at the edge:** `.ToArray()` when the consumer needs
`IReadOnlyList<T>` / array; `.ToList()` only when a later local mutation
needs `List<T>`. Prefer `static` lambda when the body does not close over
locals (`static item => Map(item)`).

**Enforcement:** `csharp.code-shape.manual-map-loop` (AST detect) +
`csharp.code-shape.manual-map-loop.fix` (`regent fix --unsafe`,
suggested-lane function rewrite for the capacity+foreach shape).

### Materialization — never double-allocate after EF tracks

EF Core's <c>ToListAsync()</c> returns <c>List&lt;T&gt;</c> (tracked or
no-tracking, depending on query). Do not run <c>.ToList()</c> on the
result — it's already a <c>List&lt;T&gt;</c>; one more copy is wasted.

```csharp
// ❌ Wrong — List → List copy is wasted
var items = await db.Clusters.AsNoTracking().ToListAsync(ct);
return new ClusterDetail(..., items.ToList(), ...);

// ✅ Correct — items is List<T>, assign directly
var items = await db.Clusters.AsNoTracking().ToListAsync(ct);
return new ClusterDetail(..., items, ...);
```

When the return type is <c>IReadOnlyList&lt;T&gt;</c>, <c>List&lt;T&gt;</c>
is implicitly castable (it implements <c>IReadOnlyList&lt;T&gt;</c>);
no cast, no copy. Only fall back to <c>.ToArray()</c> when the EF
provider gives you a non-list materialization (rare — Dapper raw
queries, custom SqlQuery) and the caller needs array semantics.

### Empty collections

```csharp
// ✅ Бесплатно
return Array.Empty<Order>();
return [];

// ❌ Аллокация
return new List<Order>();
return new Order[0];
```

### Anti-patterns

```csharp
// ❌ IEnumerable когда нужен Count или повторная итерация — LINQ дёргает источник заново
public IEnumerable<string> Skus => skus;

// ❌ List для lookups — O(n)
public List<string> Skus { get; }
```

---

## 8. Interface organization — default в `Interfaces/`

Интерфейсы лежат в папке `Interfaces/` на одном уровне с реализациями.

```
Services/
├── Interfaces/
│   ├── IProxyHealthChecker.cs
│   └── IProxyRepository.cs
├── ProxyHealthChecker.cs
└── ProxyRepository.cs
```

**Исключение — one-to-one co-location:** если у интерфейса ровно одна
реализация и они никогда не разойдутся — можно положить рядом, в одной
папке, с общим префиксом имени (`OrderProcessor.cs` + `IOrderProcessor.cs`).

**Где НЕ обязательно `Interfaces/`:**
- Marker interfaces в `*.Markers`, `*.Abstractions`.
- Domain abstractions в `*.Entity.Core` / `*.Domain` — допустимо рядом.

---

## 9. Private business logic — ЗАПРЕЩЕНА в production-коде

### Принцип одной строкой

> **Никаких `private` методов в production-классах. Без исключений, кроме contract override.**

### Что считается contract override (разрешено)

`private` modifier допустим ТОЛЬКО в одном из пяти случаев:

1. **Override метод интерфейса / base class** — `Dispose()` / `DisposeAsync()`,
   `ExecuteAsync(...)`, `ExecuteCycleAsync(...)`, `HandleAsync(...)`, и т.п. —
   везде ключевое слово `override` стоит в сигнатуре.
2. **Explicit interface implementation** — `void IDisposable.Dispose()`.
3. **Minimal API endpoint handler** — `private static` метод, переданный
   method-group'ой в `MapGet`/`MapPost`/… из `Map<Feature>Endpoints`.
   `private static` держит его вне public-поверхности; любой нетривиальный
   маппинг всё равно выносится в отдельный `file`/`internal static` класс.
   См. `class-layout-and-tooling.md` §1a и `api-design.md`.
4. **EF Core parameterless constructor** — EF Core материализует entities через
   parameterless ctor. Конвенция: `internal Vm() { }` (internal, не private) —
   так EF Core может его вызвать, но внешний код не обходит валидацию
   конструктора. Это **единственное** легитимное исключение для
   `private`/`internal` ctor в production-коде.
5. **`protected` члены на `abstract` базовом классе** — legitimate; наследники
   нуждаются в доступе к shared state базы. `protected IQueryable<T> Query()`
   на `TenantScopedRepository<T>` — корректно. `protected` **не** допустим
   на sealed- или non-abstract-классах — там это уже попытка скрыть
   private от ревьюера.

`override` modifier НЕ эквивалентен `private`. После `override` метод
становится `public` (или inherited visibility) — но его разрешено держать
**sealed override private** в наследнике, чтобы не светить наружу.

### Что запрещено (полный список)

`private` методы в **любом** из следующих классов, **если это не override**:

- Repository, QueryService, port implementation, port interface impl.
- EF DbContext, EF Configuration (`IEntityTypeConfiguration<T>`),
  EF migration.
- Controller / minimal API endpoint.
- Command / query handler (`ICommandHandler<,>`, `IQueryHandler<,>`).
- Worker / BackgroundService / HostedService.
- Adapter / external SDK wrapper / `IHttpClientFactory` consumer.
- Validator (`AbstractValidator<T>`).
- Seeder (`ISeeder`).
- Dispatcher / behavior / pipeline middleware.
- `JsonSerializerOptions` configuration holder (DI-managed via `Configure<JsonOptions>`, not hand-rolled singleton — `anti-patterns.md` §6).
- Маппер (`IEntityMapper`, `IRequestMapper`, ...).

Этот список не закрытый. **Если класс — production-код и не DTO/entity, то
там не должно быть `private` методов кроме override.**

### Почему жёстко

Потому что `private` метод в production-классе — это anti-pattern, который
**противоречит другим правилам проекта** в каждом конкретном случае:

| Если пишешь | Это нарушает |
|---|---|
| "private валидацию в Controller" | `anti-patterns.md` §4 ("никаких приватных методов-валидаторов в controller, только FluentValidation") |
| "private форматирование в Controller" | `anti-patterns.md` §6 (маппинг через `Mapper`, не private helper) |
| "private оркестрацию в Worker" | `class-layout-and-tooling.md` §1a (base class уже оркестрирует; тело в `ExecuteCycleAsync`) |
| "private SQL builder в Repository" | `ef-core.md` §1 ("no magic strings") — SQL собирается из `DatabaseInformation.Tables.X` / `Schemes.Y`, не литералами |
| "private маппинг row → DTO в Repository" | `code-shape.md` §11 ("private business logic выносим") — маппинг отдельно тестируется без БД |
| "private валидацию в `AbstractValidator`" | `anti-patterns.md` §4 — проверки через `RuleFor`, не private `BeUniqueAsync` |
| "private seed fixture в Seeder" | `code-shape.md` §9 — каждый `EnsureXxx` fixture в отдельный класс с зависимостями |
| "private URL/JSON helper в Adapter" | `ef-core.md` §"..." + DRY — extension method или отдельный маппер |

Старая формулировка §9 ("HostedService/Worker/Controller — допустимо")
**сама себе противоречила** — пример `ProxyValidationWorker.ValidateProxiesAsync`
нарушал `class-layout-and-tooling.md` §1a. Эта версия правила закрывает дыру.

### Куда выносить

| Ситуация | Куда |
|---|---|
| Есть свои зависимости (DI: repo, logger, client, ...) | `internal sealed` helper в той же папке, отдельный файл, конструктор принимает зависимости. Если сложный pipeline с интерфейсом — `IXxxHelper` / `IXxxBuilder` / `IXxxFixture`. |
| Нет зависимостей, чистая функция | `file static class` рядом (в том же или соседнем файле `*Helpers.cs`). |
| Расширение существующего типа | extension methods в `*Extensions.cs` (отдельный файл, `file static class`). |
| Маппинг между слоями | отдельный `*Mapper.cs` с интерфейсом `IXxxMapper`. |
| Валидация | FluentValidation rules (`RuleFor` chain + переиспользуемые Rule-объекты), не private-методы. |

### 9.1 — Repository / port implementation

```csharp
// ❌ WRONG — Repository с private helper'ами
public sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public async Task<Order?> GetAsync(OrderId id, CancellationToken ct)
    {
        var entity = await db.OrdersSet.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity is null ? null : MapToAggregate(entity);  // ← private method
    }

    private static Order MapToAggregate(OrderEntity e) => new(...) { ... };
    private static ProductEntity MapProduct(Product p) => new(...) { ... };
    private async Task<IReadOnlyList<Product>> LoadProductsAsync(Guid orderId, CancellationToken ct) { ... }
    private static string ToColumnName(string name) => name.ToSnakeCase();
}
```

```csharp
// ✅ CORRECT — Repository orchestration only; helpers in separate files
public sealed class OrderRepository(
    OrdersDbContext db,
    IOrderMapper mapper) : IOrderRepository
{
    public async Task<Order?> GetAsync(OrderId id, CancellationToken ct)
    {
        var entity = await db.OrdersSet.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity is null ? null : mapper.ToAggregate(entity);
    }
}

// Persistence/Mappers/OrderMapper.cs
internal sealed class OrderMapper : IOrderMapper
{
    public Order ToAggregate(OrderEntity e) => new(...) { ... };
    public ProductEntity ToEntity(Product p) => new(...) { ... };
}
```

### 9.2 — EF Configuration (`IEntityTypeConfiguration<T>`)

```csharp
// ❌ WRONG — IEntityTypeConfiguration с private helper ConfigureProducts / ConfigureFolderRelations
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable(DatabaseInformation.Tables.Orders, OrdersDbContext.Schema);
        ConfigureProducts(builder);  // ← private method
        ConfigureFolderRelations(builder);  // ← private method
    }

    private void ConfigureProducts(EntityTypeBuilder<Order> builder) { ... }
    private void ConfigureFolderRelations(EntityTypeBuilder<Order> builder) { ... }
}
```

```csharp
// ✅ CORRECT — flat Configure; nested owned-types выносятся в `file static class` рядом
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable(DatabaseInformation.Tables.Orders, OrdersDbContext.Schema);
        builder.Property(static c => c.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.OwnsMany(static order => order.Products, ProductsConfiguration.Configure);
    }
}

// Configurations/ProductsConfiguration.cs
file static class ProductsConfiguration
{
    public static void Configure(OwnedNavigationBuilder<Order, Product> product)
    {
        product.ToTable(DatabaseInformation.Tables.Products, OrdersDbContext.Schema);
        product.HasKey(static p => p.Id);
        product.Property(static p => p.ImageUrl).HasColumnName("image_url").HasMaxLength(2048).IsRequired();
    }
}
```

### 9.3 — Worker / BackgroundService / HostedService

```csharp
// ❌ WRONG — Worker с private orchestration (нарушает class-layout-and-tooling.md §1a)
public sealed class ProxyValidationWorker(
    IProxyRepository proxyRepository,
    IProxyHealthChecker proxyHealthChecker,
    ILogger<ProxyValidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ValidateProxiesAsync(stoppingToken);  // ← private method
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ValidateProxiesAsync(CancellationToken cancellationToken) { ... }
}
```

```csharp
// ✅ CORRECT — Worker через ScheduledWorkerBase; всё тело в ExecuteCycleAsync (override)
public sealed class ProxyValidationWorker(
    IServiceProvider services,
    TimeProvider clock,
    IDiagnosticSource? diagnostics,
    ILogger<ProxyValidationWorker> logger) : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    protected override WorkerSchedule Schedule => new IntervalSchedule(TimeSpan.FromMinutes(1));

    protected override async Task<object?> ExecuteCycleAsync(WorkerContext ctx, CancellationToken ct)
    {
        var repository = ctx.GetService<IProxyRepository>();
        var checker = ctx.GetService<IProxyHealthChecker>();
        var processed = 0;
        var failed = 0;

        foreach (var proxy in await repository.GetAliveProxiesAsync(ct))
        {
            var checkResult = await checker.CheckAsync(proxy, ct);
            processed++;
            if (!checkResult.IsHealthy) { failed++; continue; }
            await repository.MarkHealthyAsync(proxy.Id, checkResult.LatencyMs, ct);
        }

        return new ValidationResult(Processed: processed, Failed: failed);
    }
}
```

### 9.4 — Controller / minimal endpoint

```csharp
// ❌ WRONG — Controller с private валидацией и форматированием
public sealed class OrdersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var validation = ValidateCreateRequest(request);  // ← private method
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var formattedTotal = FormatTotal(request.Total);  // ← private method
        var command = new CreateOrderCommand(..., formattedTotal);
        var id = await dispatcher.SendAsync(command, ct);
        return CreatedAtAction(nameof(GetByIdAsync), new { id }, id);
    }

    private static ValidationResult ValidateCreateRequest(CreateOrderRequest request) { ... }
    private static string FormatTotal(decimal total) => total.ToString("0.00");
}
```

```csharp
// ✅ CORRECT — FluentValidation (через ValidationBehavior) + CommandMapper
public sealed class OrdersController(
    IDispatcher dispatcher,
    IOrderCommandMapper mapper) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        var command = mapper.ToCommand(request);
        var id = await dispatcher.SendAsync(command, ct);
        return CreatedAtAction(nameof(GetByIdAsync), new { id }, id);
    }
}

// Endpoints/Mappers/OrderCommandMapper.cs
internal sealed class OrderCommandMapper : IOrderCommandMapper
{
    public CreateOrderCommand ToCommand(CreateOrderRequest request) =>
        new(request.CustomerId, request.Name.Trim(), request.StartDate, request.EndDate, request.Total);
}

// Application/Validation/CreateOrderValidator.cs — FluentValidation
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(static c => c.Name).SetValidator(new NameRule());
        RuleFor(static c => c.Total).GreaterThan(0).When(static c => c.Strategy == PricingStrategy.Fixed);
    }
}
```

### 9.5 — Command / query handler

```csharp
// ❌ WRONG — handler с private Ensure / Build методами
public sealed class CreateOrderHandler(IOrderRepository repo, TimeProvider clock) : ICommandHandler<CreateOrderCommand, OrderId>
{
    public async ValueTask<OrderId> HandleAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        await EnsureNameUnique(cmd.Name, ct);  // ← private
        var order = BuildOrder(cmd);           // ← private
        await repo.AddAsync(order, ct);
        return order.Id;
    }

    private async Task EnsureNameUnique(string name, CancellationToken ct) { ... }
    private Order BuildOrder(CreateOrderCommand cmd) => Order.Create(...);
}
```

```csharp
// ✅ CORRECT — handler orchestration only
public sealed class CreateOrderHandler(
    IOrderRepository repo,
    IOrderFactory factory) : ICommandHandler<CreateOrderCommand, OrderId>
{
    public async ValueTask<OrderId> HandleAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = factory.Create(cmd);  // factory бросит DomainException при нарушении инвариантности
        await repo.AddAsync(order, ct);
        return order.Id;
    }
}

// Application/Orders/OrderFactory.cs
internal sealed class OrderFactory : IOrderFactory
{
    public Order Create(CreateOrderCommand cmd) => Order.Create(
        new CustomerId(cmd.CustomerId),
        cmd.Name,
        cmd.StartDate,
        cmd.EndDate,
        cmd.Total,
        ...);
}
```

### 9.6 — Adapter / external SDK wrapper

```csharp
// ❌ WRONG — клиент с private URL builder'ом, маппером и 15 OkStatus fixture'ами
public sealed class PaymentsGatewayClient(IHttpClientFactory http) : IPaymentsGatewayClient
{
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(..., CancellationToken ct)
    {
        var url = BuildUrl("products");  // ← private
        var response = await http.CreateClient().GetAsync(url, ct);
        return MapToProductList(response);  // ← private
    }

    private static string BuildUrl(string path) => $"https://api/v1/{path}";
    private static ProductDto MapToProductList(HttpResponseMessage response) { ... }
    private static ApiResponse<X> OkStatus() => new(HttpStatusCode.OK, default!);  // ← 15 копий
    // ... ещё 12 приватных OkXxx-хелперов
}
```

```csharp
// ✅ CORRECT — клиент orchestration; URL builder / маппер / fixtures — отдельно
public sealed class PaymentsGatewayClient(
    IHttpClientFactory http,
    IPaymentsGatewayUrlBuilder urls,
    IProductResponseMapper mapper) : IPaymentsGatewayClient
{
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(..., CancellationToken ct)
    {
        var response = await http.CreateClient().GetAsync(urls.Products(), ct);
        return mapper.ToProductList(response);
    }
}

// Adapter/Helpers/PaymentsGatewayUrlBuilder.cs
internal sealed class PaymentsGatewayUrlBuilder : IPaymentsGatewayUrlBuilder
{
    public Uri Products() => new("https://api/v1/products", UriKind.Absolute);
}

// Adapter/Mappers/ProductResponseMapper.cs
internal sealed class ProductResponseMapper : IProductResponseMapper { ... }

// Tests/PaymentsResponseFixtures.cs (file static)
file static class PaymentsResponseFixtures
{
    public static ApiResponse<T> Ok<T>(T payload) => new(HttpStatusCode.OK, payload);
}
```

В **stub'ах** (например `StubPaymentsGatewayClient`) fixture'ы тоже не
`private static ApiResponse OkStatus()` 15 раз. Один `PaymentsResponseFixtures.Ok(...)`
в `PaymentsResponseFixtures.cs` (file static).

### 9.7 — SQL / ADO.NET port implementation

```csharp
// ❌ WRONG — IProductContextLookup с inline ADO.NET + SQL literal + 3 private helpers
public sealed class ProductContextLookup(
    OrdersDbContext dbContext,
    ISettingResolver settingResolver) : IProductContextLookup
{
    private const string Sql = """ SELECT ... FROM orders.products p ... """;  // ← magic strings

    public async Task<ProductContextSnapshot?> GetAsync(string productId, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);  // ← private
        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        AddParameter(command, "@productId", productId, DbType.String);  // ← private
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var snapshot = await ReadSnapshotAsync(reader, ct);  // ← private

        if (snapshot.Account?.Id is { } accountIdValue)
        {
            var accountId = ObjectId.Parse(accountIdValue.Value!.ToString());  // ← round-trip
            var settings = await settingResolver.ResolveAsync(
                new SettingScope.ForAccount(accountId), ct);
            snapshot = snapshot with { Settings = settings };
        }

        return snapshot;
    }

    private static async Task EnsureOpenAsync(NpgsqlConnection connection, CancellationToken ct) { ... }
    private static void AddParameter(NpgsqlCommand command, string name, object value, DbType type) { ... }
    private static async Task<ProductContextSnapshot> ReadSnapshotAsync(NpgsqlDataReader reader, CancellationToken ct) { ... }
}
```

```csharp
// ✅ CORRECT — port orchestration only; SQL/ADO.NET/проекция — отдельные helper'ы
public sealed class ProductContextLookup(
    IProductContextSnapshotReader reader,    // SQL + ADO.NET + row→snapshot
    ISettingResolver settingResolver) : IProductContextLookup
{
    public async Task<ProductContextSnapshot?> GetAsync(string productId, CancellationToken ct)
    {
        var snapshot = await reader.ReadAsync(productId, ct);
        if (snapshot is null) return null;
        return snapshot.Account?.Id is { } accountId
            ? snapshot with { Settings = await settingResolver.ResolveForAccountAsync(accountId, ct) }
            : snapshot;
    }
}

// Persistence/Helpers/ProductContextSnapshotReader.cs
internal sealed class ProductContextSnapshotReader(
    IDbConnectionFactory connectionFactory) : IProductContextSnapshotReader
{
    private const string Sql = $"""
                                SELECT
                                    p.id                AS product_id,
                                    o.id                AS order_id,
                                    cu.id               AS customer_id,
                                    ac.id               AS account_id,
                                    ac.marketplace_id   AS account_marketplace_id,
                                    mk.id               AS marketplace_id
                                FROM {OrdersDbContext.Schema}.{DatabaseInformation.Tables.Products} p
                                INNER JOIN {OrdersDbContext.Schema}.{DatabaseInformation.Tables.Orders} o
                                    ON o.id = p.order_id
                                LEFT JOIN {CustomersDbContext.Schema}.{CustomersDbInformation.Tables.Customers} cu
                                    ON cu.id = o.customer_id
                                ...
                                WHERE p.id = @productId
                                """;  // SQL собран через DatabaseInformation, не литералы

    public async Task<ProductContextSnapshot?> ReadAsync(string productId, CancellationToken ct) { ... }
}

// Persistence/Helpers/NpgsqlCommandExtensions.cs
internal static class NpgsqlCommandExtensions
{
    public static void AddStringParameter(this NpgsqlCommand command, string name, string value) { ... }
}

// Persistence/Helpers/NpgsqlConnectionExtensions.cs
internal static class NpgsqlConnectionExtensions
{
    public static async Task EnsureOpenAsync(this NpgsqlConnection connection, CancellationToken ct) { ... }
}
```

Заодно выполняется `ef-core.md` §1 (no magic strings): SQL собирается через
константы `DatabaseInformation.Tables.*` / `Schemes.*`, а не литералы.

### 9.8 — Validator (`AbstractValidator<T>`)

```csharp
// ❌ WRONG — Validator с private проверками (нарушает anti-patterns.md §4)
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator(IOrderRepository repo)
    {
        RuleFor(static c => c.Name).NotEmpty();
        RuleFor(static c => c.Name).MustAsync(BeUniqueAsync).WithMessage("Name must be unique.");
        RuleFor(static c => c.EndDate).Must(EndAfterStart).WithMessage("End date must not precede start date.");
    }

    private async Task<bool> BeUniqueAsync(string name, CancellationToken ct) =>  // ← private
        !await repo.NameExistsAsync(name, ct);

    private static bool EndAfterStart(CreateOrderCommand cmd) =>  // ← private
        cmd.EndDate is null || cmd.EndDate >= cmd.StartDate;
}
```

```csharp
// ✅ CORRECT — правила inline + переиспользуемые Rule-объекты
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(static c => c.Name).SetValidator(new NameRule());
        RuleFor(static c => c.EndDate).GreaterThanOrEqualTo(static c => c.StartDate)
            .When(static c => c.EndDate.HasValue)
            .WithMessage("End date must not precede start date.");
    }
}

// Уникальность имени — отдельная политика домена, не validator:
//   IOrderUniquenessPolicy.EnsureUniqueAsync(name, ct)
// вызывается из handler'а до AddAsync, не прячется в private MustAsync.
```

### 9.9 — Seeder (`ISeeder`)

```csharp
// ❌ WRONG — Seeder с private EnsureXxx методами
public sealed class SampleDataSeeder(...)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var mk = await EnsureMarketplaceAsync(db, ct);          // ← private
        var account = await EnsureAccountAsync(db, mk.Id, ct);  // ← private
        var team = await EnsureDefaultTeamAsync(db, account.Id, ct);  // ← private
        var customer = await EnsureCustomerAsync(db, account.Id, team.Id, ct);  // ← private
        ...
    }

    private async Task<Marketplace?> EnsureMarketplaceAsync(...) { ... }
    private async Task<Account?> EnsureAccountAsync(...) { ... }
    private async Task<Team?> EnsureDefaultTeamAsync(...) { ... }
    private async Task<Customer?> EnsureCustomerAsync(...) { ... }
    // ещё 5 приватных EnsureXxx
}
```

```csharp
// ✅ CORRECT — каждый Ensure-блок в `EnsureXxxFixture.cs` с собственными зависимостями
public sealed class SampleDataSeeder(
    MarketplaceFixture marketplaceFixture,
    AccountFixture accountFixture,
    TeamFixture teamFixture,
    CustomerFixture customerFixture,
    OrderFixture orderFixture,
    LineItemFixture lineItemFixture,
    ProductFixture productFixture) : ISeeder
{
    public string Name => "samples";

    public async Task SeedAsync(CancellationToken ct)
    {
        var mk = await marketplaceFixture.EnsureAsync(ct);
        var account = await accountFixture.EnsureAsync(mk.Id, ct);
        var team = await teamFixture.EnsureDefaultAsync(account.Id, ct);
        var customer = await customerFixture.EnsureAsync(account.Id, team.Id, ct);
        ...
    }
}

// Seeders/Fixtures/MarketplaceFixture.cs
internal sealed class MarketplaceFixture(CustomersDbContext db)
{
    public async Task<Marketplace?> EnsureAsync(CancellationToken ct) { ... }
}

// Seeders/Fixtures/AccountFixture.cs
internal sealed class AccountFixture(CustomersDbContext db)
{
    public async Task<Account?> EnsureAsync(MarketplaceId mkId, CancellationToken ct) { ... }
}
```

### 9.10 — Static helpers / parsers

```csharp
// ❌ WRONG — handler с private static parser
public sealed class SkuParserHandler(...)
{
    public async ValueTask<...> HandleAsync(...) {
        var parts = ParseSku(command.Sku);  // ← private static
        ...
    }

    private static (string Category, string Variant) ParseSku(string sku) { ... }
}
```

```csharp
// ✅ CORRECT — extension method в file static class
public sealed class SkuParserHandler(...)
{
    public async ValueTask<...> HandleAsync(...) {
        var parts = command.Sku.ParseSkuParts();  // extension
        ...
    }
}

// Helpers/SkuExtensions.cs
file static class SkuExtensions
{
    public static (string Category, string Variant) ParseSkuParts(this string sku)
    {
        var idx = sku.IndexOf('_');
        return (sku[..idx], sku[(idx + 1)..]);
    }
}
```

### Self-audit шаг для LLM (обязателен перед коммитом)

```bash
# По всему diff'у — найти ВСЕ private методы в добавленных/изменённых файлах
git diff --name-only --diff-filter=AM | xargs -I {} \
  rg -n '^\s*private (static )?(async )?\w+ [A-Z]\w+\s*\(' {}

# Должно быть пусто. Если найдено — выноси в helper/extension/static file.
```

Полный grep по `src/` для ревьюера:

```bash
# Все private методы (исключаем поля, константы, override, explicit interface impl):
rg -n '^\s*private ' src/ -g '*.cs' \
  | rg -v 'private (sealed )?(class|record)\b' \
  | rg -v 'private (const|static readonly|readonly)\b' \
  | rg -v 'override\b' \
  | rg -v '_[A-Z]\w*\s*='
```

Каждый результат = потенциальное нарушение §9. Действия:

1. Вынести в `internal sealed` helper в той же папке.
2. Или в `file static class` (если нет зависимостей).
3. Или extension method в `*Extensions.cs`.
4. Если это всё-таки contract override — добавить `override` keyword
   (после `override` modifier'а уже не `private`).

### Reviewer checklist

При code review класса, в котором есть `private` метод (не override):

- [ ] Это contract override (`Dispose`/`DisposeAsync`/`Configure`/`OnModelCreating`/`ExecuteAsync`/`ExecuteCycleAsync`/`HandleAsync`/...)? Если да — `override` keyword присутствует?
- [ ] Если нет — почему метод в этом классе, а не в helper/extension/separate service?
- [ ] Можно ли его изолированно протестировать без внешних зависимостей класса?
- [ ] Не нарушает ли это `anti-patterns.md` §4 (валидация) или `class-layout-and-tooling.md` §1a (оркестрация)?
- [ ] Не маскирует ли он god-object (класс > 200 строк, много private методов)?
- [ ] Не использует ли он magic strings (literals), которые должны быть в `DatabaseInformation` / `*Constants`?

Если на любой вопрос "нет" / "не знаю" — просить вынести.

### Hard rule для LLM

> **Если ты (LLM) написал `private` метод в production-классе и это не
> `override` — ты нарушил §9. Исправь ДО коммита. Self-audit grep выше —
> это Definition of Done, не опция.**

### Существующий техдолг (forward-only)

Это правило **forward-only**. Существующие `private` методы остаются как
есть до момента их естественного рефакторинга в рамках задачи, которая
их трогает. При любом изменении файла, содержащего `private` метод, LLM
**обязан** прогнать self-audit и привести файл к §9 в рамках этой задачи.

> **Awareness:** конкретный список pre-existing нарушений — per-repo. Если
> он ведётся, он живёт в `<repo>/.planning/` или repo-local правиле, не здесь.
> В этом проекте на старте кодовой базы нарушений §9 нет.

При следующем touch файла с `private`-методом — вынести в helper / extension
/ static file в рамках той же задачи.

---

## 10. No `#region` directives — ЗАПРЕЩЕНЫ во всех C#-файлах

`#region` / `#endregion` запрещены. В проекте 0 таких директив по конвенции.

**Почему — анти-паттерн:**
1. Прячут структуру файла от outline-режима IDE.
2. Поощряют раздувание класса (Helpers-регион длиннее 3 методов = сигнал).
3. Шумят в diff-ах.
4. Ломают source generators и рефакторинги.

**Что делать вместо региона:**

| Случай | Решение |
|--------|---------|
| "Group constructors" | primary ctor + members по visibility |
| "Helpers" | вынести в отдельный тип (mapping → Mapperly, validation → validator) |
| "Constants" | `file static class` рядом с потребителем (см. `constructors-and-fields.md`) |
| "Properties" | порядок по роли, или value object |
| Большой файл | partial по responsibility (last resort) — сначала спросить "не отдельный класс ли это?" |

**Exemptions:** `.g.cs` / `.Designer.cs` (сгенерированные) — вне редактирования.

**Enforcement:** convention + `worker-audit.md`. Build-gate regex на
литеральный токен `#region` не добавлен пока проект в чистом состоянии.

---

## 11. `ThrowIf*` argument checks — ЗАПРЕЩЕНЫ в этом проекте

**Hard rule.** Никаких `ArgumentNullException.ThrowIfNull`,
`ArgumentException.ThrowIfNullOrEmpty`,
`ArgumentException.ThrowIfNullOrWhiteSpace` или аналогичных.
Ни в public API, ни в private helpers, ни в extension-методах.

**Причина.** `<Nullable>enable</Nullable>` включён глобально через
`Directory.Build.props`. Когда параметр объявлен non-nullable (`string x`,
не `string? x`), компилятор **уже** enforced non-null на каждом call-site.
Runtime-проверка — duplicate noise, и это ложь о контракте: она намекает
caller'у, что метод может принимать null, хотя компилятор это уже запретил.

### Banned BCL helpers — полный список

- `ArgumentNullException.ThrowIfNull(x)` — banned
- `ArgumentNullException.ThrowIfNullOrEmpty(x)` — banned
- `ArgumentException.ThrowIfNullOrEmpty(x)` — banned
- `ArgumentException.ThrowIfNullOrWhiteSpace(x)` — banned
- `ArgumentOutOfRangeException.ThrowIfNull*` / `ThrowIfNegative*` / аналогичные
  range/null helpers — banned по той же причине (валидация range — caller'а
  ответственность, не вызываемого метода)

Принцип: **trust the signature**. Если параметр non-nullable и
не-empty/non-whitespace — это контракт caller'а, не обязанность метода
перепроверять.

### Что делать вместо `ThrowIf*`

- **Non-null reference type** (`string x`, `IUserRepository repo`) — компилятор
  enforced non-null at every call-site. Ничего не нужно.
- **`string?` / nullable reference type** — параметр **может** быть null по
  контракту. Не бросайте `ArgumentNullException`, бросьте **доменное
  исключение со стабильным `Code`** (`exceptions.md` §3 +
  `error-mapping.md` §2):

  ```csharp
  // ✅ Correct — nullable<T> → nullable<T> или domain exception, не ThrowIfNull
  public sealed User? FindById(UserId id, CancellationToken cancellationToken = default)
  {
      // optional input — let it through, return null when not found
  }

  // или, если null — programmer error в вызывающем коде:
  public sealed User RequireById(UserId id, CancellationToken cancellationToken = default)
  {
      return FindById(id, cancellationToken)
          ?? throw new IdentityException("user.not_found", $"user {id} not found");
  }
  ```
- **Empty/whitespace string check** — это **бизнес-инвариант**, не контракт
  метода. Если «пустая строка» ломает поведение, это (a) `default` в caller'е
  (compile-error на `default(string)`), либо (b) ошибка на стороне caller'а.
  Валидируйте **в слое FluentValidation** (`anti-patterns.md` §4), не
  повторно в каждом методе.

```csharp
// ❌ Wrong — компилятор уже отверг null. ThrowIf* — шум + ложь о контракте.
public static IServiceCollection AddFooCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(services);       // ← duplicate
    ArgumentNullException.ThrowIfNull(configuration);  // ← duplicate
    ArgumentException.ThrowIfNullOrEmpty(name);        // ← caller должен передать не-пустое
    // ...
}

// ❌ Wrong — в extension-методе.
public static string Ok(this string text)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(text);    // ← шум
    return $"[ok]{text}[/]";
}

// ✅ Correct — trust the signature
public static IServiceCollection AddFooCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddOptions<FooOptions>().Bind(...);
    // ...
}
```

**Когда всё-таки можно.** Только в **nullable-oblivious** boundary:

- Reflection — `MethodInfo.Invoke`, `Activator.CreateInstance`.
- Interop / P/Invoke — unmanaged код не nullable-aware.
- Serialization — десериализаторы конструируют объект в обход ctor.
- External library скомпилированная с `<Nullable>disable`.

В этих случаях добавить комментарий с обоснованием:

```csharp
// boundary: invoked via reflection, compiler cannot enforce nullability
ArgumentNullException.ThrowIfNull(instance);
```

Внутри solution (везде `<Nullable>enable</>`) для внутренних вызовов
таких границ нет. **Не используйте ThrowIf* в production-коде этого
репозитория, кроме boundary.**

**Enforcement:** convention + `worker-audit.md`. CA1062 в `.editorconfig`
`none` — проект уже решил, что валидация аргументов избыточна под nullable.

**Self-audit grep (перед коммитом):**

```bash
rg -n 'Argument(Null|Exception|OutOfRangeException)\.ThrowIf' src/ tests/
```

Должно быть пусто. Существующие исключения в pre-existing коде —
forward-only: каждый PR, который трогает файл, должен удалить
`ThrowIf*` из него. **В новой работе — никаких исключений.**
Единственное разрешённое исключение — `// boundary:` комментарий
для nullable-oblivious код (reflection/interop/serialization).

---

## 12. Builder pattern — content struct для состояния, не private fields

Билдеры с `Set*` методами, которые мутируют `private` поля —
анти-паттерн. Поле спрятано за методом, но метод просто присваивает.
Используйте `internal sealed class` с auto-properties для
состояния, а методы билдера делайте тонкой обёрткой над ним.

**Почему:**

1. **Нет `this`-capture церемонии.** Метод становится
   `Content.X = Y;` вместо `this._x = Y;`. Данные и API — разные
   объекты.
2. **Состояние тестируется / инспектируется изолированно.** Unit-тест
   может создать `BuilderContent` напрямую и assert'ить против него,
   не прогоняя каждый метод билдера.
3. **Контент передаётся в helpers без билдера.** `Build()` может
   принять content параметром — проще рассуждать, чем через `this`.
4. **`internal`/`file` доступ не загрязняет public API.** Контент
   не торчит наружу как часть публичной поверхности.
5. **Auto-properties `{ get; set; }` вместо public fields** —
   соответствует остальному коду (DTO / entity используют
   auto-properties), соблюдает `CA1051` (visible fields should be
   encapsulated), и не нарушает правила нейминга/стиля,
   выставленные в `.editorconfig`.

```csharp
// ❌ Old — private fields, this-capture в каждом setter
public sealed class CliBuilder
{
    private string? toolName;
    private string? toolVersion;
    private string? clusterName;

    public CliBuilder Name(string name)
    {
        toolName = name;       // ← this._toolName = name;
        return this;
    }
}

// ✅ New — internal content struct, auto-properties, collection init через []
internal sealed class CliContent
{
    public string? ToolName { get; set; }
    public string? ToolVersion { get; set; }
    public string? ClusterName { get; set; }
    public List<Action<IConfigurator>> PendingConfigurations { get; set; } = [];
}

public sealed class CliBuilder
{
    public CliContent Content { get; } = new();

    public CliBuilder Name(string name)
    {
        Content.ToolName = name;
        return this;
    }
}
```

**Инициализация коллекций — `[]`, не `new()`** (C# 12 collection
expressions). Работает для `T[]`, `List<T>`, `IEnumerable<T>`, любого
типа с `Add` методом. Для непустой инициализации тоже —
`= [1, 2, 3]` вместо `= new() { 1, 2, 3 }`.

### Visibility rules

| Что | Где живёт | Access |
|-----|-----------|--------|
| **Content struct/class** | Тот же файл или `Abstractions/` рядом с билдером | `internal sealed` (по умолчанию), `file sealed` если только в одном файле |
| **Свойства на content** | На content | `public { get; set; }` — auto-property, не field. Внутри internal scope это ОК. |
| **Builder** | Public API | `public sealed` |
| **Helper-метод на билдере** | Public API | `public` |
| **File-local helper** | Один файл | `file static class` или `file sealed class` |

**Никаких public fields.** Даже на internal content-типах —
auto-properties. Это правило проекта (CA1051 + стиль).

### Параметр-object для many-arg методов

Если метод принимает 3+ связанных параметра — оборачивайте их в
record/struct и передавайте одной переменной:

```csharp
// ❌ Many positional parameters
public void Render(string toolName, string toolVersion, string? clusterName, string? nodeName);

// ✅ Parameter object
public sealed record FooterRequest(string ToolName, string ToolVersion, string? ClusterName, string? NodeName);
public void Render(FooterRequest request);
```

Record может быть `internal sealed` если метод internal, `public sealed`
если метод public.

### `file` scope для локальных хелперов

Если helper-тип используется только в одном файле — объявляйте
`file sealed class` / `file static class`. Компилятор enforces:
никакой consumer вне файла не может его reference. Это
предотвращает «utils» / «helpers» папки, в которых живут
shared-утилсы, которые никто не хочет выносить в отдельный модуль.

```csharp
file static class CliBuilderHelpers
{
    // только для CliBuilder в этом файле
    public static void ApplyContent(this CliContent content, CommandApp app) { ... }
}
```

### Self-audit grep

```bash
# По всему src/ — найти билдеры с private field + setter pattern
rg -n 'private\s+(string|int|long|bool|Guid|List<|Dictionary<)\s+_\w+\s*[=;]' src/ --type cs

# Должно показать только DTO/entity (где private поля — норма) и
# не билдеры. Если в билдере private поле + setter — рефактор на Content.

# Также — найти public fields в content (если кто-то пропустил):
rg -n 'public\s+(string|int|long|bool|Guid|List<|Dictionary<)[?!]?\s+\w+\s*;' src/ --type cs
```

---

## Связанные правила

- `naming-and-types.md` — naming, sealed, record vs class
- `constructors-and-fields.md` — primary ctor, fields, constants
- `class-layout-and-tooling.md` — XML docs, model placement, required tooling
- `async-and-tasks.md` — async/await
- `anti-patterns.md` — enum anti-patterns, tuple ban, validation