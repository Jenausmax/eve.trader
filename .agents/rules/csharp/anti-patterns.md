---
description: c# anti-patterns — enum-with-behavior, enum-as-command, switch sprawl, records DTO placement, tuples ban, fluentvalidation, [FromServices], JsonSerializerOptions
globs: ["**/*.cs"]
always: true
---

# Anti-patterns — ЗАПРЕТЫ и обязательные замены

Этот файл — то, что **нельзя** делать, и обязательные замены. Покрывает
самые частые нарушения, которые LLM делает по умолчанию.

Naming/sealed/records-as-types — в `naming-and-types.md`. Code shape
(pattern matching, var, braces) — в `code-shape.md`. Async — в `async-and-tasks.md`.

## 1. Enum anti-patterns — три формы

Domain enum — закрытый набор меток (status, type, scope, op). Становится
антипаттерном в трёх повторяющихся ситуациях.

### 1.1 Enum-with-behavior → извлечь value object или extension

Enum несёт **неявные данные** (длительность, размер, лимит) и вынуждает
потребителей писать `switch(enum)` чтобы их восстановить.

```csharp
// ❌ Wrong — QuotaCheckPeriod + PeriodHours вшит в xml-комментарии
public enum QuotaCheckPeriod
{
    /// <summary>24 hours.</summary> Day = 0,
    /// <summary>168 hours (7 days).</summary> Week = 1,
}
public sealed record QuotaCheckSetting
{
    public required QuotaCheckPeriod Period { get; init; }
    public int PeriodHours => Period == QuotaCheckPeriod.Day ? 24 : 168;
}

// И дубль в QuotaContributors.cs:
return setting.Period == QuotaCheckPeriod.Day ? 24 : 168;
```

Два потребителя, два одинаковых switch-а, длительность живёт не там где
должна.

**Замена (минимум-churn):**

```csharp
// ✅ Behavior на enum — extension method
public static class QuotaCheckPeriodExtensions
{
    public static int AsHours(this QuotaCheckPeriod p) => p switch
    {
        QuotaCheckPeriod.Day  => 24,
        QuotaCheckPeriod.Week => 168,
        _ => throw new ArgumentOutOfRangeException(nameof(p), p, null),
    };
}
```

**Замена (когда длительность — реальный domain-инвариант):**

```csharp
// ✅ Value object
public sealed record QuotaCheck(QuotaCheckPeriod Period)
{
    public TimeSpan AsTimeSpan() => Period switch
    {
        QuotaCheckPeriod.Day  => TimeSpan.FromHours(24),
        QuotaCheckPeriod.Week => TimeSpan.FromHours(168),
        _ => throw new ArgumentOutOfRangeException(...),
    };
}
```

**Выбирай A** когда enum остаётся wire-формат меткой (DTO / EF column).
**Выбирай B** когда длительность часть бизнес-инвариантов.

### 1.2 Enum-as-command → один command на операцию

Controller принимает `enum Action` и диспатчит через большой switch —
flag-based dispatch hub, не доменная операция. Каждый `case` концептуально
другая команда, но они слиты и не могут быть авторизованы, валидированы,
аудитированы независимо.

```csharp
// ❌ Тот же switch в OrderHandlers.cs
public async ValueTask<Unit> HandleAsync(ApplyOrderStatusCommand command, ...)
{
    switch (command.Action)
    {
        case OrderStatusAction.SubmitForReview: ...
        case OrderStatusAction.Approve:             ...
        case OrderStatusAction.Reject:              ...
        case OrderStatusAction.Pause:               ...
        case OrderStatusAction.Resume:              ...
        case OrderStatusAction.Complete:            ...
    }
}
```

**Замена — каждая операция = свой command/request/handler:**

```csharp
public sealed record ApproveOrderCommand(OrderId Id, string ReviewerNote)
    : ICommand<Unit>;
public sealed class ApproveOrderHandler(OrderRepository repo, ...) : ...
{
    public ValueTask<Unit> HandleAsync(ApproveOrderCommand c, ...) { ... }
}
```

Wire-format (HTTP) может всё ещё expose один endpoint, принимающий verb в
body — controller парсит verb и диспатчит в нужный command. Это controller
concern, не domain.

### 1.3 Switch sprawl → полиморфизм

`switch(enum)` с 4+ ветками, каждая из которых **выполняет логику**, не
просто `return X`. Добавление нового enum-значения молча ломает switch
(нет compile-time reminder). Каждый новый caller копирует switch.

**Замена — state pattern:**

```csharp
public abstract class OrderState
{
    public abstract OrderState SubmitForReview(Order c);
    public abstract OrderState Approve(Order c, string note);
    public abstract OrderState Reject(Order c, string reason);
    public abstract OrderState Pause(Order c);
}
public sealed class DraftState : OrderState { ... }
public sealed class ActiveState : OrderState { ... }

// В handler — никакого switch:
public async ValueTask<Unit> HandleAsync(ApproveOrderCommand c, ...)
{
    var order = await repo.GetAsync(c.Id, ct);
    order.ApplyTransition(Transition.Approve(c.ReviewerNote));
    await repo.SaveAsync(order, ct);
}
```

**Rule of thumb.** Если `switch(myEnum)` имеет 4+ веток и каждая больше
2 строк логики (не просто `return X`) — это state-machine smell. Новое
значение enum должно требовать **новый код**, не редактирование существующего
switch.

### 1.4 Когда enum — это ОК

- Closed set labels без поведения — `Currency`, `AccountStatus`, `FolderScope`.
- Wire-format / EF column — JSON `status: "Active"`, integer column.
- `[Flags]` bitmask — `Permissions { Read = 1, Write = 2, ... }`.

Правило срабатывает когда enum несёт **поведение** или **диспатчит workflow**.
Plain labels остаются enum-ами.

---

## 2. Records DTO — отдельный файл, не в controller

Request/Response record-DTO (wire-format) **никогда** не объявляются внутри
controller-файла. Каждый record — отдельный файл, в `Application/Models/`
подпапке соответствующего модуля.

```csharp
// ❌ Wrong — record в конце controller-файла
[ApiController]
[Route($"{ApiRoutes.Base}/workspaces")]
public sealed class WorkspacesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost(Name = "workspaces-create")]
    public async Task<...> CreateAsync(...) { ... }
    // ... 6 action-методов ...
}

public sealed record CreateWorkspaceRequest(ObjectId AccountId, string Name);
public sealed record CreateWorkspaceResponse(ObjectId Id);
public sealed record EnableWorkspaceFeatureRequest(string FeatureKey);
public sealed record GetWorkspaceResponse(ObjectId Id, string Name, DateTimeOffset CreatedAt);
```

```csharp
// ✅ Correct — record в Application/Models/
// Application/Models/CreateWorkspaceRequest.cs
namespace App.Modules.Accounts.Application.Models;

public sealed record CreateWorkspaceRequest(ObjectId AccountId, string Name);
```

**Почему:**
1. Размер файла — OrdersController с 7 records = 350+ строк, тяжело навигировать.
2. Поиск — хочешь найти `CreateOrderRequest` — идёшь в `Models/`.
3. Cohesion records — `CreateXxxRequest` + `CreateXxxResponse` живут парой.
4. Reuse — record в отдельном файле, import одинаковый из любого места.
5. Wire-format stability — OpenAPI генератор читает тип из одного места.

**Namespace:** `App.Modules.<X>.Application.Models`.
**Имя файла = имя типа** в PascalCase.

**Enforcement:** convention + `worker-audit.md`. Нет analyzer'а.
**Test:** `grep -nE '^public (sealed )?record ' <Controller>.cs` — должно быть пусто.

---

## 3. Tuples — ЗАПРЕТ в user code

`ValueTuple` / `(TypeA Name1, TypeB Name2)` is banned in **all** user code
— not just public API. No tuples in field declarations, local variables,
method returns, parameters, or anywhere else. Use a named type (`record`,
`class`) instead.

```csharp
// ❌ Wrong — tuple anywhere: anonymous, no semantics, no behaviour
public (ApiKey Key, string Plaintext) IssueApiKey(string name) { ... }

// ❌ Wrong — tuple in a private helper
private static (User User, Address Address) MapPair(Row r) { ... }

// ❌ Wrong — tuple in a field
private readonly (int X, int Y) origin = (0, 0);

// caller:
var (key, plaintext) = user.IssueApiKey("bot");
// Что такое plaintext? Имя не говорит.

// ✅ Correct — record with named type and meaningful field names
public sealed record IssuedApiKey(ApiKey Key, string Plaintext)
{
    public string MaskedPlaintext => $"{Plaintext[..4]}...";  // behaviour!
}

public IssuedApiKey IssueApiKey(string name) { ... }
```

**Почему tuples — антипаттерн в user code:**
1. Анонимность — `(ApiKey, string)` не имеет имени типа.
2. Нет семантики — `string` во второй позиции — что это?
3. Не расширяется — добавить третье поле = breaking change.
4. Нет поведения — record может иметь computed properties.
5. Сериализация — `System.Text.Json` пишет `{"Item1":..., "Item2":...}`.
6. Локальные tuple-variable дают `(string, int) foo` без имени — читающий
   не знает что это, какой Item1 vs Item2.

**Где tuples допустимы (вне user code):**
- **Third-party generated shapes** (Refit's `IApiResponse<T>` etc. — not
  user code, can't change). Wrap at the boundary in a named type.
- LINQ projections inside a single method are **discouraged** but not
  banned by static checker — prefer extracting a `record` projection type
  when the projection spans more than 2 lines or is reused.

```csharp
// ❌ Discouraged — projection unnamed; survives only inside this method
var pairs = items.Select(item => (item.Id, item.Name))
                 .Where(p => p.Name.Length > 0);

// ✅ Better — extract a named projection
public sealed record ItemPair(Guid Id, string Name);
var pairs = items.Select(item => new ItemPair(item.Id, item.Name))
                 .Where(p => p.Name.Length > 0);
```

**Test:** `grep -nE '\([A-Z][A-Za-z0-9_]+\s+[A-Z][A-Za-z0-9_]+[,)]'` src/
— должно быть пусто (1 hit per line is a parse, not a tuple).

### Collection return types — `IReadOnlyCollection<T>` by default

Public API surface returns the **most-restrictive read-only type** the consumer
needs; never expose mutable collections (`List<T>`, `IList<T>`). `List<T>` exposes
mutable surface — every consumer can `.Add()`, `.Clear()`, reorder, and break
invariants the producer assumed.

| What the consumer needs | Return type |
|---|---|
| Iteration only | `IEnumerable<T>` (use with care — re-enumerates the source) |
| Iteration + `Count` | `IReadOnlyCollection<T>` |
| Iteration + `Count` + index | `IReadOnlyList<T>` |
| Membership check (O(1)) | `IReadOnlySet<T>` / `HashSet<T>` |
| Fixed set, max perf | `T[]` |

`List<T>` lives **only** inside a method body for local mutation; the returned
surface is read-only. Full matrix + examples in `code-shape.md` §7.

---

## 4. Validation — два слоя, FluentValidation, переиспользуемые правила

**Валидация входных данных — только через FluentValidation.**
`ModelState.AddModelError` запрещён **полностью** — никаких исключений,
даже для `[FromQuery]` cross-field.

### Два слоя

| Слой | Что проверяет | Как | Когда |
|------|--------------|-----|-------|
| **FluentValidation** (Application) | Structural — non-empty, max length, format, range | `AbstractValidator<T>` + `ValidationBehavior` в CQRS pipeline | До handler'а |
| **Domain factory** (Domain) | Semantic — business invariants, state transitions | `throw DomainException` | Внутри aggregate |

### Когда валидатор ОБЯЗАН

```csharp
// ✅ ОБЯЗАН — есть user-input поля
public sealed record CreateOrderCommand(
    ObjectId CustomerId,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal Amount)
    : ICommand<OrderId>;

// ✅ НЕ НУЖЕН — только ID
public sealed record DeleteOrderCommand(OrderId Id) : ICommand<Unit>;
```

**Правило:** если command содержит хотя бы одно **user-editable** поле —
валидатор обязан.

### Переиспользуемые правила — два механизма

**Механизм 1: Rule-объекты** (`AbstractValidator<T>` + `SetValidator`).
Для правил которые **переиспользуются 3+ раз** и имеют собственные
константы. Живут в `Kernel/Cqrs/Validation/Rules/`.

```csharp
// src/shared/App.Shared.Kernel/Cqrs/Validation/Rules/NameRule.cs
public sealed class NameRule : AbstractValidator<string>
{
    public const int MaxLength = 256;

    public NameRule()
    {
        RuleFor(name => name)
            .NotEmpty()
            .WithMessage("name must not be empty or whitespace.")
            .MaximumLength(MaxLength)
            .WithMessage($"name must be {MaxLength} characters or fewer.");
    }
}
```

Использование — одна строка:

```csharp
public sealed class CreateWorkspaceValidator : AbstractValidator<CreateWorkspaceCommand>
{
    public CreateWorkspaceValidator()
    {
        RuleFor(static command => command.Name).SetValidator(new NameRule());
    }
}
```

**Механизм 2: Extension methods.** Для простых параметризуемых правил.

```csharp
// src/shared/App.Shared.Kernel/Cqrs/Validation/ValidationExtensions.cs
public static class ValidationExtensions
{
    public static IRuleBuilderOptions<T, string> MustBeAllowedVerb<T>(
        this IRuleBuilder<T, string> rule, IReadOnlySet<string> allowed)
    {
        return rule
            .NotEmpty()
            .Must(allowed.Contains)
            .WithMessage(x => $"Unknown action '{x}'. Expected one of: {string.Join(", ", allowed)}.");
    }
}
```

### ModelState — ЗАПРЕЩЁН полностью

```csharp
// ❌ Wrong — даже для query params
if (to < from)
{
    ModelState.AddModelError("to", "'to' must not precede 'from'.");
    return ValidationProblem(ModelState);
}
```

Решение — обернуть query params в request record + FluentValidation:

```csharp
public sealed record OrderDailyQuery(
    ObjectId OrderId,
    DateOnly From,
    DateOnly To);

public sealed class OrderDailyQueryValidator : AbstractValidator<OrderDailyQuery>
{
    public OrderDailyQueryValidator()
    {
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("'to' must not precede 'from'.");
    }
}
```

В controller валидатор нужен **ровно одному endpoint** — через `[FromServices]`,
не в конструктор:

```csharp
public sealed class ReportingController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("orders/{id}/daily")]
    public async Task<ActionResult<StatsReport>> GetDailyAsync(
        ObjectId id,
        [FromQuery] OrderDailyQuery query,
        [FromServices] IValidator<OrderDailyQuery> validator,
        CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(query, ct);
        return Ok(await dispatcher.SendAsync(new GetDailyStatsQuery(id, query.From, query.To), ct));
    }
}
```

### Регистрация

Каждый Application модуль регистрирует валидаторы:

```csharp
_ = services.AddValidatorsFromAssembly(
    typeof(<Module>ApplicationInstaller).Assembly,
    includeInternalTypes: true);
```

**Файл валидатора:** `<ValidatedType>Validator.cs`, рядом с валидируемым типом.

**Test:** `grep -rnE 'ModelState\.AddModelError' src/modules/.../Endpoints` — должно быть пусто.

---

## 5. Endpoint-specific dependencies — `[FromServices]`

Сервис, который нужен **только одному endpoint** в controller, не
инжектируется через конструктор. Он берётся через `[FromServices]` на
параметре action-метода.

```csharp
// ❌ Wrong — splitQuery нужен только PostSplitQueryAsync
public sealed class ReportingController(
    IDispatcher dispatcher,
    ISplitQueryService splitQuery,
    IValidator<OrderDailyQuery> validator1,
    IValidator<MetricQueryRequest> validator2)
    : ControllerBase

// ✅ Correct — только общие зависимости в конструкторе
public sealed class ReportingController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("orders/{id}/daily")]
    public async Task<ActionResult<StatsReport>> GetDailyAsync(
        ObjectId id,
        [FromQuery] OrderDailyQuery query,
        [FromServices] IValidator<OrderDailyQuery> validator,
        CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(query, ct);
        // ...
    }

    [HttpPost("split/query")]
    public async Task<ActionResult<SplitQueryResponse>> PostSplitQueryAsync(
        [FromBody] SplitQueryRequest request,
        [FromServices] ISplitQueryService splitQuery,
        CancellationToken ct = default)
    {
        return Ok(await splitQuery.ExecuteAsync(request, ct));
    }
}
```

**Когда что:**

| Где | Что |
|-----|-----|
| **Конструктор controller'а** | Сервисы которые нужны **2+ endpoints** (обычно `IDispatcher`) |
| **`[FromServices]` на action** | Сервис который нужен **ровно 1 endpoint** |

**Что НЕ переносить в `[FromServices]`:**
- `IDispatcher` — нужен почти каждому endpoint.
- Логгер — если нужен везде.
- Configuration / Options — если влияют на все endpoints.

**Порядок параметров в action-методе:**

```csharp
public async Task<ActionResult<T>> SomeActionAsync(
    [FromRoute] ObjectId id,               // 1. route params
    [FromBody] SomeRequest request,         // 2. body DTO
    [FromQuery] SomeQuery query,            // 3. query params
    [FromServices] ISomeService service,    // 4. injected services
    CancellationToken cancellationToken = default)  // 5. cancellation token (всегда последний)
```

---

## 6. `JsonSerializerOptions` — `Web`, иначе DI, а синглтон только без DI

`JsonSerializerOptions` — частая точка расхождения в проекте. Каждый
файл изобретает по-своему: `new JsonSerializerOptions()` инлайн,
`private static readonly` на классе, `JsonSerializerOptions.Default`,
`(JsonSerializerOptions?)null`, shared singleton field.

**Правило: не создавай `new JsonSerializerOptions(...)` в app code.**

### Tier 1 — `JsonSerializerOptions.Web` (framework, кэширован)

.NET 9+ предоставляет **frozen, cached, shared** instance:

```csharp
JsonSerializerOptions Web { get; }   // camelCase + case-insensitive + AllowReadingFromString
```

**Когда использовать `.Web`** — стандартная web/API сериализация без
project-specific converters.

**`JsonSerializerOptions.Web` — frozen.** Не вызывай `.Converters.Add()`,
не присваивай `PropertyNamingPolicy`. Это shared instance: мутация
повлияет на всех потребителей в процессе.

### App-level customisation — через DI configuration, не shared singletons

Когда нужны custom converters (`ObjectIdJsonConverter`,
`JsonStringEnumConverter` для enum-DTO) — настраивай **через DI**, не через
shared singleton fields:

```csharp
// ✅ ASP.NET Core — composition root
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new ObjectIdJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// ✅ Не-ASP.NET (background workers / libraries) — Microsoft.AspNetCore.Http.Json
builder.Services.Configure<JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new ObjectIdJsonConverter());
});
```

DI-конфигурация применяется автоматически к consumers через
`IOptions<JsonOptions>` / встроенные хуки ASP.NET Core — один источник
правды, без singleton поля.

### Там, где DI нет — именованный синглтон рядом с типом

Запрет «только через DI» неисполним в сборках, куда контейнер не доходит:
сборка контрактов, доменный слой, обработчики, работающие до и вне запроса.
Требовать там DI — значит требовать невозможного, а невозможное правило не
соблюдают, его обходят молча.

Поэтому: если DI в этой сборке недоступен, заводится ОДИН именованный
синглтон, и он лежит рядом с типом, который сериализует:

```csharp
// ✅ сборка контрактов — контейнера здесь нет по построению
internal static class SettingsJson
{
    public static JsonSerializerOptions Instance { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        options.Converters.Add(new ObjectIdJsonConverter());
        return options;
    }
}
```

Условия, все три обязательны:

- `internal`, либо `file`-класс, если потребитель один. `public` синглтон
  превращается в скрытую точку расширения, которую никто не контролирует;
- лежит рядом с сериализуемым типом, а не в общем «Json»-складе: иначе два
  потребителя одного контракта разъедутся настройками незаметно;
- **не** мутируется после создания. Собран в `Build()` и заморожен.

Признак, что синглтон заведён зря: в этой же сборке есть установщик DI.
Тогда это не «DI недоступен», а «не захотели искать композиционный корень».

### Hard bans

- **NEVER** `new JsonSerializerOptions(...)` в app code, кроме
  `new JsonSerializerOptions(JsonSerializerDefaults.Web)` как base для
  DI-конфигурации в composition root.
- **NEVER** мутировать `JsonSerializerOptions.Web` (shared instance).

### Anti-patterns

```csharp
// ❌ JsonSerializerOptions.Default — PascalCase, ломает camelCase wire-format
JsonSerializer.Serialize(rule.Conditions, JsonSerializerOptions.Default);

// ❌ Inline new JsonSerializerOptions(JsonSerializerDefaults.Web) в call-site
await JsonSerializer.SerializeAsync(stream, payload,
    new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);

// ❌ Inline new JsonSerializerOptions { ... } с конфигурацией
await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new ObjectIdJsonConverter() }
}, cancellationToken);

// ❌ Shared singleton field (custom converters leak across types — multiple
//   consumers with different converter needs collide on the same instance;
//   configure via DI `Configure<JsonOptions>` instead)
internal static class OutboxJsonOptions
{
    public static JsonSerializerOptions Instance { get; } = new()
    {
        Converters = { new ObjectIdJsonConverter() }
    };
}

// ❌ Мутация JsonSerializerOptions.Web (shared!)
JsonSerializerOptions.Web.Converters.Add(new ObjectIdJsonConverter());

// ❌ private static readonly на классе потребителя
public sealed class CountryRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new() { ... };
}

// ❌ (JsonSerializerOptions?)null в аргументе
JsonSerializer.Serialize(set, (JsonSerializerOptions?)null);
```

### Что НЕ покрывается правилом

- `services.AddControllers().AddJsonOptions(...)` в composition root —
  единственное легитимное место для `AddJsonOptions(...)` configuration.
- `new JsonSerializerOptions(JsonSerializerDefaults.Web)` в `RefitSettings.ContentSerializer`
  — единственный легитимный случай inline `new`, потому что Refit API ждёт
  именно instance. Wrap в `JsonSerializerOptions.Web` после настройки,
  если появятся custom converters — через `services.Configure<JsonOptions>`.

**Test:** `grep -rnE 'new JsonSerializerOptions\b' src/ --include='*.cs'`
— должно быть пусто (composition root использует `AddJsonOptions`, не `new`).

---

## Связанные правила

- `naming-and-types.md` — sealed, record vs class
- `code-shape.md` — pattern matching, var, braces
- `api-design.md` — controllers, endpoints, DTOs
- `analyzers.md` — analyzer packages