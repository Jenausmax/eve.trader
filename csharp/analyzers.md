---
description: c# analyzer packages — IDE / VSTHRD / CA-security wiring. как они подключены, что делать при новых warnings.
globs: ["**/*.csproj", "**/Directory.Build.props", "**/Directory.Packages.props", "**/.editorconfig"]
always: true
---

# Analyzer packages — cheatsheet

C#-анализаторы подключены **централизованно** в `Directory.Build.props`.
CPM выключен (см. комментарий в `Directory.Packages.props`) — версии пакетов
явные.

## Что активно (сокращённый набор, 2026-07-13 — «оставить IDE + async»)

| Анализатор | Статус | Что ловит |
|-----------|--------|-----------|
| **IDE** (code-style, SDK) | `EnforceCodeStyleInBuild=true` | house style: var / braces / file-scoped ns / naming / pattern matching / IDE0029-0031 (`??`/`?.`) |
| **VSTHRD** (`Microsoft.VisualStudio.Threading.Analyzers`) | пакет в `Directory.Build.props` | async-safety: `.Result`/`.Wait()`, `async void` (VSTHRD100), Async-суффикс, observe-await |
| **CA** (`Microsoft.CodeAnalysis.NetAnalyzers`, SDK) | **OFF by default** (`AnalysisMode=None`) | включена только **Security** (`AnalysisModeSecurity=All`: SQL/крипта/XXE/десериал.) + opt-in `CA1051` / `CA2200` через `.editorconfig` |
| ~~Meziantou (MA)~~, ~~Roslynator (RCS)~~ | **УДАЛЕНЫ** 2026-07-13 | были шумными; упрощения (`??`/`?.`) покрыты IDE0029/0030/0031 |

Build-флаги в `Directory.Build.props`:
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — warnings = errors
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` — IDE-правила в билд
- `<AnalysisMode>None</AnalysisMode>` + `<AnalysisModeSecurity>All</AnalysisModeSecurity>` — CA off, кроме security

Severity конкретных правил — в `.editorconfig`. Обзорный разбор активных
правил (что ловит + где документировано) — в `analyzer-catalog.md`. Открывай
его при падении на `CA####` / `VSTHRD###` / `IDE####`.

## Когда подключать analyzer к одному проекту

Если analyzer нужен **только** одному проекту (не глобально), в его `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Some.Analyzer" Version="1.*" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` обязателен — иначе analyzer утечёт в runtime-зависимости.

## Что делать при новом warning

**Приоритет — жёсткий, в этом порядке:**

1. **Починить код.** Warnings = code smell. Build падает.
2. **Локально подавить** через `#pragma warning disable RULE // <почему>` +
   `#pragma warning restore RULE`. Разрешено без одобрения владельца —
   ограничено конкретным местом.
3. **Запросить одобрение** на глобальное ослабление severity через `.editorconfig`.
   Ждать `ok` владельца. После одобрения — запись в `.agents/debt/BACKEND-ISSUES.md`
   со ссылкой на чат.

## Hard rule: `.editorconfig` менять только с одобрения владельца

Любые правки `.editorconfig` (severity override, new rule, `[*.cs]` block,
reorganization) требуют **явного одобрения владельца в чате** перед коммитом.
Agent **не** вносит такие правки самостоятельно.

**Почему:** `.editorconfig` — single source of truth для code style на весь
solution. Локально-мотивированное `severity = none` ради обхода текущего
затруднения накапливается (1 правило сегодня, 5 через месяц). Каждое
ослабление — осознанное проектное решение, не convenience-переключатель.

**Запрещено:**

- ❌ Тихо ослаблять severity при первом столкновении.
- ❌ Добавлять `[*.cs]` блок для обхода одного правила.
- ❌ Коммитить `.editorconfig` change в одном PR с другим функционалом — маскировка.
- ❌ Говорить «это просто стиль, неважно» — стиль важен.
- ❌ Добавлять `<NoWarn>` в csproj без записи в baseline-issue.

**Разрешено без одобрения:**

- ✅ Любые правки `*.cs` (code-level fixes).
- ✅ `#pragma warning disable` в конкретном месте с обоснованием.
- ✅ Усиление (добавление нового правила с severity = `error`).
- ✅ Запись в `.agents/debt/BACKEND-ISSUES.md` со ссылкой на issue.

## Чеклист: добавить новый analyzer package

1. `<PackageVersion Include="..." Version="..." />` в `Directory.Packages.props`.
2. `<PackageReference Include="..." PrivateAssets="all" />` в `Directory.Build.props` (для всех) или в `.csproj` (для одного).
3. `dotnet build <solution>.slnx -c Debug` — посмотреть новые warnings.
4. Разобрать warnings: починить / подавить с обоснованием / baseline.
5. Отдельный коммит `[hybrid](meta/deps): add <package> analyzer`.

## Связанные правила

- `analyzer-catalog.md` — что активно (IDE / VSTHRD / CA-security) + где документировано
- `.editorconfig` — severity каждого правила
- `process/build-verification.md` — build gate (компиляция + analyzers + format)
- `process/worker-audit.md` — self-audit gate перед коммитом