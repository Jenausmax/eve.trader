---
description: structured logging с microsoft.extensions.logging — только structured, никакой интерполяции, PascalCase placeholders
globs: ["**/*.cs"]
always: true
---

# Logging (Microsoft.Extensions.Logging)

Этот файл — правила structured logging. API design / controllers — в
`api-design.md`. OTel — в `observability/diagnostics.md`.

## 1. Только structured logging — ЗАПРЕТ интерполяции

```csharp
// ✅ Correct
logger.LogInformation(
    "Starting execution {ExecutionId} for task {TaskName}",
    execution.Id, execution.TaskName);

logger.LogWarning(
    "Execution {ExecutionId} exceeded timeout of {TimeoutMs}ms",
    execution.Id, timeout.TotalMilliseconds);

logger.LogError(
    exception,
    "Execution {ExecutionId} failed",
    execution.Id);

// ❌ Wrong — string interpolation ломает structured logging
logger.LogInformation($"Starting execution {execution.Id}");

// ❌ Wrong — concat
logger.LogInformation("Starting execution " + execution.Id);
```

**Запрещено `.editorconfig`-ом через CA2254 (severity=error).**

---

## 2. Log levels

| Level | Когда |
|-------|-------|
| `Trace` | Очень детальная трассировка, обычно выключена в проде |
| `Debug` | Диагностика во время разработки |
| `Information` | Штатные события: запуск, выполнение задачи, регистрация |
| `Warning` | Неожиданное, но обработанное (retry, fallback, rate limit) |
| `Error` | Сбой операции, приложение продолжает работу |
| `Critical` | Сбой, угрожающий работе приложения (потеря БД, OOM) |

```csharp
logger.LogDebug("Query took {ElapsedMs}ms", elapsedMs);
logger.LogInformation("Task {TaskKey} executed successfully", taskKey);
logger.LogWarning("Retry attempt {AttemptNumber} for task {TaskKey}", attempt, taskKey);
logger.LogError(exception, "Failed to execute task {TaskKey}", taskKey);
logger.LogCritical("Database connection lost");
```

---

## 3. Placeholders — PascalCase

```csharp
// ✅ PascalCase placeholders — стандарт Serilog/MEL
logger.LogInformation("User {UserId} signed in from {IpAddress}", userId, ipAddress);

// ❌ camelCase / snake_case
logger.LogInformation("User {userId} signed in", userId);
```

**Enforcement:** CA1727 (severity=error).

---

## Связанные правила

- `api-design.md` — controllers
- `observability/diagnostics.md` — OTel spans + counters (kernel Diagnostics)
- `analyzers.md` — analyzer packages