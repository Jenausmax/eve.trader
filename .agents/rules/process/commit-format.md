---
description: commit format is [<project>](<area>): <subject> — project tag, required area path, imperative subject
priority: high
always: true
---

# Commit format

Every commit in this repository follows exactly one shape:

```
[<project>](<feat/...>): <subject>
```

Three required parts, no exceptions:

| Part | Description |
|---|---|
| `[<project>]` | Тег проекта, литерал. Отличает коммиты этого репозитория в общей рабочей области из нескольких репозиториев. Значение объявляет сам проект (например `[hybrid]`), и оно одинаково во всех его коммитах. |
| `(<feat/...>)` | Required feature path in parentheses. Always starts with `feat/` followed by a hierarchical, lowercase kebab-case area (see §Feature path). |
| `<subject>` | Imperative summary, no period, ≤72 chars, lowercase. |

**The feature path is always present** — there is no `[<project>]: <subject>` form.
If a change spans multiple features, pick the dominant one and put the rest in
the body. A change without a clear dominant feature belongs to `feat/meta`
(rules, build, CI, scripts, repo-level config — see §Top-level areas).

## Feature path

A feature path is **lowercase kebab-case**, slashes for nesting. The first segment
is **always** `feat/`; segments after are the area name. Two to five segments is
the sweet spot.

```
[<project>](feat/<area>): <subject>
[<project>](feat/<area>/<sub-area>): <subject>
```

### Top-level areas

The actual list of `feat/<area>` values is **per-project** —
each repo's `.agents/rules/process/commit-format-areas.md` (or
equivalent) defines which areas exist. The global rule is just the
shape:

- `<area>` matches a top-level project area (module, capability, tooling)
- `feat/` prefix is universal — never drop it
- If unsure, `feat/meta` (rules, build, CI, scripts) is the safe default

The per-project area catalog is the illustrative table below (inline). If a
repo needs a fixed, enforced list, it adds `.agents/rules/process/commit-format-areas.md`
in that repo — App currently keeps its areas inline (no separate file).

Examples (illustrative — layer-based areas; a repo may add its own):

| Area | When |
|---|---|
| `feat/<module-name>` | Changes inside `src/modules/<Solution>.Modules.X/` (e.g. `feat/orders`, `feat/invoices`) |
| `feat/<shared-capability>` | Changes inside `src/shared/<Solution>.Shared.X/` (e.g. `feat/kernel`, `feat/http`) |
| `feat/<provider>` | Changes inside `src/providers/<Solution>.Providers.X/` (e.g. `feat/catalog`) |
| `feat/host` | Changes inside `src/host/<Solution>.Host/` (composition root) |
| `feat/build-tools` | Changes inside `src/host/<Solution>.Build.Tools/` (MSBuild SDK, gates) |
| `feat/fe` | Changes inside `web/` (frontend monorepo) |
| `feat/tests` | Changes inside `tests/` (unit + integration) |
| `feat/meta` | Build, CI, deps, scripts, repo-level config, **rules themselves** |
| `feat/docs` | Documentation-only commits (`.agents/docs/`, ADRs, README updates) |

**All commits start with `feat/`** — `feat/` is the universal first segment.
The second segment picks the area. Sub-segments localize further.

### Sub-paths

Append a sub-path to localize the change inside an area:

```
[<project>](feat/clusters): add workload action endpoint
[<project>](feat/clusters/api): wire controllers into Program.cs
[<project>](feat/clusters/application/installers): explicit handler registration
[<project>](feat/runtime-providers/tier-3): docker compose provider
[<project>](feat/runtime-providers/tier-5): k3s provider + kustomize renderer
[<project>](feat/nodeagent): adopt compute abstractions
[<project>](feat/kernel/cqrs): drain domain events into outbox
[<project>](feat/host/mtls): end-to-end mtls with pem certs
[<project>](feat/tests/architecture): assert kernel does not depend on modules
[<project>](feat/meta/ef): regenerate migrations via dotnet ef tool
[<project>](feat/meta/rules): rewrite commit-format rule
[<project>](feat/meta/format): cleanup pre-existing format drift
[<project>](feat/docs): add ADR-0003 for plugin discovery
[<project>](feat/meta/deps): bump dotnet to 10.0.108
```

### Rules for picking a feature path

1. **The first segment is always `feat/`.** No `[<project>](clusters): ...` —
   the second segment picks the area.
2. **The second segment must be a top-level area** from the table above. No
   inventing new top-level areas without discussion in chat.
3. **One feature path per commit.** If a commit touches `feat/clusters` and
   `feat/tests`, prefer the one that drives the change (usually
   `feat/clusters`) and mention the rest in the body.
4. **Don't duplicate path segments.** `[<project>](feat/clusters/clusters/api)`
   is wrong — use `[<project>](feat/clusters/api)`.
5. **Don't use `feat/meta` for code changes.** `feat/meta` is for tooling,
   rules, CI, build, scripts. Code changes use a feature area even if they
   touch a build script — e.g. `feat/nodeagent` for csproj edits in
   `src/agents/<Solution>.NodeAgent/`.

### When no clear area fits

A pure docs change with no implementation impact → `feat/docs`. A
repo-config / rules / build / CI change → `feat/meta`. Anything that
primarily changes a runtime area → that area. If two areas tie, pick the
one that's downstream of the other and mention the upstream in the body.

## Subject

- **Imperative** — "add", "fix", "bump", "wire" — not "added", "fixed", "bumped".
- **No period** at the end.
- **≤72 characters** total in the subject line.
- **Lowercase** for the subject.
- **No "wip", "tmp", "draft" markers** — if it's not ready, don't commit it.
- **File names in subject = OK** (e.g. `fix Dockerfile`, `wire OpenApiBuildTimeExtensions`).
  **Type names in subject = bad** — names go in code, commit messages describe
  intent.

## Body (optional)

Through a blank line after the subject. Wrap at ~72 chars. Explains **why**, not
**what** — the diff already shows what. The body is where context, trade-offs,
and risk live.

```
[<project>](feat/kernel/messaging): make outbox insert atomic with aggregate write

Раньше outbox insert делался отдельным SaveChanges — между ним и
aggregate write другой writer мог прочитать half-committed state.
Склеили в одну транзакцию через ChangeFeedPublish + TransactionBehavior
(см. ADR-0002 §co-commit).

Verified: 10-writer race in tests/App.ArchitectureTests.Integration/Outbox,
no duplicate / lost rows.
```

## Footer (optional)

Through a blank line after the body. For breaking changes and ticket refs:

```
[<project>](feat/clusters/api): change /workspaces response shape

BREAKING CHANGE: /workspaces now returns { items, total } instead of array.
Migration: clients must read .items.

Refs: COM-142
```

## Good

```
[<project>](feat/clusters): add workload action endpoint
[<project>](feat/clusters/application): wire create-workload command handler
[<project>](feat/clusters/infrastructure): migrations for workloads table
[<project>](feat/runtime-providers/tier-3): docker compose provider
[<project>](feat/runtime-providers/tier-4): podman quadlet provider
[<project>](feat/runtime-providers/tier-5): k3s provider + kustomize renderer
[<project>](feat/nodeagent): adopt compute abstractions
[<project>](feat/host/mtls): end-to-end mtls with pem certs
[<project>](feat/host/cert-authority): ca + x509 issuer
[<project>](feat/kernel/cqrs): drain domain events into outbox
[<project>](feat/kernel/messaging): co-commit outbox + aggregate writes
[<project>](feat/tests/architecture): assert kernel does not depend on modules
[<project>](feat/meta/ef): regenerate migrations via dotnet ef tool
[<project>](feat/meta/rules): rewrite commit-format rule
[<project>](feat/meta/format): cleanup pre-existing format drift
[<project>](feat/docs): add ADR-0003 for plugin discovery
[<project>](feat/meta/deps): bump dotnet to 10.0.108
[<project>](feat/fe/clusters): wire clusters screen to control-plane api
```

## Bad

```
feat(clusters): add workload endpoint             ← no [<project>] tag, no feat/ prefix
chore(format): apply fixes                       ← Conventional Commits 1.0.0, deprecated
[app](clusters): add workload endpoint           ← old [app] tag, missing feat/
[<project>] add workload endpoint                    ← feature path missing entirely
[<project>](clusters) add workload endpoint          ← missing feat/ prefix
[<project>](feat/clusters) add workload endpoint     ← missing `: ` separator
[<project>](feat/clusters/clusters): add workload   ← duplicate path segment
[<project>](feat/clusters/api): Added endpoint.      ← past tense, period at end
[<project>](feat/clusters/api): wip                 ← WIP marker
[<project>](feat/PROJ): add workload                ← uppercase feature path
[<project>](feat/clusters/api): this is a long subject that exceeds the seventy-two character limit and rambles on
[<project>](feat/clusters/api): add WorkloadActionCommand   ← TYPE names in subject
```

## Validation regex

For tooling (commit-msg hook, CI lint, etc.):

```regex
^\[<project>\]\(feat/([a-z][a-z0-9-]*(/[a-z][a-z0-9-]*)*)\): .{1,72}$
```

Group 1 = feature path (without the leading `feat/`), rest = subject. The body
and footer are not checked by the regex.

## History migration

- **Bootstrap era** (before this rule): commits used Conventional Commits 1.0.0
  (`feat(scope): subject`, `fix(scope): subject`). **Not rewritten** — the
  rule applies forward only.
- **`[app]` era**: commits used `[app](<area>): subject` without a
  `feat/` prefix. many commits from 2026-05 to 2026-07 are in this
  form. **Not rewritten** — forward only. If a future migration becomes
  desirable, do it via `git rebase -i --exec`, **not** a mass `filter-branch`.

## Приоритет над личными настройками

У харнесса и у разработчика могут быть свои правила коммита — например в
пользовательском каталоге агента. Это правило их перекрывает: формат коммита
принадлежит репозиторию, а не машине, с которой в него пишут. Иначе история
одного проекта окажется в двух форматах в зависимости от того, кто коммитил.

## Other places this format appears

If you copy a snippet from these rules or a previous commit, double-check
the format. Common places `[app]` or `feat/` sneaks in:

- `.agents/rules/csharp/analyzers.md` §"Что делать при новом warning" —
  example commit
- `.agents/rules/process/build-verification.md` §"Good / Bad" — example
  commit in git command

If you find a `[app](<area>)` reference in any `.agents/rules/*.md` file
that's NOT in `commit-format.md` (this file), update it to
`[<project>](feat/<area>)`. Format drift applies to docs too.
