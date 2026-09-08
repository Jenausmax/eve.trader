---
description: ioptions pattern — configuration validation, validate-on-start, ioptions vs snapshot vs monitor
globs: ["**/*.cs"]
always: true
---

# IOptions pattern

Этот файл — правила Options-классов, валидации, мониторинга. Installer
pattern — в `di-installer.md`. Service lifetimes — в `di-lifetimes.md`.

## 1. Каждый Options — отдельный класс

Один Options-класс на одну логическую секцию конфигурации. Не складывай
всё в один `AppOptions` с десятком вложенных классов.

```csharp
// ✅ Один Options — одна секция
public sealed class OrderOptions
{
    public const string SectionName = "Features:Order";

    [Range(1, int.MaxValue)]
    public int MaxOrdersPerUserPerHour { get; init; } = 100;

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal MinimumOrderAmount { get; init; } = 0.01m;
}
```

---

## 2. `SectionName` как const внутри класса

Путь к секции — **константа `SectionName`** прямо в Options-классе.

```csharp
public sealed class KafkaOptions
{
    public const string SectionName = "Brokers:Kafka";
    // ...
}

// Installer использует:
services.AddOptions<KafkaOptions>()
    .Bind(configuration.GetSection(KafkaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Запрещено:** магическая строка в Installer.

---

## 3. Свойства — `init` и `required`

Options — иммутабельные. После старта значения не меняются (если только
не используется `IOptionsMonitor`).

```csharp
public sealed class KafkaOptions
{
    public const string SectionName = "Brokers:Kafka";

    [Required]
    public required string BootstrapServers { get; init; }

    [Required]
    public required string ConsumerGroupPrefix { get; init; }

    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 100;
}
```

`required` — для значений без разумного default. EF Core, JSON binding,
`IConfiguration` это понимают.

---

## 4. `sealed class`, не record

Options-классы — `sealed class`. Record не нужен — value equality для
Options не имеет смысла.

---

## 5. `.ValidateOnStart()` — REQUIRED

Каждый `AddOptions<T>()` обязан заканчиваться `.ValidateOnStart()`:

```csharp
services.AddOptions<OrderOptions>()
    .Bind(configuration.GetSection(OrderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();           // ← обязательно
```

Без `.ValidateOnStart()` валидация выполняется лениво — при первом
обращении к `IOptions<T>.Value`. Битый `appsettings.json` в проде упадёт
не при старте, а через час в первом HTTP-запросе.

---

## 6. Что валидируем

1. `[Required]` для всех обязательных полей.
2. `[Range]` для числовых ограничений.
3. `[StringLength]` / `[MaxLength]` для строк с ограничением.
4. `[Url]`, `[EmailAddress]` где применимо.
5. Custom validators через `.Validate(predicate, "message")` для cross-field.

```csharp
public sealed class KafkaOptions
{
    public const string SectionName = "Brokers:Kafka";

    [Required]
    [MinLength(1)]
    public required string BootstrapServers { get; init; }

    [Required]
    [RegularExpression(@"^[a-z0-9-]+$",
        ErrorMessage = "Consumer group prefix must contain only lowercase letters, digits and dashes")]
    public required string ConsumerGroupPrefix { get; init; }

    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 100;

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

---

## 7. Cross-field validation

Когда правило не сводится к атрибуту на одном поле:

```csharp
services.AddOptions<RetryOptions>()
    .Bind(configuration.GetSection(RetryOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.MaxAttempts > 0,
        "MaxAttempts must be positive")
    .Validate(options => options.InitialDelay <= options.MaxDelay,
        "InitialDelay must not exceed MaxDelay")
    .ValidateOnStart();
```

Для крупных классов — выноси в `IValidateOptions<T>`:

```csharp
public sealed class KafkaOptionsValidator : IValidateOptions<KafkaOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            failures.Add("BootstrapServers is required");
        }

        if (options.BatchSize > 1_000 && options.PollTimeout < TimeSpan.FromSeconds(10))
        {
            failures.Add("Large BatchSize requires PollTimeout >= 10s");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

services.AddSingleton<IValidateOptions<KafkaOptions>, KafkaOptionsValidator>();
```

---

## 8. IOptions vs Snapshot vs Monitor

### Default — `IOptions<T>`

В контейнеризованных приложениях с env-variables / mounted ConfigMaps —
всегда `IOptions<T>`. Singleton, фиксируется при старте.

```csharp
public sealed class OrderService(IOptions<OrderOptions> orderOptions)
{
    public Task DoAsync()
    {
        var maxOrders = orderOptions.Value.MaxOrdersPerUserPerHour;
    }
}
```

### `IOptionsMonitor<T>` — только при remote config provider

Используй **только когда** в стэке есть:
- Consul KV.
- Azure App Configuration.
- AWS AppConfig / HashiCorp Vault с динамической перезагрузкой.
- `reloadOnChange: true` в `appsettings.json`, который **реально** меняется в runtime.

```csharp
public sealed class FeatureFlagService(IOptionsMonitor<FeatureFlagsOptions> featureFlags)
{
    public bool IsEnabled(string feature)
        => featureFlags.CurrentValue.Flags.GetValueOrDefault(feature);
}
```

### `IOptionsSnapshot<T>` — почти не используем

Scoped, читает значение один раз на scope (обычно один HTTP-запрос).
В реальности нужно очень редко.

### Запрет смешивания

В рамках **одного приложения** для одного и того же Options-класса
используется **один** интерфейс. Если внутри приложения часть сервисов
должна реагировать, а часть нет — для всех используется `IOptionsMonitor<T>`.

---

## Связанные правила

- `di-installer.md` — Installer pattern, composition root
- `di-lifetimes.md` — service lifetimes, captive dependency