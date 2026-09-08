---
description: Mandatory self-audit gate — run after writing code, before git commit. Catches analyzer violations + naming/style violations that the compiler does NOT enforce.
priority: high
always: true
---

# Worker self-audit gate

После того как ты написал код, и **до** `git commit` — ОБЯЗАТЕЛЬНО прогон
этого gate. Цель: поймать нарушения правил которые ты внёс, и
обнаружить пробелы где существующих правил не хватает.

Skipping = полагаться на CI чтобы поймать то что ты пропустил. Это
противоположно self-verification.

**Hard rule.** Каждый шаг — exit 0. Иначе fix → повторить. Max 3 итерации.

## Шаги (по порядку)

### Шаг 0. Format drift (ОБЯЗАТЕЛЬНО перед build)

```bash
dotnet format <solution>.slnx --severity hidden
```

Format drift — самый частый повод «я не могу закоммитить, CI красный».
VerifyFormatOnBuild target валит на любом drift, так что build упадёт.
**Чини до build, не после**.

Если drift слишком большой (50+ violations), вынеси в отдельный
«format cleanup» commit — не смешивай с feature-работой.

### Шаг 1. Build + analyzer warnings

```bash
dotnet build <solution>.slnx -c Debug
```

**Любой warning = failure.** В этом проекте `TreatWarningsAsErrors=true`
глобально, build падает автоматически. Та же команда прогоняет
format-гейт (`VerifyFormatOnBuild`) — drift тоже валит build.

**Чини код, не подавляй warning.** Документированный escape hatch
только через `analyzers.md` owner approval.

### Шаг 2. **MANDATORY self-audit grep на изменённых файлах**

**Это критический шаг, который ты раньше пропускал.** Build + analyzers
ловят ~80% violations, но НЕ ловят:
- Naming convention violations (forbidden abbreviations, banned suffixes)
- Private methods (no analyzer exists yet)
- Banned patterns: `throw ex`, `_ = discard`, `async void`, `_field` prefix
- Wrong return type abstractions (List<T> vs IReadOnlyCollection<T>)
- File organization (multiple types per file, wrong namespace)

Эти violations проходят build и попадают в production код.

#### 2a. Определи scope изменений

```bash
git diff --name-only --diff-filter=AM HEAD
# или если не staged:
git diff --name-only --diff-filter=AM
```

Запиши список файлов в `$CHANGED` (только .cs / .csproj — markdown и dotfiles не трогаем).

#### 2b. Прогон self-audit grep'ов

Запусти **ВСЕ** команды ниже. Любой match = violation = fix или обоснование.

**Naming (csharp/naming-and-types.md):**

```bash
# Запрещённые сокращения параметров (ct, req, resp, err, msg, svc, u, x, tmp)
rg -n "\b(ct|req|resp|err|msg|svc|u|x|tmp)\b\s*[,)]" $CHANGED --type cs

# Underscore-prefixed private fields (запрещены)
rg -n "^\s*private\s+(readonly\s+)?\w+(\[\])?\s+_\w+" $CHANGED --type cs

# Banned class suffixes (Dto, Model, Impl, Util, ViewModel)
rg -n "class\s+\w+(Dto|Model|Impl|Util|Utility|ViewModel)\b" $CHANGED --type cs

# Lambdas with one-letter params (forbidden; _ allowed for discard)
rg -n "\(\w+\s*=>" $CHANGED --type cs | rg -v "_\s*=>|_\s*\)"
```

**Class structure (csharp/class-layout-and-tooling.md):**

```bash
# Private methods (no framework override) — должен быть 0
rg -n "^\s*private\s+(static\s+)?(async\s+)?[A-Z]\w+\s+\w+\(" $CHANGED --type cs

# Allowed exemptions (these match legitimate uses — keep):
#   private bool Equals(MyType other)            # IEquatable override
#   private void Dispose(bool disposing)         # IDisposable pattern
#   private static async Task<...> GetXxxAsync(...) # minimal API endpoint handler
# Verify each match is in one of these categories; otherwise extract to file-static helper.
```

**Code shape (csharp/code-shape.md):**

```bash
# throw ex; (wrong — resets stack trace)
rg -n "throw\s+\w+\s*;" $CHANGED --type cs

# _ = discard pattern (forbidden; use bare call)
rg -n "^\s*_\s*=\s" $CHANGED --type cs

# async void (forbidden)
rg -n "async\s+void\b" $CHANGED --type cs

# Expression-bodied method (block body required)
rg -n "=>\s+[^;]+;" $CHANGED --type cs | rg -v "=>\s+(new\s|true|false|null|\$|\"[^\"]*\"|[0-9]+|new\s*\()" | rg "=>"

# List<T> in public API surface (use IReadOnlyCollection<T>)
rg -n "public\s+(static\s+)?(\w+\s+)?IEnumerable<" $CHANGED --type cs
rg -n "public\s+\w+<[^>]*>\s+\w+\s*[;{]" $CHANGED --type cs | rg "List<" | rg -v "private|file"

# Guard-only local: присвоение, чья локальная проверяется следующим же if
# (code-shape §1 формы A/B — слить в `is { } x` / property-pattern)
rg -nU 'var (\w+) = [^;]+;\r?\n(?:\s*//[^\n]*\r?\n)*\s*if \(\1\b' $CHANGED --type cs -P

# Repeated null-guard: одна локальная null-гейтится в соседних if — один внешний if
rg -nU 'if \((\w+) is not null[^{]*\{[^}]*\}\s*(?://[^\n]*\r?\n\s*)*if \(\1 is not null' $CHANGED --type cs -P
```

**Async (csharp/async-and-tasks.md):**

```bash
# Task/ValueTask return without Async suffix
rg -n "public\s+(async\s+)?Task<" $CHANGED --type cs | rg -v "Async"
rg -n "public\s+(async\s+)?ValueTask<" $CHANGED --type cs | rg -v "Async"

# ConfigureAwait in app code (forbidden)
rg -n "\.ConfigureAwait" $CHANGED --type cs

# CancellationToken named ct (forbidden abbreviation)
rg -n "CancellationToken\s+ct[,)\s]" $CHANGED --type cs
```

**Anti-patterns (csharp/anti-patterns.md):**

```bash
# ArgumentNullException.ThrowIfNull (forbidden under nullable)
rg -n "ArgumentNullException\.ThrowIfNull" $CHANGED --type cs

# Tuples in public API
rg -n "public\s+\([^)]+\)\s+\w+\s*\(" $CHANGED --type cs

# #region directive (forbidden)
rg -n "^\s*#region\s" $CHANGED --type cs
```

**Folder organization (csharp/folder-organization.md):**

```bash
# Multiple public types in one file
rg -n "^public\s+(sealed\s+)?(class|record|static\s+class|interface)\s+\w+" $CHANGED --type cs

# File-scoped namespace expected (not block-scoped)
rg -n "^namespace\s+\w+(\.\w+)+\s*$" $CHANGED --type cs | rg -v ";"
rg -n "^\s*\{\s*$" $CHANGED --type cs | head -5
# (manual check: any namespace { } block style is wrong)

# Wrong namespace (doesn't mirror folder path)
rg -n "^namespace\s+" $CHANGED --type cs
# (visual: namespace App.Foo.Bar must match file at /App/Foo/Bar.cs)
```

#### 2c. Manual cross-check на diff

После grep'ов — прочитай каждый изменённый файл ещё раз, ищи:
- Primary ctor violations (`public class X { public X(...) { ... } }` когда можно primary)
- Unsealed concrete classes (должны быть `public sealed class` кроме abstract)
- XML doc missing (`public class` без `/// <summary>` выше)
- File header / type declaration order — проверь member ordering из class-layout §1

#### 2d. Все matches → fix или обоснование

Каждый match из grep'ов выше — это violation. Действия:
1. **Fix code** (предпочтительно).
2. **Suppress локально** через `#pragma warning disable` — только если
   это documented escape hatch (см. `analyzers.md`).
3. **Обоснование в commit message** — если fix отложен (покажи
   почему именно этот match — exception, не generic skip).

**Если не уверен** — спрашивай owner, не скрывай.

### Шаг 3. Tests

```bash
dotnet test <touched-test-project> --no-build
```

Если затронуты тесты — прогон обязателен. Skip только если
100% уверен что изменение не может повлиять (e.g. переименование,
комментарии).

### Шаг 4. Commit

Только после шагов 0-3 с exit 0:

```bash
git add $CHANGED
git commit -m "[<project>](feat/<area>): <subject>"
```

Commit format — см. `commit-format.md`.

## Почему это mandatory

Pi загружает global rules в system prompt, но **НЕ загружает .cs файлы**.
LLM может пропустить violation пока редактирует один файл, не видя
остальные. Self-audit grep закрывает эту дыру:

- Build/analyzers — ловят compile-time violations
- Self-audit grep — ловят naming/style/organization violations
- Code review (human) — ловит semantic/business logic

Если skip self-audit — agent пишет "code that passes build" но это
может быть процедурный код, private методы, banned naming, etc.
Exactly the problems that у owner (you) уже были в начале этой
сессии ("куча нарушений в нейминге").

## Связанные правила

- `csharp/naming-and-types.md` — все grep naming
- `csharp/class-layout-and-tooling.md` — grep private methods
- `csharp/code-shape.md` — grep code shape violations
- `csharp/async-and-tasks.md` — grep async violations
- `csharp/anti-patterns.md` — grep anti-patterns
- `csharp/folder-organization.md` — grep file organization
- `analyzers.md` — escape hatches, severity rules
- `../process/build-verification.md` — build gate details