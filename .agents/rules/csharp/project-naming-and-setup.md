---
description: project naming convention, repository root, slnx, decision tree for new projects, creating a project, internal structure
globs: ["**/*.csproj", "**/*.slnx", "**/Directory.Build.props", "**/Directory.Build.targets", "**/Directory.Packages.props"]
always: true
---

# Project naming, decision tree, setup

Этот файл — naming, decision tree для нового проекта, создание, internal
structure. Layer overview — в правиле слоёв конкретного проекта (project-local).
Layer dependencies — в `project-deps-and-tests.md`.

> Раскладка слоёв ниже (`application/`, `feature/`, `database/`, `client/`,
> `models/`, `shared/`, `generation/`, `bots/`) — **пример** для проекта,
> объявившего layered-стиль (`architecture.md`). Проект с другим объявленным
> стилем подставляет свои слои; правила именования, глубины и запретов
> одинаковы для всех.

## 1. Repository root

Корень — **только** управляющие файлы и каталоги верхнего уровня. Никаких
исходников и lock-файлов вложенных стэков.

| Файл | Назначение | Регистр |
|------|-----------|---------|
| `Directory.Build.props` / `.targets` / `.Packages.props` | Общие MSBuild свойства | **PascalCase** |
| `.editorconfig`, `.gitattributes`, `.gitignore` | Код-стайл, Git | как есть |
| `<solution>.slnx` | Solution-файл | lowercase |
| `README.md`, `CLAUDE.md`, `AGENTS.md` | Документация | UPPERCASE |

**Critical:** MSBuild на Linux (CI) — case-sensitive. `Directory.Build.props`
**обязан** быть в PascalCase, иначе `dotnet build` на Linux не подхватит
общие свойства.

**Чего не должно быть:** lock-файлы npm/bun/yarn в корне; `node_modules/`,
`bin/`, `obj/` (в `.gitignore`); исходники; дубли `*.sln` + `*.slnx`.

---

## 2. Solution file — `.slnx` only

Один формат — `.slnx` (XML из .NET SDK 9 / Rider 2024.3+).

**Запрет:** держать одновременно `<name>.sln` и `<name>.slnx`. Миграция:
`dotnet sln <name>.sln migrate` → `git rm <name>.sln`.

---

## 3. Project naming — REQUIRED convention

### Базовый шаблон

```
<Company>.<App>.<Layer>[.<Specifier>][.<Extra>]
```

| Слой | Префикс | Пример |
|------|---------|--------|
| `shared/` | `<Company>.<App>.<Capability>` | `Acme.Shop.Composition` |
| `feature/` | `<Company>.<App>.<Capability>` | `Acme.Shop.Background` |
| `feature/patterns/` | `<Company>.<App>.Pattern.<Name>` | `Pattern.Batching` |
| `feature/specified/` | `<...>.Specified.<Name>` | `Specified.Bulk` |
| `database/` | `<...>.Database.<DbContext>` / `Database.Core` | `Database.Customers` |
| `client/` | `<Company>.<App>.Client.<ApiName>` | `Client.Public` |
| `models/` | `<Company>.<App>.Entity.<Scope>` / `Api.Contracts` | `Entity.Core` |
| `application/` | `<Company>.<App>.<EntryPoint>.<Kind>` | `Api.Public` |
| `bots/` | `<Company>.<App>.Bots.<Channel>` | `Bots.Telegram` |
| `generation/` | `<Company>.<App>.Code.<Tech>` | `Code.Roslyn` |
| `tests/` | `<SourceProject>.<Kind>[.<Feature>]` | `Pattern.Batching.Unit.Chunking` |

### Глубина имён

```
<Company>.<App>.<Domain>.<SubDomain>      → ок (4 сегмента)
<Company>.<App>.<Domain>.<Kind>           → ок
<Company>.<App>.<Layer>.<Name>.<Variant>  → ок
Глубже 4 — перебор. Поднимай на уровень вверх.
```

### Запрещённые имена

Имена-помойки: `Utils`, `Helpers`, `Common`, `Misc`, `Tools`, `Shared`,
`Core` (без квалификации), `Implement`, `Context`, `Manager`, `Service`.

---

## 4. Decision tree — куда класть новый проект

Сверху вниз, останавливаемся на первом подходящем пункте.

```
1. Запускаемое приложение (Main, Web host)?
   ├── Публичный HTTP API     → application/api/
   ├── Внутренний сервис       → application/internal/
   └── Bot                      → bots/

2. Roslyn analyzer / source generator?  → generation/

3. Работа с БД (DbContext, миграции, repositories)?
   ├── Новый DbContext                 → database/<Company>.<App>.Database.<Name>/
   ├── Generic infra (specs, base repos)→ database/.../Database.Core/
   └── Только для одного контекста      → внутрь database/<...>.Database.<Name>/

4. HTTP-клиент к нашему API?
   → client/<Company>.<App>.Client.<ApiName>/

5. Типы, пересекающие границы проектов?
   ├── Доменные entities       → models/<Company>.<App>.Entity.Core/
   └── HTTP контракты           → models/<Company>.<App>.Api.Contracts/

6. Cross-cutting инфраструктура (DI, logging, extensions)?
   → shared/

7. Переиспользуемый доменный паттерн с собственной логикой?
   → feature/patterns/<Company>.<App>.Pattern.<Name>/

8. Специализация / реализация общего концепта?
   → feature/specified/<...>.Specified.<Name>/

9. Level-0 capability (независимый блок)?
   → feature/<Company>.<App>.<Capability>/

10. Frontend?
    → src/frontend/
```

Не подошёл ни один — **остановись и обсуди**. Новая папка верхнего уровня
— архитектурное решение.

---

## 5. Creating a new project — 5 шагов

```
1. Определить место                     (decision tree, §4)
2. Создать физическую папку             (mkdir)
3. Создать csproj                       (dotnet new <template>)
4. Добавить в solution                  (dotnet sln add --solution-folder)
5. Добавить ProjectReference            (dotnet add reference)
```

**Критично:** шаги 2 и 4 в этом порядке. Solution folder в `.slnx` **не
создаёт** физическую папку — она должна быть на диске **до** `sln add`.

**Шаблоны:** `classlib` для большинства; `webapi` для `application/api/`;
`worker` для `application/internal/` и `bots/`; `console` для `bots/` и
benchmarks; `xunit` для тестов; `classlib` с `netstandard2.0` для
`generation/`. Test framework фиксируется **один** на solution.

**Solution folder = physical path** — должна точно соответствовать
физическому пути.

### Минимальный csproj

Общие свойства (`TargetFramework`, `Nullable`, `ImplicitUsings`,
`LangVersion`, `TreatWarningsAsErrors`) — в `Directory.Build.props`.
csproj содержит только `ProjectReference` и `PackageReference`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup />
  <ItemGroup>
    <ProjectReference Include="..\..\..\shared\Acme.Shop.Logging\Acme.Shop.Logging.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
  </ItemGroup>
</Project>
```

❌ Не дублировать: `TargetFramework`, `Nullable`, `ImplicitUsings`,
`LangVersion` (общее), версии пакетов (централизованно).

**Только `ProjectReference`** внутри solution. `PackageReference` — только
для внешних NuGet.

### CI проверка целостности

```bash
diff <(find src tests -name '*.csproj' | sort) \
     <(dotnet sln <solution>.slnx list | grep '\.csproj$' | sort)
```

Расхождение → fail в CI. Защита от забытого `dotnet sln add`.

Удаление проекта: `dotnet sln remove` + `rm -rf`. Переименование —
**удаление + создание заново**, не переименование csproj.

---

## 6. Internal project structure

Базовый шаблон + специализации:

```
<Project>/                              # базовый
├── <Project>.csproj
├── Interfaces/                         # все интерфейсы (см. `code-shape.md` §8)
├── Models/                             # проект-specific
├── Extensions/
└── <ConcreteClasses>.cs
```

```
<Feature>/                             # feature-проект
├── <Feature>.csproj
├── Interfaces/, Models/
├── Services/                           # бизнес-сервисы
├── Handlers/                           # message/event handlers
└── Extensions/ServiceCollectionExtensions.cs
```

```
<App>/                                 # application-проект
├── <App>.csproj
├── Program.cs
├── appsettings.json, appsettings.Development.json
├── Configuration/                      # Options pattern
└── Endpoints/ или Controllers/
```

❌ Папки `Helpers/`, `Utils/`, `Common/`, `Misc/`, `Tools/` внутри проекта
**запрещены**.

---

## Связанные правила

- правило слоёв проекта (project-local) — обзор слоёв
- `architecture.md` — объявленный стиль архитектуры + 6 законов
- `project-deps-and-tests.md` — layer dependencies, testing structure
- `process/project-slnx-registration.md` — чек-лист регистрации нового .csproj