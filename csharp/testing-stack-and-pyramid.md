---
description: test stack — xunit v3 + shouldly + nsubstitute + testcontainers + respawn. когда unit, когда integration
globs: ["tests/**/*.cs", "tests/**/*.csproj"]
always: true
---

# Test stack & pyramid

Этот файл — стэк и решение когда unit vs integration. Unit-тесты подробно —
в `testing-unit.md`. Integration-тесты подробно — в `testing-integration.md`.

---

## 1. Stack — ФИКСИРОВАННЫЙ выбор, версии у потребителя

Ниже — **какие библиотеки** и **почему именно эти**. Номеров версий здесь нет
намеренно: версия — локальный факт, её источник истины у потребителя
(`Directory.Packages.props` либо csproj, если CPM выключен). Правило,
называющее версию, устаревает в тот день, когда её поднимут, и начинает
противоречить репозиторию, ничего об этом не сообщая.

| Назначение | Библиотека |
|------------|------------|
| Test framework | **xUnit v3** |
| Assertions | **Shouldly** |
| Mocking | **NSubstitute** |
| Container infrastructure | **Testcontainers** |
| Web API testing | **Microsoft.AspNetCore.Mvc.Testing** |
| DB cleanup between tests | **Respawn** |
| Fake data generation | **Bogus** |
| Coverage | **Coverlet.collector** |
| Architecture tests | **NetArchTest.Rules** |

**xUnit v3 — цель, не обязательно текущее состояние.** Проект может ещё
сидеть на v2 с `Microsoft.NET.Test.Sdk` и VSTest: миграция стоит времени и
откладывается осознанно. Правило задаёт направление, а не отменяет факт;
если потребитель на v2, это записывается у него, а не правкой этого файла.

### Почему этот стэк

- **xUnit v3** — на `Microsoft.Testing.Platform`, не `VSTest`; быстрее discovery.
- **Shouldly** вместо FluentAssertions — FluentAssertions 8.0+ коммерческая (Xceed).
- **NSubstitute** вместо Moq — Moq в 2023 встроил SponsorLink (сбор email).
- **Testcontainers** — реальная Postgres/Redis в Docker.
- **Respawn** — быстрая очистка БД через TRUNCATE.

### Исключение из CODING-RULES для тестов

**`IAsyncLifetime.InitializeAsync()` / `DisposeAsync()` не принимают
`CancellationToken`.** Это override интерфейса xUnit, сигнатура зафиксирована
библиотекой. Правило "CancellationToken последним" применяется к нашим
методам, не к override чужих интерфейсов.

```csharp
// ✅ Корректно — это override IAsyncLifetime
public async Task InitializeAsync()
{
    await Container.StartAsync();
}

// ✅ Наши собственные методы — с CancellationToken
public async Task ResetAsync(CancellationToken cancellationToken = default)
{
    await respawner.ResetAsync(connection, cancellationToken);
}
```

В остальном тестовый код подчиняется тем же правилам: `var`, file-scoped
namespaces, braces везде, осмысленные lambda-имена, structured logging,
`is null` вместо `== null`.

### Csproj шаблон тест-проекта

> Версий в шаблоне нет по той же причине, что и в таблице выше: их источник
> истины — файл пакетов потребителя, а не это правило.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>

  <!-- Только для integration projects: -->
  <ItemGroup Condition="'$(IsIntegrationTest)' == 'true'">
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Respawn" />
  </ItemGroup>

</Project>
```

Общие пакеты (xUnit, Shouldly, NSubstitute, coverlet) выносятся в
`Directory.Build.props` для всех проектов в `tests/` через
`Condition="'$(IsTestProject)' == 'true'"`.

---

## 2. Decision tree — когда unit, когда integration

```
Что тестируем?

├── Калькулятор / парсер / форматтер / валидатор?
│   └── UNIT (чистая функция)
│
├── Стратегия / алгоритм / pattern matching с многими ветками?
│   └── UNIT с [Theory] + InlineData (или MemberData)
│
├── Repository / EF query / SQL?
│   └── INTEGRATION с Testcontainers
│
├── HTTP endpoint?
│   └── INTEGRATION через WebApplicationFactory
│
├── Сервис, который ходит во внешний API?
│   ├── Hot path (логика обработки ответа)        → UNIT с моком клиента
│   └── Сам клиент к API                          → CONTRACT tests
│
├── Workflow из нескольких сервисов?
│   └── INTEGRATION end-to-end
│
└── HostedService / BackgroundService?
    └── INTEGRATION
```

### Что НЕ тестируем вообще

- DTO / Records без логики (только `init`-properties) — нечего тестировать.
- EF Core entities (если только в них нет custom-методов).
- Auto-mapped профили AutoMapper/Mapperly — если простой 1:1 mapping.
- Microsoft / NuGet библиотеки.
- Один-в-один обёртки над сторонним API без логики.

---

## 3. Coverage thresholds — 70% line для unit-проектов

В `Directory.Build.props` тест-проектов:

```xml
<PropertyGroup>
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura,opencover</CoverletOutputFormat>
  <Threshold>70</Threshold>
  <ThresholdType>line</ThresholdType>
  <ThresholdStat>total</ThresholdStat>
</PropertyGroup>
```

**70% line coverage** для `tests/unit/`. Не 80%+ — ведёт к бессмысленным
тестам. Integration — без threshold.

Что исключаем:

```xml
<PropertyGroup>
  <ExcludeByFile>
    **/Program.cs,
    **/*.Designer.cs,
    **/Migrations/**/*.cs,
    **/Generated/**/*.cs
  </ExcludeByFile>
  <Exclude>[*.Tests]*,[*.Benchmarks]*</Exclude>
</PropertyGroup>
```

---

## Связанные правила

- `testing-unit.md` — unit-тесты подробно
- `testing-integration.md` — integration-тесты подробно
- `project-deps-and-tests.md` — testing structure, naming