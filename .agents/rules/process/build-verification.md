---
description: когда и как прогонять build+verify на backend (.net) и frontend (react/ts), какие ошибки недопустимы перед коммитом
globs: ["**/*.cs", "**/*.csproj", "**/*.sln", "**/*.slnx", "**/*.ts", "**/*.tsx", "**/package.json", "**/tsconfig*.json", "**/vite.config.*"]
priority: high
interactive: false
always: true
---

# Build & Verification

Это правило описывает, **когда** и **как** запускать сборку backend (.NET) и
frontend (React/TypeScript), какие состояния считаются failure, и что делать
с pre-existing ошибками в незатронутых файлах.

## Анти-drift checklist — самое важное

> **Каждый format drift, который ты вносишь, чинится в том же коммите,
> где он появился.** Не «починю потом», не «это pre-existing», не
> «у меня времени нет». Format drift — токсичная задолженность: 10
> warnings сегодня = 373 warnings через месяц = часовая чистка которую
> никто не хочет делать. Поэтому:

**Перед коммитом (любой нетривиальной правки):**

```
1. git diff --stat      ← посмотри что менял
2. dotnet build <solution>.slnx -c Debug
   ├── если ошибки компиляции / analyzers (warning|error) → почини
   └── если format drift (RCS/IDE/CA/MA в obj/format-verify.log):
       3. dotnet format <solution>.slnx --severity hidden
          ← ЭТО часть работы над коммитом. Не пропускай.
       4. повторить build → убедиться что 0 drift
5. dotnet test tests/unit/<touched-project> --no-build
6. если затронут FE → cd web && bun run typecheck && bun run lint && bun run test
7. git add + git commit
```

**Не «git commit → CI поймает»**. К моменту CI-fix-а в истории уже лежит
коммит с drift-ом, и при rebase / cherry-pick другие агенты будут получать
его в качестве pre-existing.

## Единственная команда верификации backend

> **`dotnet build <solution>.slnx -c Debug`** — это ВЕСЬ гейт. Одна команда,
> одинаковая на всех платформах, та же, что в CI.

`dotnet build` делает три вещи за один прогон:

1. **Компиляция.**
2. **Анализаторы** (CA / RCS / MA / VSTHRD / IDE) — через
   `EnforceCodeStyleInBuild=true` + `TreatWarningsAsErrors=true` в
   `Directory.Build.props`. Любой `warning` = ошибка сборки.
3. **Format-check** — target `VerifyFormatOnBuild` (в
   `src/build/<Solution>.Build.Tools/<Solution>.Build.Tools.targets`) запускает
   `dotnet format <solution>.slnx --verify-no-changes --severity hidden` один
   раз на solution-сборку. **Format drift = build FAILS** (жёсткий гейт).

Зелёный `dotnet build` ⇒ код соответствует стандарту. Точка. Это убирает
ситуацию «локально прошло, CI красный»: CI гоняет ту же команду.

**Важно:** только **solution**-сборка (`<solution>.slnx`). При сборке одного
проекта (`dotnet build src/foo/foo.csproj`) `<Solution>.Build.Tools` не попадает
в граф → format-check не запускается.

Эскейпы (только для inner-loop, не для «готово»):

```bash
dotnet build <solution>.slnx -c Debug -p:DisableFormatOnBuild=true        # пропустить format-гейт
dotnet build <solution>.slnx -c Debug -p:FormatOnBuildTreatAsWarning=true # drift как warning, не error
```

**`--severity hidden`** — это критический флаг при ручном fix:

```bash
dotnet format <solution>.slnx --severity hidden
```

Без флага `dotnet format` идёт на дефолтном `--severity warn` и **не чинит**
правила ниже `warning` (silent/suggestion — например, `IDE0320`, `IDE0305`,
`RCS1161`). Без флага `dotnet format` отработает «вхолостую», а гейт
продолжит падать на тех же violations.

**Только whitespace (быстрый фикс):** если `dotnet format --severity hidden`
застревает на analyzer errors (например RCS1163; MA* удалены 2026-07-19) и не доходит до whitespace
cleanup, используй сабкоманду:

```bash
dotnet format <solution>.slnx whitespace
```

Это запускает **только** whitespace-правила (пустые строки, trailing spaces,
EOL) и **не** запускает analyzer rules — не застрянет ни на чём. Проверить
без изменения: `dotnet format <solution>.slnx whitespace --verify-no-changes`.

## Agent contract — Definition of Done

Перед тем как сказать «готово» — прогнать набор. Без сокращений:

```bash
# 1. Fix drift (если есть)
dotnet format <solution>.slnx --severity hidden

# 2. Build = компиляция + анализаторы + format-гейт (единственная команда).
dotnet build <solution>.slnx -c Debug

# 3. Тесты (затронутые проекты).
dotnet test tests/unit/<ProjectName>.Unit --no-build --nologo

# 4. Frontend — если затронут (нет единого `gate`-скрипта — три команды подряд).
cd web && bun run typecheck && bun run lint && bun run test
```

**Правило:** exit ≠ 0 от **любой** команды = задача **не готова**. Чинить и повторить.

**Нет pre-commit hook и нет shell-helpers для авто-формата** (см. «Почему нет
pre-commit hook» ниже). Gate обеспечивается через:

1. `VerifyFormatOnBuild` target — локально, при каждом `dotnet build`.
2. CI — `build` job гоняет ту же `dotnet build <solution>.slnx -c Debug`.
3. Сам агент — это правило + checklist выше.

**Исключение (только горячий hotfix):** если коммит блокирует прод, а гейт
мешает — пропустить format через `-p:DisableFormatOnBuild=true` и починить в
следующем коммите. **Не норма**, оправдано только срочностью, отметить в
commit message (см. `commit-format.md`).

## Почему нет pre-commit hook (см. Anti-drift checklist выше)

Pre-commit hook `dotnet format` + auto-amend был вариантом, но отвергнут:
- **Bypassable**: `git commit --no-verify` пропускает хук, агент под
  давлением может сорваться.
- **State confusion**: auto-amend меняет staged files пока агент пишет
  commit-message — в момент `git commit` файлы уже не те, что он видит.
- **Cross-platform friction**: bash на Windows (Git Bash) и PowerShell дают
  разные хуки; C#-проект + cross-tooling → два хука, две проблемы.
- **Решение дешевле**: rule + checklist. Если агент следует `process/build-verification.md`,
  drift не ленднет. Build-gate автоматический, agent-gate явный (checklist).

## Когда применять

Применяется ко **всем** нетривиальным правкам в `src/`:

| Затронутая сторона | Паттерн файлов | Что проверять |
|--------------------|----------------|---------------|
| Только BE | `**/*.cs`, `**/*.csproj`, `**/*.slnx` | `dotnet build <solution>.slnx -c Debug` |
| Только FE | `**/*.ts`, `**/*.tsx`, `**/package.json`, `**/tsconfig*.json`, `**/vite.config.*` | FE build |
| Обе стороны | mix of above | **Оба** билда (см. Dual-Build Rule) |
| OpenAPI / контроллеры API | `**/*.cs` с `[ApiController]` / `**/openapi.v1.json` | BE + регенерация FE API client |

**Тривиальные правки** (опечатки в markdown, переименование файла) — полный
прогон не требуется, но если Format drift остаётся — прогоните хотя бы
`dotnet format <solution>.slnx --severity hidden`.

**Не применяется** к:
- Правкам только в `.agents/docs/`, `.planning/`, `**/*.md`
- Правкам только в `src/feature/*/Generated/` (Mapperly / source-gen)
- Правкам только в `**/Migrations/**/*.cs` (EF Core design-time artifacts —
  excluded from format-gate + analyzer style rules via a `[**/Migrations/*.cs]`
  block in `.editorconfig`)

## Что ловит build, чего не ловит «голый компилятор»

`VerifyFormatOnBuild` (`dotnet format --severity hidden`, самый permissive
уровень) внутри build ловит то, что анализаторы при компиляции в принципе
не покрывают:

- **Whitespace** — BOM, trailing-whitespace, EOL, пустые строки между членами.
- Style-rules с severity ниже `warning` (`silent`/`suggestion`), у которых
  есть auto-fix.

Поэтому отдельный `dotnet format --verify` запускать не нужно — он уже внутри
`dotnet build`.

**Глобально suppressed** (см. `.editorconfig` + `Directory.Build.props`):
- `RMG012` (Mapperly), `CS1573` (param comment в partial-методах),
- `xUnit1004` — `[Fact(Skip = "...")]` is the canonical xUnit pattern
  for environment-dependent tests; v0.1 keeps Skip parameter.

Все остальные diagnostics — **починить до коммита**. Не отключать анализаторы,
не править `.editorconfig` ради одного warning (см. `csharp/ef-migrations.md`).

## Frontend

FE-монорепо живёт в `web/` (bun + turbo + React 19 + Vite). PM — **bun**,
не npm/pnpm.

**Единого `gate`-скрипта в `web/package.json` пока нет** — FE-гейт это три
команды подряд (typecheck + lint + test). Если добавить alias
`"gate": "bun run typecheck && bun run lint && bun run test"` в console-приложение —
тогда `cd web && bun run gate`.

```bash
cd web && bun run typecheck && bun run lint && bun run test   # каноничный FE-гейт
cd web && bun run build                                       # если менялся build-вывод (vite)
```

**Требования к выходу:**
- Exit code = `0`, **0 TypeScript errors**, eslint чисто.
- Vite warnings (chunk size > 500kb, dynamic import hints) — допустимы.

## Dual-Build Rule

Если задача затрагивает **обе стороны** (новый endpoint + страница, смена
контракта API, схема БД, отражающаяся в FE) — прогнать **обе** сборки:

```bash
# 1. Backend (компиляция + анализаторы + format-гейт).
dotnet build <solution>.slnx -c Debug

# 2. Frontend (нет единого `gate`-скрипта — три команды подряд).
cd web && bun run typecheck && bun run lint && bun run test

# 3. Опционально: регенерация API client, если менялись контроллеры.
cd web && bun run codegen
cd web && bun run build
```

**Обе** должны дать exit 0. Недопустимо «FE компилируется, BE потом починю».

## Pre-existing drift — отдельная задача

Если при первом запуске `dotnet build <solution>.slnx -c Debug` в начале сессии
format-gate падает на drift, который ты **не вносил** в этой сессии:

| Ситуация | Что делать |
|----------|------------|
| Drift в **затронутом** файле | ✅ Починить в рамках текущей задачи (если fix тривиальный) ИЛИ отдельный commit «fix format drift» |
| Drift в **незатронутом** файле, < 50 violations | ✅ Отдельный cleanup-коммит в начале сессии — НЕ смешивать с feature-работой |
| Drift в **незатронутом** файле, > 50 violations | ⚠️ Можно разбить на несколько коммитов, **отметить в commit message** что это pre-existing |
| Техдолг в `.planning/BACKEND-ISSUES.md` | ⚠️ Можно отложить, **отметить в commit message** |

❌ **Запрещено:**
- `// @ts-ignore` / `// eslint-disable-next-line` без обоснования.
- `dotnet_diagnostic.* = none` в `.editorconfig` для подавления warning.
- `#pragma warning disable` без `restore` и без комментария «почему».
- `-p:DisableFormatOnBuild=true` в финальном build (только inner-loop escape).

## Format drift — scope discipline (для агентов)

`dotnet format --severity hidden` — **solution-wide по умолчанию**. Когда
agent (или человек) запускает его на solution-wide scope, он чистит
pre-existing drift в файлах, которых никто не касался в текущей задаче.
Это нормальное поведение инструмента, не баг — но agent ответственен
за то, чтобы эти чужие изменения НЕ попали в feature-PR.

**Hard rule:** agent НЕ ДОЛЖЕН коммитить файлы, которые он не менял
намеренно. Протокол:

1. Перед `git add` запусти `git status --short` и просмотри каждый файл.
   Если файл НЕ был частью задачи:
   - Либо откати: `git checkout HEAD -- <file>`
   - Либо вынеси в отдельный cleanup-коммит (mixed-cleanup требует
     явного одобрения пользователя)

2. **Предпочитай scoped format** чтобы избежать blast radius:
   ```bash
   # Per-file
   dotnet format path/to/SpecificFile.cs --severity hidden
   # Per-project
   dotnet format path/to/Project/Project.csproj --severity hidden
   # Только когда явно чистишь pre-existing drift (с одобрения пользователя):
   dotnet format anlytra.slnx --severity hidden
   ```

3. `VerifyFormatOnBuild` target ловит drift в **графе проекта билда**
   — solution-wide он не падает. Adjacent projects' drift — НЕ ломает
   build, но agent всё равно должен его НЕ коммитить в feature-commit.

**Что должен делать orchestrator** когда sub-agent финиширует commit:

1. Запустить `git diff --name-only HEAD~1 HEAD`
2. Если видишь файлы вне scope коммита — либо revert, либо
   split в отдельный cleanup-commit
3. Если всё ожидаемо — accept

## Перед коммитом — чеклист (TL;DR)

```
1. git diff --stat  → определил затронутую сторону (BE / FE / обе)
2. dotnet format <solution>.slnx --severity hidden
   ← ОБЯЗАТЕЛЬНО. Даже если вроде всё чисто. 5 сек.
3. dotnet build <solution>.slnx -c Debug
   ← exit 0? Если нет → fix → повторить.
4. dotnet test tests/unit/<ProjectName>.Unit --no-build
   ← pass? Если нет → fix → повторить.
5. (если FE) cd web && bun run typecheck && bun run lint && bun run test
6. git add + git commit
```

**Запрет:** коммитить до того как все 6 шагов дали exit 0. Без исключений
кроме горячего hotfix (см. выше).

## Good / Bad

```bash
# ✅ Correct — затронут только BE, format gate зелёный
$ dotnet format <solution>.slnx --severity hidden
$ dotnet build <solution>.slnx -c Debug
 ... Build succeeded. 0 Warning(s) 0 Error(s)
$ git commit -m "[<project>](feat/<area>): add Bar endpoint"
```

```bash
# ❌ Wrong — затронут только BE, format drift не починил
$ dotnet build <solution>.slnx -c Debug
 ... error : [VerifyFormatOnBuild] Format drift detected: 47 violation(s)
$ git commit --no-verify -m "..."   # BUG: drift ленднет в историю
```

```bash
# ❌ Wrong — свой набор команд вместо стандарта
$ dotnet format ... --severity warn  # не тот severity, не та проверка
$ dotnet build src/foo/foo.csproj    # per-project → format-гейт не сработал
# Стандарт ОДИН: dotnet build <solution>.slnx -c Debug
```

## Связанные правила и файлы

- `.agents/rules/csharp/ef-migrations.md` — миграции EF
- `.agents/rules/architecture/persistence.md` — Repository/Specification patterns
- `.editorconfig` — severity правил (прод / тесты)
- `Directory.Build.props` — глобальные suppressed warnings, `TreatWarningsAsErrors`
- `src/build/<Solution>.Build.Tools/<Solution>.Build.Tools.targets` — `VerifyFormatOnBuild` гейт
- `.planning/BACKEND-ISSUES.md` — зафиксированный техдолг
