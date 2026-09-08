---
description: frontend workspace — bun only, verification gate, i18next for copy, type imports, no dev/watch in agent runtime
globs: ["**/*.ts", "**/*.tsx"]
always: false
---

# Frontend workspace & i18n

## 1. bun, never npm/pnpm/yarn

The FE monorepo is **bun workspaces** (lockfile `bun.lock`). Use `bun install`,
`bun run <script>`, `bun add`. Committing a `package-lock.json` / `pnpm-lock.yaml`
or running `npm`/`pnpm` is a mistake — it desyncs the lockfile.

## 2. Verification gate (all three, exit 0)

There is no single `gate` script — run the three individually from `web/`:

```bash
bun run typecheck   # tsc --noEmit
bun run lint        # eslint --max-warnings 0
bun run test        # vitest run
```

Any non-zero exit = not done.

## 3. Never run long-lived / watch / browser processes

Do NOT run `bun run dev`, `vite`, `tsc --watch`, `vitest --watch`, `playwright`,
`puppeteer`, or `chromium` from an agent — they hang the runtime. See
`process/agent-runtime-safety.md`. Verify with the non-watch commands above.

## 4. i18n — no hardcoded user-facing copy

User-facing strings go through **i18next** (`useTranslation`, `t('key')`), with
locales maintained in parallel (`en` + `ru`). Add the key to every locale file.

```tsx
const { t } = useTranslation();
<PageTemplate title={t('items.list.title')} />
```

Non-user-facing text (dev logs, test names) is exempt.

## 5. Imports & unused vars

- `import type { … }` for type-only imports (`consistent-type-imports`).
- Prefix intentionally-unused bindings with `_` (`no-unused-vars` ignores `^_`).
- Use the `@/` alias for app-internal imports (`@/shared/…`, `@/features/…`).

## 6. Enforcement

The gate in §2 is the contract. Lint enforces §5; `agent-runtime-safety.md`
enforces §3.
