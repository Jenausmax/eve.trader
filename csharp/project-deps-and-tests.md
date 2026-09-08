---
description: layer dependencies (только вниз), testing structure (unit/ + integration/), anti-patterns организации проекта
globs: ["**/*.csproj", "**/*.slnx"]
always: true
---

# Layer dependencies & testing structure

Этот файл — правила ссылок между слоями, структура тестов, anti-patterns
организации. Layers overview — в правиле слоёв конкретного проекта
(project-local). Naming/setup — в `project-naming-and-setup.md`.

> Названия слоёв ниже — пример layered-раскладки (см. `architecture.md`).
> Проект с другим объявленным стилем подставляет свои; **правило** одно:
> ссылки только вниз, соседи одного слоя друг на друга не ссылаются, общее
> уезжает в нижний слой.

## 1. Layer dependencies — правило: ссылки только вниз

```
application/  ─┐
bots/         ─┤
               ├──→  feature/   ─→  models/   ─→  shared/
generation/   ─┘                ─→  database/ ─┘

feature/patterns/    ─→  feature/  (можно)
feature/specified/   ─→  feature/  (можно)
feature/             ─→  models/, shared/  (можно)

feature/patterns/    ─×  application/  (нельзя)
shared/              ─×  feature/      (нельзя)
models/              ─×  database/     (нельзя)
```

### Конкретные разрешённые ссылки

| Слой | Может ссылаться на |
|------|-------------------|
| `application/` | `feature/`, `database/`, `client/`, `models/`, `shared/`, `bots/` |
| `bots/` | `feature/`, `client/`, `models/`, `shared/` |
| `feature/patterns/`, `feature/specified/` | `feature/`, `models/`, `shared/` |
| `feature/<other>` | `models/`, `shared/` (**НЕ** другие feature) |
| `database/<...>.Database.<Name>` | `Database.Core`, `models/`, `shared/` |
| `database/Database.Core` | `models/`, `shared/` |
| `client/` | `models/`, `shared/` |
| `models/` | `shared/` (минимально, лучше — ничего) |
| `shared/`, `generation/` | ничего (только nuget) |
| `tests/` | любое из `src/` |

### Между проектами одного слоя

- `feature/<other>` **НЕ** ссылаются друг на друга. Общее → `shared/`.
- `feature/patterns/*` → `Pattern.Core` (базовые абстракции); между
  конкретными паттернами — нет.
- `database/<...>.Database.<Name>` **НЕ** ссылаются друг на друга.
  Cross-db связи — на application-уровне.

Проверяется автоматически в `tests/architecture/` через NetArchTest
(см. `class-layout-and-tooling.md` §3).

---

## 2. Anti-patterns

### Технический долг в имени папки

```
❌ database/.../Repositories/        # "(бывшие, вынесены)"
❌ feature/.../Services_Old/
❌ shared/.../Deprecated/
```

Либо удалить сразу, либо issue с дедлайном. Не хранить "на всякий случай".

### Циклы зависимостей через DI

```csharp
// ❌ feature/A регистрирует реализацию из feature/B → cycle
services.AddSingleton<ISomething, SomethingFromFeatureB>();
```

Решение: общая абстракция в `shared/` или `models/`, реализации
регистрируются на application-уровне.

### Бизнес-логика в `application/`

Application — **только** composition + bootstrap. Расчёты и доменные решения
— в `feature/patterns/`.

### Persistence в feature

`feature/` работает через **интерфейсы** репозиториев / query services,
реализации — в `database/`. Позволяет тестировать паттерны без БД.

### Утечка EF-атрибутов в DTO

API контракты (`Acme.Shop.Api.Contracts`) не знают про EF Core. Никаких
`[Table]`, `[Column]`, `[ForeignKey]`.

### Папки с именами-помойками

`Helpers/`, `Utils/`, `Common/`, `Misc/`, `Tools/`, `Stuff/`.

### Один большой проект вместо нескольких

```
❌ Acme.Shop.Providers.Client/
   ├── ProviderA/      ← если > 30 файлов или специфичные nuget
   ├── ProviderB/
   ├── ProviderC/
   └── ...
```

Критерий выноса: > 30 файлов с собственной структурой; специфичные nuget;
независимый цикл релиза; можно отключить/заменить без влияния.

---

## 3. Testing structure — `tests/unit/` + `tests/integration/`

> **Unit-first по умолчанию.** Unit-тесты — основная масса; integration живут
> отдельно и могут принадлежать другой команде/треку.
>
> **`tests/unit/`** — domain aggregates, value objects, CQRS handlers (mocked
> deps), validators, pure functions, query builders, contributors. Ориентир
> покрытия — 70% строк.
>
> **`tests/integration/`** — repositories, HTTP endpoints, hosted workers,
> cross-service flows. Стек Testcontainers / WebApplicationFactory / Respawn —
> в `testing-integration.md`.

Физическое разделение по категориям (не плоско). Категория дублируется и
в имени проекта, и в под-папке.

```
tests/
├── unit/                                      # active scope (this team)
│   ├── Acme.Shop.Pattern.Batching.Unit.Chunking/
│   ├── Acme.Shop.Pattern.Batching.Unit.Retry/
│   └── Acme.Shop.Architecture.Tests/          # один на весь solution (reflection, no IO)
└── integration/                               # может принадлежать отдельной команде
    ├── Acme.Shop.Pattern.Batching.Integration.Execution/
    ├── Acme.Shop.Api.Public.Integration.Health/
    └── Acme.Shop.Testing/                     # shared infra (PostgresFixture, WebAppFactory)
```

`tests/` — множественное число. НЕ `test/`.
`Architecture.Tests` живёт в `unit/` — это reflection/convention тесты без I/O.
`*.Testing` (shared integration infra) живёт в `integration/` — хелпер-библиотека
**для** integration-тестов.

### Нейминг

```
<SourceProject>.<TestKind>[.<Feature>]
```

- `SourceProject` — обязательно.
- `TestKind` — `Unit` | `Integration` | `Benchmarks`, обязательно.
- `Feature` — опционально, если у src-проекта **ровно один** тест-проект
  данного типа. Если появляется второй — оба обязаны иметь Feature.

Разделять тест-проекты когда: > 30 файлов и логически делится; разные
dependencies (Testcontainers vs нет); разные команды.

### TestKind

| TestKind | Когда |
|----------|-------|
| `Unit` | Моки, in-memory, < 100ms каждый |
| `Integration` | Реальные зависимости: БД через Testcontainers, HTTP через `WebApplicationFactory` |
| `Benchmarks` | BenchmarkDotNet |

`Architecture.Tests` — один на весь solution. Правила слоёв, нейминга,
размещения интерфейсов и моделей.

❌ Не должно быть: тестов внутри src, папки `test/` (ед. число), тест-проектов
лежащих на прямо в `tests/`, одного тест-проекта на несколько src
(исключение — `Architecture.Tests`).

---

## Связанные правила

- правило слоёв проекта (project-local) — обзор слоёв
- `architecture.md` — объявленный стиль архитектуры + 6 законов
- `project-naming-and-setup.md` — naming, decision tree
- `testing-stack-and-pyramid.md` — test stack
- `testing-unit.md` — unit-тесты подробно
- `testing-integration.md` — integration-тесты подробно