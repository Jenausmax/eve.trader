---
description: integration tests — real DB via env-connection or Testcontainers (.WithReuse), one shared instance per run via ICollectionFixture, Respawn between tests, API through WebApplicationFactory HttpClient, shared *.Testing infra project
globs: ["tests/**/*.cs", "tests/**/*.csproj"]
---

# Integration tests — Testcontainers + Respawn + WebApplicationFactory

Heavier than unit tests (real DB, real HTTP pipeline); keep them in
`tests/integration/`, separate from `tests/unit/`. Stack + the unit/integration
decision tree → `testing-stack-and-pyramid.md`.

> A project may assign integration tests to a separate team/track. When it does,
> this file is the reference that team works from; the rules below don't change.

## 1. Принципы

1. **Реальная БД** — не in-memory, не SQLite. Источник БД резолвится:
   env-строка подключения (CI-`services:` контейнер, либо dev, указавший на
   уже запущенный инстанс) **или** Testcontainers с `.WithReuse(true)`.
2. **Один общий инстанс БД на весь прогон**, шарится через
   `ICollectionFixture<T>` + один `[CollectionDefinition]`. Между тестами —
   Respawn чистит данные. **Контейнер-на-класс не делаем**.
3. **API тестируем через `HttpClient`** из `WebApplicationFactory`, не
   напрямую через сервисы.
4. **Никаких production-secrets** — все настройки через `appsettings.Test.json`
   или environment variables в фикстуре.

---

## 2. Базовая фикстура для БД

**Стандарт резолвинга:** env-строка подключения → если задана, используем её
как есть (CI отдаёт БД через `services:`); иначе Testcontainers с
`.WithReuse(true)`.

```csharp
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string EnvConnection = "TEST_DB_CONNECTION";

    private PostgreSqlContainer? container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var env = Environment.GetEnvironmentVariable(EnvConnection);
        if (string.IsNullOrWhiteSpace(env))
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithReuse(true)
                .Build();
            await container.StartAsync();
            ConnectionString = container.GetConnectionString();
        }
        else
        {
            ConnectionString = env;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}
```

> **Кто мигрирует.** Миграции накатывает фикстура (в порядке FK).

---

## 3. Базовая фикстура для API

```csharp
public sealed class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture postgresFixture = new();
    private Respawner? respawner;

    public async Task InitializeAsync()
    {
        await postgresFixture.InitializeAsync();

        _ = CreateClient();

        await using var connection = new NpgsqlConnection(postgresFixture.ConnectionString);
        await connection.OpenAsync();

        respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(postgresFixture.ConnectionString));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(postgresFixture.ConnectionString);
        await connection.OpenAsync();
        await respawner!.ResetAsync(connection);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await postgresFixture.DisposeAsync();
    }
}
```

### Тест API

```csharp
[Collection(nameof(AppCollectionDefinition))]
public sealed class CreateOrderEndpointShould(AppFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync() => await factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateOrderAndReturn201()
    {
        var client = factory.CreateClient();
        var request = new CreateOrderRequest { Reference = "ORD-100", Quantity = 1.5m };

        var response = await client.PostAsJsonAsync("/api/orders", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        created.ShouldNotBeNull();
        created.Reference.ShouldBe("ORD-100");
        created.Quantity.ShouldBe(1.5m);
    }
}

[CollectionDefinition(nameof(AppCollectionDefinition))]
public sealed class AppCollectionDefinition : ICollectionFixture<AppFactory>;
```

### `IClassFixture<T>` vs `ICollectionFixture<T>`

| Когда | Что использовать |
|-------|------------------|
| Фикстура для **одного** класса тестов | `IClassFixture<T>` |
| Фикстура шарится **между классами** | `ICollectionFixture<T>` + `[CollectionDefinition]` |

Для БД-инстанса всегда `ICollectionFixture` — один общий `[CollectionDefinition]`
на **весь** прогон. Имя класса-definition — с суффиксом `Definition`
(`PostgresCollectionDefinition`), иначе ловит CA1711 на "Collection".

---

## 4. Respawn vs пересоздание БД

**Respawn** — `TRUNCATE` всех таблиц с сохранением структуры. Быстро,
~100ms на 50 таблиц.

**`Database.EnsureDeletedAsync()` + `MigrateAsync()`** — пересоздание схемы.
~5s. Только при тестах самих миграций.

```csharp
// ✅ В каждом тесте
public async Task InitializeAsync() => await factory.ResetDatabaseAsync();
```

---

## 5. Параллелизация

xUnit параллелит **классы тестов**, но не методы внутри класса. Для
integration тестов **отключаем параллелизацию для коллекции**, шарящей
контейнер:

```csharp
[CollectionDefinition(nameof(AppCollection), DisableParallelization = true)]
public sealed class AppCollection : ICollectionFixture<AppFactory>;
```

Между разными integration-коллекциями параллелизация остаётся.

---

## 6. Shared test infrastructure

Общая инфраструктура выносится в отдельный проект `tests/<Company>.<App>.Testing/`.

```
tests/
├── <Company>.<App>.Testing/                      # инфраструктура (не тесты!)
│   ├── Fixtures/  (PostgresFixture, RedisContainerFixture)
│   ├── Factories/ (AppFactory, WorkerHostFactory)
│   ├── Bases/     (IntegrationTestBase, ApiIntegrationTestBase, HostedServiceTestBase)
│   ├── Extensions/ (HttpClientAuthExtensions, FactorySeedingExtensions, WaitExtensions)
│   ├── Builders/  (OrderBuilder, UserBuilder)
│   └── TestData/  (KnownIds, SeedDataFactory)
├── <Company>.<App>.Api.Public.Integration.Orders/
└── <Company>.<App>.Api.Public.Integration.Auth/
```

**`<Company>.<App>.Testing`** — библиотека, не тест-проект. В нём нет
`[Fact]`. CI собирает, но не запускает через `dotnet test`.

### `IIntegrationFactory` — общий контракт

```csharp
public interface IIntegrationFactory : IAsyncLifetime
{
    public IServiceProvider Services { get; }
    public Task ResetAsync();
}

public interface IApiFactory : IIntegrationFactory
{
    public HttpClient CreateClient();
}

public interface IHostedServiceFactory : IIntegrationFactory
{
    public Task StartHostedServicesAsync();
}
```

### Generic base classes

```csharp
public abstract class IntegrationTestBase<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : class, IIntegrationFactory
{
    protected TFactory Factory { get; } = factory;

    protected T GetService<T>() where T : notnull
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    protected async Task UsingScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    public virtual Task InitializeAsync() => Factory.ResetAsync();
    public virtual Task DisposeAsync() => Task.CompletedTask;
}

public abstract class ApiIntegrationTestBase<TFactory>(TFactory factory)
    : IntegrationTestBase<TFactory>(factory)
    where TFactory : class, IApiFactory
{
    protected HttpClient Client { get; } = factory.CreateClient();
}
```

### Extension methods для специфики

```csharp
public static class HttpClientAuthExtensions
{
    public static HttpClient AuthenticateAs(this HttpClient client, string userId, params string[] roles)
    {
        var token = TestTokenFactory.Create(userId, roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient WithoutAuth(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}

public static class FactorySeedingExtensions
{
    public static async Task<TEntity> SeedAsync<TEntity>(
        this IIntegrationFactory factory, TEntity entity) where TEntity : class
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Set<TEntity>().AddAsync(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }
}

public static class WaitExtensions
{
    public static async Task WaitForAsync(
        this IIntegrationFactory factory,
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(interval);
        }

        throw new TimeoutException(
            $"Condition '{description}' not met within {timeout.TotalSeconds:F1}s");
    }
}
```

### Когда наследоваться, когда писать extension

| Что | Куда |
|-----|------|
| Базовый setup (factory, client, GetService) | `*TestBase` через наследование |
| Сброс БД, lifecycle | `*TestBase.InitializeAsync` |
| Аутентификация | extension на `HttpClient` |
| Seeding entity в БД | extension на `IIntegrationFactory` |
| Ожидание async условия | extension на `IIntegrationFactory` |
| Парсинг response с типизацией | extension на `HttpResponseMessage` |
| Что-то нужно один раз в одном тесте | в самом тесте, не выноси |

### Multiple `Program` в одном solution

Если несколько entry-points — у каждого свой Factory. Проблема:
`WebApplicationFactory<Program>` ссылается на класс `Program`, которых в
solution несколько. Решается через alias в csproj:

```xml
<ProjectReference Include="..\..\src\.../Api.Public/Api.Public.csproj">
    <Aliases>PublicApi</Aliases>
</ProjectReference>
```

```csharp
extern alias PublicApi;

public sealed class AppFactory : WebApplicationFactory<PublicApi::Program> { ... }
```

Альтернатива (проще) — сделать `Program` partial в каждом entry point:

```csharp
// <Company>.<App>.Api.Public/Program.cs
public partial class Program;

// <Company>.<App>.Worker/Program.cs
public partial class Program;
```

---

## 7. Что НЕ должно быть в integration тесте

- Hard-coded URL продакшна.
- Реальные secrets / production connection strings.
- `Thread.Sleep` для ожидания async операций — polling через `WaitForAsync`.
- Зависимости между тестами — каждый тест работает с чистой БД.
- `[Trait("Category", "Integration")]` ручные — категория зашита в имени проекта.

---

## 8. CI integration

```yaml
test:integration:
  image: mcr.microsoft.com/dotnet/sdk:10.0
  services:
    - { name: postgres:16-alpine, alias: postgres }
    - { name: clickhouse/clickhouse-server, alias: clickhouse }
  variables:
    POSTGRES_DB: app
    POSTGRES_USER: app
    POSTGRES_PASSWORD: app
    TEST_DB_CONNECTION: "Host=postgres;Port=5432;Database=app;Username=app;Password=app"
    CLICKHOUSE_USER: app
    CLICKHOUSE_PASSWORD: app
    CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT: "1"
    TEST_CLICKHOUSE_CONNECTION: "Host=clickhouse;Protocol=http;Port=8123;Database=default;Username=app;Password=app"
  script:
    - dotnet test --filter "FullyQualifiedName~Integration"
```

Параллелизацию внутри коллекции, шарящей инстанс, выключаем
(`DisableParallelization = true`); между разными коллекциями она остаётся.

### Не-Postgres сторейджи (ClickHouse и т.п.)

Тот же стандарт env-или-Testcontainers(`.WithReuse`). Три оговорки на
**общем** CI-service-инстансе:

1. Самоочищающийся сидинг. xUnit вызывает `InitializeAsync` на каждый
   тест-метод. На свежем контейнере это безвредно, на общей БД
   неидемпотентный `INSERT` накопит строки. Сидинг начинается с
   `DROP DATABASE/TABLE IF EXISTS` перед пересозданием.
2. ClickHouse: один statement на команду. HTTP-интерфейс ClickHouse
   отклоняет multi-statement (`;`-склейку).
3. Креды CI-service. У официального образа ClickHouse `default`-юзер
   **требует пароль** → провижним явного юзера через job-переменные.

---

## 9. Локальный pre-commit

Запускаем только unit-тесты (быстро):

```bash
dotnet test --filter "FullyQualifiedName~Unit" --logger "console;verbosity=minimal"
```

Integration можно гонять и локально — Testcontainers с `.WithReuse(true)`
поднимет/переиспользует инстанс сам, ручной `docker compose up` не нужен.
В pre-commit держим только unit ради скорости; полный integration-прогон
— на CI.

---

## Связанные правила

- `testing-stack-and-pyramid.md` — stack, decision tree
- `testing-unit.md` — unit-тесты
- `project-deps-and-tests.md` — testing structure
