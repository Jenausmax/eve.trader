---
description: di installer pattern + composition root — registration через installer extension methods, явная цепочка в Program.cs
globs: ["**/*.cs", "**/Program.cs"]
always: true
---

# DI — Installer pattern & composition root

Этот файл — правила регистрации DI и composition root. Options/IOptions —
в `di-options.md`. Service lifetimes / captive dependency — в `di-lifetimes.md`.

## 1. Stack & принципы

| Назначение | Что используем |
|------------|----------------|
| DI container | `Microsoft.Extensions.DependencyInjection` (нативный MSDI) |
| Configuration | `Microsoft.Extensions.Configuration` + `IOptions<T>` |
| Hosting | `Microsoft.Extensions.Hosting` + `WebApplicationBuilder` |
| Options validation | `DataAnnotations` + `.ValidateOnStart()` |

**Принципы:**

1. Только native Microsoft. Никаких Autofac / Lamar / Castle.Windsor.
2. Никакой рефлексии для авторегистрации.
3. Composition root явный — в `Program.cs` видна полная цепочка.
4. Конфигурация через `IOptions<T>`. Никакого `IConfiguration` в сервисах.
5. Валидация при старте — `.ValidateOnStart()`.

---

## 2. Installer pattern — REQUIRED

**Installer** = `static class` с extension-методами, инкапсулирующий
регистрацию одного модуля.

```csharp
namespace Acme.Shop.Order.Installers;

public static class OrderFeatureInstaller
{
    public static IServiceCollection AddOrderFeatureCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OrderOptions>()
            .Bind(configuration.GetSection(OrderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    public static WebApplication UseOrderFeatureCore(this WebApplication application)
    {
        return application;
    }
}
```

### Структура

- **Папка:** `Installers/` в корне проекта.
- **Имя класса:** `<Module>Installer`.
- **Имя метода Add:** `Add<Module>Core` (суффикс `Core` обязателен).
- **Имя метода Use:** `Use<Module>Core` (если нужен).

**`Core` отличает основную регистрацию** от опциональных дополнений:

```csharp
services
    .AddOrderFeatureCore(configuration)            // обязательно
    .AddOrderFeatureMetrics(configuration)         // опционально
    .AddOrderFeatureBackgroundJobs(configuration); // опционально
```

### Когда какой метод нужен

| Что регистрируется | `Add<Module>Core` | `Use<Module>Core` |
|--------------------|-------------------|-------------------|
| Только сервисы и options | ✅ | ❌ |
| Сервисы + middleware/endpoints | ✅ | ✅ |
| Только middleware | ❌ | ✅ |
| Hosted services | ✅ | ❌ |

Не пиши пустой `Use<Module>Core` — это шум.

### Один модуль — один Installer

Каждый **проект** в `feature/`, `database/`, `client/`, `shared/` экспонирует
**один** Installer. Если внутри проекта много регистраций — дели на
**private extension methods внутри одного Installer**.

### Использование в Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSharedInfrastructureCore(builder.Configuration)
    .AddOrderFeatureCore(builder.Configuration)
    .AddOrdersDatabaseCore(builder.Configuration);

var application = builder.Build();

application.UseSharedInfrastructureCore();
application.UseOrderFeatureCore();

await application.RunAsync();
```

**Порядок в Program.cs виден глазами.** Нет авторегистрации.

---

## 3. IOptions — в сервисах, IConfiguration — в Installer

### В сервисах — только `IOptions<T>`

```csharp
// ❌ Wrong — сервис знает про IConfiguration
public sealed class OrderService(IConfiguration configuration)
{
    public Task DoAsync()
    {
        var maxOrders = configuration["Features:Order:MaxOrdersPerUserPerHour"];
        // ...
    }
}

// ✅ Correct — сервис принимает типизированные Options
public sealed class OrderService(IOptions<OrderOptions> orderOptions)
{
    public Task DoAsync()
    {
        var maxOrders = orderOptions.Value.MaxOrdersPerUserPerHour;
    }
}
```

`IConfiguration` остаётся **только** в `Program.cs` и в Installer-ах.

---

## 4. Условная регистрация на основе конфига

Когда **что регистрировать** зависит от значения в конфиге (multi-provider,
feature flags на уровне DI, регистрация по списку). В Installer это
**разрешено** через прямое чтение `IConfiguration`.

```csharp
public static IServiceCollection AddMessagingCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var messagingOptions = configuration
        .GetSection(MessagingOptions.SectionName)
        .Get<MessagingOptions>()
        ?? throw new InvalidOperationException(
            $"Section '{MessagingOptions.SectionName}' is missing");

    services.AddOptions<MessagingOptions>()
        .Bind(configuration.GetSection(MessagingOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    switch (messagingOptions.Provider)
    {
        case MessagingProvider.Kafka:
            services.AddSingleton<IMessageBus, KafkaMessageBus>();
            services.AddHostedService<KafkaConsumerHost>();
            break;
        // ...
    }

    return services;
}
```

**Запрещено:**

```csharp
// ❌ Wrong — собираем ServiceProvider ради чтения Options
using var tempProvider = services.BuildServiceProvider();
var messagingOptions = tempProvider
    .GetRequiredService<IOptions<MessagingOptions>>().Value;
```

---

## 5. Composition root — два варианта

### A. Per-module Installer + явная цепочка в Program.cs

Когда у приложения свой набор зависимостей. Видно глазами в `Program.cs`.

### B. Application-level Composition проект

Отдельный `*.Composition` собирает полный набор через один Installer:

```csharp
// shared/Acme.Shop.Composition/Installers/SharedInfrastructureInstaller.cs
public static class SharedInfrastructureInstaller
{
    public static IServiceCollection AddSharedInfrastructureCore(
        this IServiceCollection services,
        IConfiguration configuration)
        => services
            .AddLoggingCore(configuration)
            .AddTelemetryCore(configuration)
            .AddHealthChecksCore(configuration);

    public static WebApplication UseSharedInfrastructureCore(this WebApplication application)
        => application
            .UseLoggingCore()
            .UseTelemetryCore()
            .UseHealthChecksCore();
}
```

### Default: гибрид A + B

- `shared/<Company>.<App>.Composition` — только cross-cutting.
- Каждый `feature/`, `database/`, `client/` — свой Installer.
- `Program.cs` каждого entry-point — явная цепочка.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSharedInfrastructureCore(builder.Configuration)   // ← cross-cutting
    .AddOrderFeatureCore(builder.Configuration)           // ← feature
    .AddOrdersDatabaseCore(builder.Configuration)         // ← database
    .AddPublicApiClientCore(builder.Configuration);       // ← client

var application = builder.Build();

application.UseSharedInfrastructureCore();

await application.RunAsync();
```

---

## 6. Anti-patterns

```csharp
// ❌ Service locator
public sealed class OrderService(IServiceProvider serviceProvider)
{
    public async Task DoAsync()
    {
        var repository = serviceProvider.GetRequiredService<IOrderRepository>();
    }
}

// ❌ Сборка ServiceProvider до Build
var tempProvider = builder.Services.BuildServiceProvider();
var options = tempProvider.GetRequiredService<IOptions<MyOptions>>().Value;

// ❌ Авторегистрация через рефлексию
foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
{
    if (type.GetInterfaces().Any(i => i.Name.StartsWith("I")))
    {
        services.AddScoped(type.GetInterfaces().First(), type);
    }
}

// ❌ Магические строки путей к секциям
services.AddOptions<KafkaOptions>()
    .Bind(configuration.GetSection("Brokers:Kafka"));
// Дублируется по проекту. Используй KafkaOptions.SectionName.

// ❌ Один Options-класс на всё приложение
public sealed class ApplicationOptions
{
    public DatabaseOptions Database { get; init; } = null!;
    public KafkaOptions Kafka { get; init; } = null!;
    // 10 вложенных классов
}

// ❌ IOptions<T>.Value в конструкторе сохранён в поле
public sealed class OrderService(IOptions<OrderOptions> options)
{
    private readonly OrderOptions orderOptions = options.Value;
    // Потеряли обновления для Monitor. .Value на использовании.

// ❌ Пустой Use<Module>Core
public static WebApplication UseOrderFeatureCore(this WebApplication application)
{
    // do nothing
    return app;
}
```

---

## Связанные правила

- `di-options.md` — IOptions pattern, validation, monitor
- `di-lifetimes.md` — service lifetimes, captive dependency
- `project-naming-and-setup.md` — где живёт Installer