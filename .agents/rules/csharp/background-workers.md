---
description: >-
  authoring background workers via ScheduledWorkerBase — mandatory patterns for
  Schedule choice (Interval/Cron/AdaptivePoll/Startup), DI scope-per-cycle,
  reflection-based counter emission via [Counter] attribute, error policy,
  cancellation, test infrastructure
globs:
  - src/**/Workers/**/*.cs
  - src/**/BackgroundServices/**/*.cs
  - src/**/ScheduledWorkerBase*.cs
  - src/**/*ScheduledWorker*.cs
priority: high
interactive: false
always: false
---

# Background workers — ScheduledWorkerBase pattern

**Все новые** periodic / startup background workers ОБЯЗАНЫ наследовать от
проектного `ScheduledWorkerBase` (обычно
`<Solution>.Shared.Kernel/BackgroundServices/ScheduledWorkerBase.cs`), а не от
`BackgroundService` напрямую. PR без соответствия отклоняется.

> В проекте, где такой базы ещё нет, первый periodic worker её и заводит — с
> формой, описанной ниже. Точные имена типов и путь объявляются в правилах
> проекта; здесь — контракт.

## Зачем base class

Без `ScheduledWorkerBase` каждый worker сам реализует:
- `while (!ct.IsCancellationRequested)` loop + `Task.Delay` / cron wait
- per-cycle DI scope (`await using var scope = services.CreateAsyncScope();`)
- per-cycle telemetry (`using var op = diagnostics.Operation(...).Build();`)
- cancellation propagation (`try { } catch (OperationCanceledException) {}`)
- start/stop logging
- manual error policy + counter emission (`diagnostics.X.WithTag(...).Add(1)`)

Десяток воркеров в кодовой базе делали это каждый по-своему. Base class
сокращает 100+ строк boilerplate per worker до одной строки
`protected override WorkerSchedule Schedule => ...;`.

## Анатомия worker'а

```csharp
public sealed class DailyReconciliationWorker(
    IServiceProvider services,                    // ← root only; scope per cycle в base
    TimeProvider clock,
    IOptions<CronOptions> cron,
    IDomainDiagnostics diagnostics,
    ILogger<DailyReconciliationWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
{
    private readonly CronSchedule schedule =
        new(cron.Value.DailyReconciliation, TimeZoneInfo.Utc);

    protected override string WorkerName => nameof(DailyReconciliationWorker);

    protected override WorkerSchedule Schedule => schedule;

    protected override async Task<object?> ExecuteCycleAsync(
        WorkerContext context, CancellationToken cancellationToken)
    {
        var accounts = context.GetService<IAccountRepository>();  // scoped
        // бизнес-логика возвращает ICycleCounters record (или null)
        return new DailyReconciliationResult(Processed: 10, Failed: 1, Skipped: 0);
    }
}
```

Business-only код в `ExecuteCycleAsync`, всё остальное в base.

## Шесть обязательных паттернов

### 1. Schedule — выбор формы

| Когда | Что | Cron expression vs Interval? |
|-------|-----|-------------------------------|
| Периодическая работа без привязки к "wall clock" (раз в 5 мин, очистка каждый час) | `IntervalSchedule(TimeSpan)` | НЕ cron |
| Работа привязана к времени суток/недели (02:00 daily, по воскресеньям) | `CronSchedule(expr, TimeZoneInfo)` | ДА cron |
| DB-poll: "drain as fast as possible когда есть работа, sleep когда пусто, sleep при ошибке" | `AdaptivePollSchedule(Idle, Busy, Error)` | НЕ cron |
| One-shot на старте хоста (миграции EF, schema guard, warmup cache) | `StartupSchedule.Instance` | one-time |

Антипаттерн: комбинировать несколько тасок в один worker (каждое — отдельный).

### 2. Worker — return `ICycleCounters` record

```csharp
public sealed record SyncResult(
    [property: Counter("processed")] int Processed,
    [property: Counter("failed")] int Failed,
    [property: Counter("skipped")] int Skipped = 0)
    : ICycleCounters, ISkippedCounters;

protected override async Task<object?> ExecuteCycleAsync(
    WorkerContext context, CancellationToken ct)
{
    // business; collect counters
    return new SyncResult(Processed: 10, Failed: 1, Skipped: 2);
    // null = no counters for this cycle
}
```

`base` рефлексирует свойства с `[Counter]` и эмитит:
- `worker.{WorkerName}.{CounterName}` — `DiagnosticsCounter.Add(value)`

**Опциональные интерфейсы (помимо `ICycleCounters`):**
- `ISkippedCounters` — `{ int Skipped }` для idempotency-хитов
- `IRetriedCounters` — `{ int Retried }` для batch replay

Worker НЕ вызывает `diagnostics.X.Add(1)` напрямую для processed/failed.
Per-entity domain-метрики (e.g. per-tenant duration) — всё ещё идут
через typed counters на `IXxxDiagnostics`.

### 3. ctor — `IServiceProvider`, base создаёт scope per cycle

```csharp
public sealed class MyWorker(
    IServiceProvider services,      // ← root only; НЕ IServiceScopeFactory
    TimeProvider clock,
    IDiagnosticSource? diagnostics,  // ← может быть null (no telemetry)
    ILogger<MyWorker> logger)
    : ScheduledWorkerBase(services, clock, diagnostics, logger)
```

В `ExecuteCycleAsync` services доступны через `context.Services` — это свежий
DI scope, создаваемый base'ом на старте каждого cycle и disposed на выходе.

- ✅ Per-cycle scope — DbContext не leakится между циклами
- ❌ НЕ `IServiceScopeFactory scopeFactory` — base берёт это на себя
- ❌ НЕ раздавать `IServiceProvider` в тело worker'а — только base им владеет

### 4. Error policy

| Failure mode | Что делает base |
|--------------|-----------------|
| Cycle throws | Log error, increment `outcome=failed`, continue to next tick |
| Stopping token cancelled | Break loop gracefully, run `finally` (logs "stopped"), exit |
| `AdaptivePollSchedule` extra | `RecordCycle(failed: true)` records error backoff |

Base **НЕ** останавливает host при cycle failure. Per-entity try/catch
**внутри** `ExecuteCycleAsync` body обязателен — не валить весь цикл из-за
одной сломанной записи.

### 5. Per-cycle success emission: `Schedule.Describe()`

`Describe()` используется в startup-логе:
- `IntervalSchedule(5m)` → `every 00:05:00`
- `CronSchedule("0 2 * * *", UTC)` → `cron '0 2 * * *' (UTC)`
- `StartupSchedule` → `once on startup`

### 6. Test infrastructure

- Tests вызывают protected `ExecuteAsync` через reflection.
- Использовать custom `WorkerSchedule` subclasses, которые ждут cancellation
  после N циклов (например `OneTickThenWaitSchedule`) — иначе loop runs forever.
- Unit-тесты базы (`tests/unit/<Solution>.Shared.Kernel.Unit/BackgroundServices/ScheduledWorkerBaseShould.cs`)
  покрывают: per-cycle scope, reflection emission (success + failure),
  error continuation, cancellation, `Schedule.Describe()`.

## Регистрация в DI + run-mode активация

Если хост умеет запускаться в нескольких режимах (полный / только API / только
обработка событий), гейтящиеся workers регистрируются через обёртку
`Add<Solution>Worker<T>(configuration)` над `AddHostedService<T>`, а НЕ напрямую
`AddHostedService<T>()`. Обёртка читает атрибут `[WorkerActivation(WorkerGroup.X)]`
на классе воркера и активный run-mode (из конфигурации) и регистрирует воркера
только когда его группа активна в текущем режиме:

```csharp
[WorkerActivation(WorkerGroup.PeriodicDomain)]
public sealed class DailyReconciliationWorker(...) : ScheduledWorkerBase(...) { }

// installer:
services.AddAppWorker<DailyReconciliationWorker>(configuration);   // PeriodicDomain
services.AddAppWorker<OutboxRelayWorker>(configuration);           // EventPipeline
```

Без атрибута группа = `WorkerGroup.Bootstrap` (активна всегда). Матрица
`mode → {groups}` живёт в **одном** месте (в самой обёртке):

| `WorkerGroup` | Активна в режимах | Для чего |
|---|---|---|
| `Bootstrap` (default) | Full, EventsOnly, ApiOnly | warmup / reference-загрузчики, нужные API |
| `EventPipeline` | Full, EventsOnly | outbox relay / change-feed (обработка событий) |
| `PeriodicDomain` | Full | периодические доменные (cron-джобы, closed-loop) |

`ScheduledWorkerBase` дополнительно сверяется с `IsActive` в начале
`ExecuteAsync` (run-гейт): зарегистрированный, но неактивный воркер не тикает.

Bootstrap-воркеры (всегда активные) можно регистрировать любым способом — для
`Bootstrap` обёртка всегда регистрирует. Гейтящиеся (`EventPipeline` /
`PeriodicDomain`) — **только** через обёртку. Конкретный набор групп и режимов
объявляется в ADR проекта.

Cron expression is bound из `IOptions`:

```csharp
public sealed class CronOptions
{
    public const string SectionName = "Cron";
    public string DailyReconciliation { get; init; } = "0 2 * * *";   // 02:00 UTC daily
    public string SyncOutbound { get; init; } = "*/5 * * * *";        // every 5 min
    public string Overlimit { get; init; } = "0 1 * * *";             // 01:00 UTC daily
}
```

Cron-парсер вызывается в ctor `CronSchedule` и fails-fast — misconfig
invalidates DI at boot, не на первом tick.

## Антипаттерны

| Anti-pattern | Why forbidden | Fix |
|--------------|---------------|-----|
| `class XxxWorker : BackgroundService` (наследует напрямую) | Duplicates loop/scope/error boilerplate; bypasses reflection emission; inconsistent error policy | `: ScheduledWorkerBase` |
| `new PeriodicTimer(...)` / `Task.Delay(...)` in worker loop | Bypasses `Schedule`; not testable | `protected override WorkerSchedule Schedule => ...` |
| `diagnostics.X.WithTag("outcome", "success").Add(processed)` в worker body | Inconsistent metric naming; bypasses reflection; duplicates counter-emit logic | `[Counter("processed")]` on a record property |
| `var processed = 0; var failed = 0;` returned via out-param or ref | Mixed logic; not typed | Return `record : ICycleCounters` with `[Counter]` props |
| `_ = await Task.Run(...)` / `Task.Run(async () => ...)` | Bypasses loop scoping | Just `await` it inline |
| `BackgroundService.ExecuteAsync` returning early without checking token | Crashes entire app | `while (!stoppingToken.IsCancellationRequested)` (built into base) |
| `IServiceScopeFactory scopeFactory` in worker ctor | Worker now depends on infra detail that's base's job | `IServiceProvider services` (root) only |
| Creating a new schedule instance in the `Schedule` getter | Record types are values and new on every access — `AdaptivePollSchedule.nextDelay` сбрасывается каждую итерацию | Cache `Schedule` in a `private readonly` field; **never** `Schedule => new XxxSchedule(...)` |
| Swallowing exceptions silently (`catch { }`) | Telemetry never sees them | Use base loop's exception handler; per-entity try/catch in body |
| `AddHostedService<T>()` для gated воркера | Обходит run-mode гейт — воркер стартует всегда | обёртка `Add<Solution>Worker<T>(configuration)` + `[WorkerActivation(...)]` |

## PR review checklist

При приёмке PR с новым / изменённым worker'ом:

- [ ] Worker наследует `ScheduledWorkerBase` (НЕ `BackgroundService` напрямую)
- [ ] `WorkerSchedule` — правильный тип для сценария (Interval/Cron/Adaptive/Startup)
- [ ] `Schedule` cached в `private readonly` field (НЕ `Schedule => new ...()` getter)
- [ ] ctor: `(IServiceProvider services, TimeProvider clock, IDiagnosticSource? diagnostics, ILogger<XxxWorker> logger)`
- [ ] `ExecuteCycleAsync` resolves services через `context.Services` (per-cycle scope)
- [ ] Return type — `ICycleCounters` record (или null) с `[Counter]` properties
- [ ] Per-entity try/catch внутри body — не валить весь цикл
- [ ] Cron expressions из `IOptions<XxxCronOptions>`, не hardcoded
- [ ] Есть unit-тест воркера рядом с тестами его модуля
- [ ] DI registration — через обёртку (обязательна для gated воркеров); группа
      объявлена `[WorkerActivation(WorkerGroup.X)]` (нет атрибута → `Bootstrap`,
      всегда вкл)

Без этого — PR **отклоняется**.

## Migration от raw `BackgroundService`

1. Поменять ctor на `(IServiceProvider services, TimeProvider clock, IDiagnosticSource? diagnostics, ILogger<XxxWorker> logger)`
2. Переместить `while (!ct.IsCancellationRequested)` логику в base (удалить из `ExecuteAsync`)
3. Заменить `using var scope = scopeFactory.CreateAsyncScope()` на `context.GetService<T>()` внутри body
4. Удалить try/catch boilerplate (base обрабатывает)
5. Удалить ручной `Operation(...).Build()` (base обёртка)
6. Заменить `diagnostics.XxxCount.Add(...)` на `[Counter] int Processed` в return record
7. `Schedule => new XxxSchedule(...)` → `private readonly XxxSchedule schedule = new(...); protected override WorkerSchedule Schedule => schedule;`

После миграции каждый worker меньше на ~60% строк. Commit per worker —
rolling миграция.

## Связанные правила

- `observability/diagnostics.md` — when + how to add OTel spans + counters
- `di-lifetimes.md` — scoped deps / `IServiceScopeFactory`
- `async-and-tasks.md` — async/await + `CancellationToken` discipline
- `anti-patterns.md` — record DTO placement, validation, JsonSerializerOptions
- `process/build-verification.md` — build gate перед commit
- `process/worker-audit.md` — self-audit checklist
