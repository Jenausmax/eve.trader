---
description: c# async/await, Task vs ValueTask, CancellationToken — асинхронность
globs: ["**/*.cs"]
always: true
---

# Async / await

Этот файл — асинхронность целиком: паттерны, типы, токены отмены,
обработка ошибок. Naming (async suffix) — в `naming-and-types.md` §1.
ConfigureAwait ban — там же.

## 1. Async suffix — REQUIRED, без исключений

Все методы с `Task` / `ValueTask` / `Task<T>` / `ValueTask<T>` в return
type оканчиваются на `Async`. Метод `DoAsync` без await — всё равно `Async`.

```csharp
public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
{
    return repository.CountAsync(cancellationToken);
}

public async Task<User> GetUserByIdAsync(
    Guid userId,
    CancellationToken cancellationToken = default)
{
    // ...
}
```

**Enforcement:** VSTHRD200 (severity=error).

---

## 2. `Task` vs `ValueTask`

- **`Task<T>`** — операция всегда асинхронная (IO, БД, HTTP).
- **`ValueTask<T>`** — операция **может** быть синхронной (кеш-хит, материализованное значение).

```csharp
// ✅ ValueTask — кеш может вернуть синхронно
public async ValueTask<User> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
{
    if (cache.TryGet(userId, out var cached))
    {
        return cached;
    }

    return await loader.LoadAsync(userId, cancellationToken);
}
```

---

## 3. `ConfigureAwait(false)` — ЗАПРЕЩЁН в app code

В .NET 8+ async/await не имеет накладных расходов на `SynchronizationContext`
capture в ASP.NET Core (где `SynchronizationContext` отсутствует).
Добавление `.ConfigureAwait(false)` — лишний шум без пользы.

```csharp
// ❌ Wrong — устаревший паттерн из .NET 4.x
await repository.GetUserAsync(id, cancellationToken).ConfigureAwait(false);

// ✅ Correct — голый await, .NET 8+ оптимизирован
await repository.GetUserAsync(id, cancellationToken);
```

**Где `ConfigureAwait(false)` ещё оправдан** (в этом проекте не встречается):
- Library code (NuGet packages).
- Bcl analyzers (CA2007) — **требуют** для libraries.

**Каноническая позиция проекта:** app code (ASP.NET Core, console hosts,
workers) — `await` без `ConfigureAwait`.

**Enforcement:** запрет держится ревьюером / self-audit. Ни `MA0004`, ни
`CA2007` здесь не работают: оба требовали бы *наличия* `ConfigureAwait` —
обратное запрету, — поэтому оба `severity = none`.

Анализатором это не закрыть, и это не упущение: и `CA2007`, и `MA0004`
требуют **наличия** `ConfigureAwait` — ровно обратного запрету. Поэтому оба
стоят в `none`, а не потому, что до них не дошли руки.

Чем запрет держится — решает потребитель. Известные варианты два: ревью с
self-audit'ом либо собственный гейт сборки, обычно MSBuild-таргет с regex по
`.ConfigureAwait(`. Второй надёжнее, но это уже строчка в сборочных файлах
конкретного репозитория, поэтому описывается у него, а не здесь.

---

## 4. CancellationToken — последний параметр везде

Все методы с IO принимают `CancellationToken cancellationToken = default`
**последним параметром**.

```csharp
public async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
{
    return await repository.GetByIdAsync(userId, cancellationToken);
}
```

Прокидывай `cancellationToken` во **все** вложенные async-вызовы. Никаких
`Task.Delay(...)` без токена.

---

## 5. Async запреты

**Запрещено** в app code:

```csharp
// ❌ .Result — dead-lock риск, VSTHRD103
var user = repository.GetUserAsync(id).Result;

// ❌ .Wait() — то же самое
repository.GetUserAsync(id).Wait();

// ❌ .GetAwaiter().GetResult() — то же самое
repository.GetUserAsync(id).GetAwaiter().GetResult();
```

**Enforcement:** VSTHRD103 (severity=error).

---

## 6. `_ =` бессмысленный discard — ЗАПРЕЩЁН в любой форме

Префикс `_ =` для отбрасывания значения, которое и так игнорируется,
— шум. Делает код длиннее без пользы. **Любая** из этих форм запрещена:

```csharp
// ❌ Wrong — discard поверх await (await уже ждёт синхронно)
_ = await db.Users.AddAsync(user, ct);
_ = await db.SaveChangesAsync(ct);
_ = await transaction.CommitAsync(ct);

// ❌ Wrong — discard на fluent-chain return (AddXxx возвращает коллекцию,
//    этот вызов её не использует)
_ = services.AddSingleton<IFoo, Foo>();
_ = services.AddScoped<IBar, Bar>();

// ❌ Wrong — discard синхронного выражения
_ = DateTimeOffset.UtcNow + offset;     // вычислил, не использовал
_ = ComputeSomething();                  // то же самое
_ = "literal" + var;                     // string concat без эффекта

// ❌ Wrong — discard параметра, который ни на что не влияет
_ = configuration;                       // placeholder для будущего binding
_ = services;                            // return-параметр extension метода,
                                          // метод возвращает его явно
```

**Правильный код:**

```csharp
// ✅ Bare await (результат не нужен)
await db.Users.AddAsync(user, ct);
await db.SaveChangesAsync(ct);
await transaction.CommitAsync(ct);

// ✅ Bare call (return value мутатора / fluent не используется)
services.AddSingleton<IFoo, Foo>();
services.AddScoped<IBar, Bar>();

// ✅ Bare выражение (результат не нужен)
DateTimeOffset.UtcNow + offset;
ComputeSomething();
```

**Только легитимные случаи `_ =` (с обязательным комментарием-обоснованием):**

```csharp
// ✅ Допустимо — true fire-and-forget (async-вызов, результат не нужен
//    и НЕ ждём завершения). На hot path, где ожидание дорого.
//    В app code крайне редко — почти всегда await правильнее.
_ = telemetryClient.FlushAsync(ct); // fire-and-forget; не критично, если потеряем

// ✅ Допустимо — async-лямбда передаётся как Action (не Task).
_ = SomeFuncReturningTask().ContinueWith(...);
```

**Почему это особенно плохо читается:** `_ =` визуально сигнализирует
«fire-and-forget» даже когда await делает ровно противоположное —
синхронно ждёт. Следующий читатель тратит 30 секунд на вопрос
«подожди, я же await'нул, зачем тут discard?» Префикс надо убрать,
не объяснять.

**Исключения (НЕ нужен `_ =` discard):**
- Зарезервированный параметр (`IConfiguration configuration` в
  Installer'е, который понадобится при Options binding) — не используй
  `_ = configuration;`. Вместо этого либо используй параметр (даже в
  комментарии-маркере), либо убери параметр. Если параметр обязан
  быть в сигнатуре — переименуй в `unused` или используй
  `[SuppressMessage]`. Не пиши `_ = x;`.
- Возврат значения из extension метода — пиши `return services;`
  явно, не `_ = services;`.

**Самопроверка перед коммитом:**
```bash
rg -n '_ = ' src/ --type cs | grep -v 'binary literal|comment'
```
Не должно быть production-кода. Только оправданные fire-and-forget
с комментарием.

**Enforcement:** convention + `process/worker-audit.md`. Roslyn не считает
explicit discard error'ом (синтаксически валиден), поэтому ловит
только reviewer или self-audit grep (если в проекте есть review-скрипт —
он проверяет тот же паттерн).

---

## Связанные правила

- `naming-and-types.md` — naming (async suffix)
- `code-shape.md` — общий code shape
- `di-lifetimes.md` — DI lifetimes для scoped/transient сервисов