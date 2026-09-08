---
description: di service lifetimes — singleton/scoped/transient, captive dependency, http client factory, disposable services
globs: ["**/*.cs"]
always: true
---

# DI — service lifetimes

Этот файл — правила выбора lifetime и защита от captive dependency.
Installer pattern — в `di-installer.md`. Options — в `di-options.md`.

## 1. Три lifetime — что значат в runtime

| Lifetime | Сколько экземпляров | Когда создаётся | Когда диспозится |
|----------|---------------------|-----------------|------------------|
| **Singleton** | 1 на всё приложение | При первом резолве (lazy) или при `BuildServiceProvider()` | При shutdown приложения |
| **Scoped** | 1 на DI scope | При первом резолве в scope | При `Dispose()` scope (обычно конец HTTP-запроса) |
| **Transient** | N — новый на каждый inject | При каждом резолве | См. §6 disposable transient |

---

## 2. Singleton — DEFAULT

**Default для большинства сервисов. Если нет конкретной причины делать
иначе — singleton.**

### Когда Singleton

1. **Stateless сервисы** — нет mutable полей.
2. **Concurrent-safe state** — `ConcurrentDictionary`, `ImmutableList`, `Channel<T>`, `Interlocked`.
3. **Конфигурация и Options** — `IOptions<T>` всегда singleton.
4. **Дорогая инициализация** — precompiled regex, expression trees, JSON-serializer.

```csharp
services.AddSingleton<ITotalCalculator, TotalCalculator>();
services.AddSingleton(TimeProvider.System);
```

### Требования к Singleton

- **Thread safety обязательна.** Singleton используется конкурентно.
- **Не зависит от Scoped или Transient** (см. §5 captive dependency).

### НЕ Singleton для

- Сервисов с per-request state (текущий пользователь, correlation ID).
- Сервисов, использующих `DbContext` напрямую.
- Сервисов, держащих mutable state без синхронизации.

---

## 3. Scoped — для DbContext и per-request state

Scoped используется реже singleton, но всегда явно — там, где scope
имеет смысл.

### Когда Scoped

1. **`DbContext`** — это правило EF Core, не выбор. `DbContext` не
   thread-safe, держит change tracker per-request, обязан быть scoped.

   ```csharp
   services.AddDbContext<OrdersDbContext>(/* ... */);
   ```

2. **Сервисы, использующие `DbContext`** — repositories, query services, handlers.

   ```csharp
   services.AddScoped<IOrderRepository, OrderRepository>();
   services.AddScoped<IOrderService, OrderService>();
   ```

3. **Per-request state** — `ICurrentUserContext`, `IRequestContext`.

### Scoped в Worker (вне HTTP)

Вне HTTP — создавай scope вручную через `IServiceScopeFactory`:

```csharp
public sealed class OrderArchiveWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

            await orderService.ArchiveExpiredAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

---

## 4. Transient — самый редкий выбор

### Когда Transient

1. **Builders с накапливающимся состоянием** — каждому потребителю нужен свежий экземпляр.
2. **Mediator handlers** (MediatR-style) — каждое сообщение получает свой handler.
3. **`IDisposable` с lifetime короче чем scope** — explicit creation через factory.

### Transient — самый дорогой по аллокациям

Не используй для горячего пути обработки запросов.

---

## 5. Captive dependency — ГЛАВНАЯ ОШИБКА

**Singleton не может зависеть от Scoped или Transient.** Singleton при
первом создании захватит **один** экземпляр scoped/transient и будет
держать его до конца жизни приложения.

```csharp
// ❌ Wrong — singleton принимает scoped DbContext
services.AddSingleton<IOrderArchiver, OrderArchiver>();
services.AddDbContext<OrdersDbContext>(/* ... */);   // Scoped по умолчанию

public sealed class OrderArchiver(OrdersDbContext dbContext)
{
    // dbContext будет тот же самый между разными запросами
    // → state leaks, race conditions, stale change tracker
}
```

Результат: тот же `DbContext` живёт сколько и приложение. Change tracker
накапливает entities, между запросами видны изменения других, рано или
поздно `ObjectDisposedException` или `InvalidOperationException`.

**Решение — `IServiceScopeFactory`:**

```csharp
// ✅ Correct
services.AddSingleton<IOrderArchiver, OrderArchiver>();

public sealed class OrderArchiver(IServiceScopeFactory scopeFactory)
{
    public async Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var expiredOrders = await dbContext.OrdersSet
            .Where(order => order.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-30))
            .ToListAsync(cancellationToken);

        // ...

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

### Защита от captive dependency

В Development включай валидацию scope при сборке container:

```csharp
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
    options.ValidateOnBuild = true;
});
```

- **`ValidateScopes`** — singleton не зависит от scoped.
- **`ValidateOnBuild`** — резолвит все сервисы при `Build()`. Ловит missing dependencies.

В Production `ValidateScopes` отключаем — стоит производительности. Но
`ValidateOnBuild` оставляем — это разовая проверка при старте.

---

## 6. Disposable services

| Lifetime | Когда `Dispose()` |
|----------|-------------------|
| Singleton | При shutdown приложения |
| Scoped | При завершении scope (конец HTTP-запроса) |
| Transient | При shutdown root scope — если зарегистрирован напрямую |

Последний пункт — источник утечек.

```csharp
// ❌ Wrong — все DisposableWorker живут до shutdown
services.AddTransient<DisposableWorker>();

// ✅ Correct — factory, освобождение управляется потребителем
services.AddSingleton<Func<DisposableWorker>>(_ => () => new DisposableWorker());

// Альтернатива: явное создание
public sealed class OrderProcessor
{
    public async Task ProcessAsync()
    {
        await using var worker = new DisposableWorker();
        await worker.DoAsync();
    }
}
```

**Исключение:** `DbContext` — `IDisposable`, но регистрируется через
`AddDbContext` как Scoped. Корректно: `Dispose()` в конце HTTP-запроса.

---

## 7. HTTP clients — особый случай

**Никогда не делай `new HttpClient()` руками** и не регистрируй
`services.AddSingleton<HttpClient>()`.

- Singleton `HttpClient` не обновляет DNS.
- `new HttpClient()` на каждый запрос приводит к **port exhaustion**.

**Используй Refit** через `AddAppRefitClient<T>` (single extension;
see `http-resilience-refit.md`):

```csharp
services.AddAppRefitClient<IPublicApiClient>(options.BaseUrl);
// internally: AddRefitClient<T>().AddStandardResilienceHandler() + bearer handler
```

- `IPublicApiClient` — это Refit interface с `[Get]`/`[Post]` атрибутами.
- `AddHttpClient<TInterface, TImpl>()` (typed-client factory) **deprecated**
  — write a Refit interface instead. See `http-resilience-refit.md` §1.
- `IHttpClientFactory` остаётся internal plumbing: рефлексия через
  `AddRefitClient<T>` под капотом его использует, но app code не
  инжектирует `IHttpClientFactory` напрямую.

---

## 8. Quick reference matrix

| Сценарий | Lifetime | Пример |
|----------|----------|--------|
| Calculator / Validator / Mapper (stateless) | Singleton | `TotalCalculator`, `OrderValidator` |
| Thread-safe кеш | Singleton | `ProductPriceCache` |
| `IOptions<T>` | Singleton | автоматически |
| `TimeProvider` | Singleton | `AddSingleton(TimeProvider.System)` |
| Дорогая инициализация | Singleton | `Regex` с `RegexOptions.Compiled` |
| `DbContext` | Scoped | через `AddDbContext<T>` |
| Repository / QueryService | Scoped | использует `DbContext` |
| Business service с persistence | Scoped | использует Repository |
| `ICurrentUserContext` | Scoped | per-request данные |
| Builder с состоянием | Transient | `QueryBuilder`, `PdfReportBuilder` |
| Mediator handler | Transient | per-message обработка |
| `HttpClient` потребитель | Transient через Refit + `AddAppRefitClient<T>` | `PublicApiClient` (Refit interface) |
| `DbContext` в singleton | Через `IServiceScopeFactory` | hosted service / worker |
| `IDisposable` transient | Через factory `Func<T>` | избегаем утечки |

---

## Связанные правила

- `di-installer.md` — Installer pattern, composition root
- `di-options.md` — IOptions pattern