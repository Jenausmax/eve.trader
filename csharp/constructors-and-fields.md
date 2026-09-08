---
description: c# primary constructors, fields, file static constants — конструкторы и поля
globs: ["**/*.cs"]
always: true
---

# Primary constructors, fields, constants

Этот файл — правила для конструкторов, полей и констант. Naming/sealed/records — в `naming-and-types.md`. Pattern matching / var / braces — в `code-shape.md`.

## Primary constructors — REQUIRED для всех sealed-классов

**Default:** primary constructor для **всех** sealed-классов — DI-классы
(сервисы, контроллеры, репозитории, worker'ы) **и** обычные non-DI
классы (state-holders, registries, value-type wrappers, parsing
helpers, anything else). Если у класса нет параметров — пиши
`public sealed class Foo();` (empty primary ctor) вместо пустого тела.

**Исключения (только эти):**
1. Нужна **валидация параметров** в конструкторе.
2. **Side-effects** в конструкторе (singleton init, lazy init).
3. **Интерфейсная иерархия** с разными ctor'ами (наследник выбирает base-ctor).
4. **Generic base with constraints** (`where T : class, new()` и т.п.),
   когда constraints должны проверяться в explicit ctor.

Все 4 исключения покрывают конкретный edge-case — **не** используй
explicit ctor "потому что лень переписывать".

### Когда класс — **не** DI

Для non-DI классов правило выглядит так:

```csharp
// ✅ Correct — primary ctor даже для non-DI. Empty primary ctor ОК,
//    когда у класса нет конструктивных параметров.
public sealed class FilterableEntityRegistry()
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<UntypedFilterableField>> byTypeName = new(StringComparer.Ordinal);

    // ...methods reference byTypeName напрямую...
}

// ✅ Correct — primary ctor с default-arg инициализацией, когда нужен
//    только default экземпляр поля. Сам DI регистрирует как singleton.
public sealed class FilterableEntityRegistry(
    ConcurrentDictionary<string, IReadOnlyList<UntypedFilterableField>> byTypeName = new(StringComparer.Ordinal))
{
}

// ❌ Wrong — explicit ctor без уважительной причины
public sealed class FilterableEntityRegistry
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<UntypedFilterableField>> byTypeName = new(StringComparer.Ordinal);

    public FilterableEntityRegistry() { }  // ← empty ctor бесполезен; либо primary, либо удалить
}
```

Правило применяется к **каждому** sealed-классу, не только DI. Если
поля в классе нет вообще (один-два метода) — пустой primary ctor
просто делает явным "у класса нет параметров".

### Параметр primary constructor — это уже поле

Не дублируй в `private readonly`. Параметр разворачивается в скрытое
backing-поле, доступен по имени в любом методе. Создавать отдельное
поле (с подчёркиванием или без) и копировать значение — двойное
хранение без выгоды.

```csharp
// ✅ Correct — primary constructor, обращение по имени параметра
public sealed class UserService(IUserRepository repository, ILogger<UserService> logger)
{
    public Task<User?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return repository.GetByIdAsync(id, cancellationToken);
    }
}

// ❌ Wrong — primary ctor + дублирующее поле
public sealed class UserService(
    IUserRepository repository,
    ILogger<UserService> logger)
{
    private readonly IUserRepository repository = repository; // ← дубликат
    private readonly ILogger<UserService> _logger = logger;   // ← дубликат
}

// ❌ Wrong — explicit ctor + private readonly + xml-doc на ctor
public sealed class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly ILogger<UserService> _logger;
    /// <summary>Constructs a new UserService.</summary>
    public UserService(IUserRepository repository, ILogger<UserService> logger) { ... }
}
```

### Имя параметра primary ctor — PascalCase (для class), camelCase (для record)

Параметр primary ctor — это по сути обычный параметр конструктора.
Выбор naming convention зависит от того, объявлен ли тип через
**record** (который пробрасывает свойства наружу) или через **class**
(свойство не создаётся автоматически).

**Class с primary ctor** — параметр живёт как backing field, наружу
не выбрасывается. Поэтому Camel-case в стиле обычного параметра:

```csharp
// ✅ Correct — class: параметр camelCase, как в любом конструкторе
public sealed class UserService(IUserRepository repository, ILogger<UserService> logger)
{
    public Task<User?> GetAsync(Guid id, CancellationToken token = default)
        => repository.GetByIdAsync(id, token);
}

// ❌ Wrong — class: PascalCase параметр. Это не record, поле не экспонируется.
public sealed class UserService(IUserRepository Repository, ILogger<UserService> Logger)
{
    // ...
}
```

**Record с primary ctor** — параметр становится публичным свойством с
init-only setter. Поэтому PascalCase, как у других свойств record'а:

```csharp
// ✅ Correct — record: PascalCase, пробрасывается как свойство
public sealed record UserSummary(Guid Id, string Name, string Email);

// ❌ Wrong — record: camelCase ломает convention record'а
public sealed record UserSummary(Guid id, string name, string email);
```

Всё производное от этого правила:
- XML doc `<param name="repository">` (camelCase) для class, `<param name="Id">` (PascalCase) для record.
- Self-init property в class: `public ILogger Logger { get; } = logger;` (PascalCase, потому что public API). Внутри class имя параметра остаётся camelCase, а assigned property должен иметь другое имя или быть без `private readonly`.

**Class, хранящий primary ctor параметр как mutable property** — нарушение другого правила ("private readonly остаётся только когда..."). Если нужно экспонировать — лучше auto-property без primary ctor или инициализация в методе lifecycle.
```

### `private readonly` остаётся **только** когда:

- нужна **мутируемая** внутренняя мутация (`private int counter`).
- значение **производное** от параметра и должно жить как фиксированное
  поле (кеш, precompiled regex, default-initialized StringBuilder).
- поле **не из primary ctor** (сетится обычным методом в lifecycle).

```csharp
// ✅ Correct — derived field, default-init, immutable
public sealed class FilterParser(int maxParenDepth = 32)
{
    // maxParenDepth — параметр primary ctor, доступен по имени в методах.
    // maxDepth — derived field с фиксированным init (копия параметра),
    //            помечена как derived-allowed.
    private readonly int maxDepth = maxParenDepth;

    public bool TryParse(...) { /* ... */ }
}
```

### Self-audit grep — перед коммитом

```bash
# Sealed class без primary ctor + private readonly field = подозрительно
# (либо explicit ctor для валидации/side-effect, либо нарушение).
rg -nB2 'class \w+' src/ --type cs   | rg -A1 '^\s*private readonly' | head -20

# Конкретно: sealed class без () сразу после имени (т.е. без primary ctor).
rg -n 'public sealed class \w+(?!\()' src/ --type cs
#   → Каждый результат = потенциальное нарушение. Проверить вручную:
#   если класс наследуется (base ctor) или explicit ctor нужен для DI / валидации — ОК.
#   иначе — переписать на primary ctor (с default-arg если non-DI).

# Дублирующее поле: primary ctor param + private readonly с тем же именем
rg -n 'private readonly \w+ (_\w+|\w+) = \w+' src/ --type cs
#   → Каждый результат = потенциальное нарушение (исключения см. выше).

# Имя параметра primary ctor: PascalCase в class (вместо camelCase).
# Правило: class → camelCase, record → PascalCase.
rg -n 'public (?:sealed )?class \w+\(' src/ --type cs
#   → Для каждого результата проверь параметры — camelCase.
#   Пример нарушения: 'public sealed class UserService(IUserRepository Repository, ...)' —
#   'Repository' должно быть 'repository' (это class, не record).
```

**Enforcement:** convention + code review + self-audit grep. Нет
analyzer для дублирующих полей (CA1852 / RCS1163 не покрывают
private readonly копирование).
**Test:** при виде `private readonly X _x = primaryCtorParam` —
удалить поле. При виде `public sealed class X` без `()` после имени —
переписать на primary ctor.

### `<remarks>` для однострочного описания ctor

Когда primary constructor нуждается в пояснении — `<remarks>` на классе,
не `<summary>` на конструкторе.

```csharp
/// <summary>OTel-backed counter wrapping <see cref="Counter{T}" />.</summary>
/// <remarks>Constructs from an OTel <see cref="Counter{T}" /> instance.</remarks>
public sealed class OpenTelemetryCounter(Counter<double> counter) : ITelemetryCounter
{
    public void Add(double value) => counter.Add(value);
}
```

### Pyramid Rule (short → long)

Параметры primary constructor сортируются **по длине типа: короткий → длинный**.

```csharp
// ✅ Correct — IUserRepository (короче) первым
public sealed class UserService(IUserRepository repository, ILogger<UserService> logger) { ... }

// ❌ Wrong — порядок нарушен
public sealed class UserService(ILogger<UserService> logger, IUserRepository repository) { ... }
```

При равной длине — алфавитный порядок.

---

## Private fields — NO underscore prefix

**Предпочитай auto-properties или primary constructor.**

Поле без подчёркивания — только если нужна custom-логика геттера/сеттера
или поле нельзя выразить через primary constructor.

```csharp
// ✅ Best — primary constructor (см. выше)

// ✅ Auto-property когда нужно
public sealed class UserService
{
    public ILogger<UserService> Logger { get; }
}

// ✅ Private field без подчёркивания — если нужна custom-логика
public sealed class Counter
{
    private int currentValue;

    public int Increment() => Interlocked.Increment(ref currentValue);
}

// ❌ Wrong
private readonly string _someValue;
```

---

## Constants — `file static class`, не `public const` на классе поведения

Строковые идентификаторы (имена кук, хедеров, секций конфига, ключей
options, префиксов схем) живут в `file static class <Name>Constants` в том
же файле, где используются.

```csharp
// MyFilter.cs
file static class MyFilterConstants
{
    public const string HeaderName = "X-My-Header";
    public const string CookieName = "my.cookie";
    public const string BearerPrefix = "Bearer ";
}

public sealed class MyFilter : IFilterMetadata
{
    public async Task OnExecutionAsync(/* ... */)
    {
        if (request.Headers.Authorization.ToString()
            .StartsWith(MyFilterConstants.BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // ...
        }
    }
}
```

**Почему:**
1. Константа не торчит на классе, который про другое.
2. File-scope не даёт зацепиться извне (`using static` не работает на file).
3. Парные константы (cookie + header) — в одном `file static class`.
4. Точный blast radius при рефакторинге.

**Когда `file static class`:**
- Имя cookie / header (antiforgery, auth, telemetry).
- Префиксы схем (`Bearer `).
- Имя секции конфига (`Auth:Cookie`).
- Ключ options.
- Любая wire-format строка.

**Когда обычный `public const` / enum:**
- Публичный API-контракт (breaking change при правке).
- Enum-члены (`AuthSchemes.Cookie`).
- Магические числа без строковой семантики.
- Доменные константы (`MaxRetryCount = 5`).

**C# 11+** разрешает несколько `file static class` с одинаковым именем в
разных файлах — каждый `file` ограничивает область одним файлом.

```csharp
// MyFilter.cs
file static class MyFilterConstants { public const string BearerPrefix = "Bearer "; }

// MyOptions.cs
file static class MyFilterConstants { public const string HeaderName = "X-My-Header"; }
```

### Anti-patterns

```csharp
// ❌ Wrong — public const на классе поведения
public sealed class AntiforgeryFilter : IFilterMetadata
{
    public const string HeaderName = "RequestVerificationToken"; // торчит наружу
    public const string CookieName = "app.xsrf";
}

// ❌ Wrong — общий Constants.cs на полпроекта
internal static class Constants
{
    public const string AntiforgeryCookieName = "app.xsrf";
    public const string AuthHeaderName = "Authorization";
    public const string OutboxPollInterval = "2000";
    // 40 полей, 5 модулей, непонятно кто меняет
}

// ❌ Wrong — литерал в коде
if (request.Headers.Authorization.ToString().StartsWith("Bearer ", ...));
```

---

## Связанные правила

- `naming-and-types.md` — naming, sealed, record vs class, type references
- `code-shape.md` — pattern matching, var, braces, no #region, no ThrowIfNull
- `anti-patterns.md` — JsonSerializerOptions (тоже про константы)