---
description: styling — prefer ds primitives, oklch tokens in index.css are source of truth, tailwind utilities, theming
globs: ["**/*.tsx", "**/*.css"]
always: false
---

# Styling & design system

The FE is **Base UI + shadcn-style wrappers** + **Tailwind CSS 4** with an
**OKLCH token system**. The app's `index.css` is the single source of truth for
design tokens — not the UI docs, which drift.

## 1. Prefer DS primitives over raw HTML

When a primitive exists in `shared/ui/primitives/` (Button, Input, Select,
Dialog, Tabs, …) or a domain component exists in `shared/ui/<domain>/`
(Duration, StatusPill, …), use it. Reach for raw `<button>`/`<input>` only when
no primitive fits.

```tsx
// Good
import { Button } from '@/shared/ui/primitives/button';
<Button variant="ghost" size="sm">Filter</Button>
```

## 2. Tokens, not hardcoded values

Style with Tailwind utilities that map to the token layer (`@theme inline` in
`index.css`). Never hardcode hex colors or px spacing that a token covers.

- Surfaces: `bg-background`, `bg-card`, `bg-surface-2`, `bg-surface-3`.
- Ink: `text-foreground`, `text-muted-foreground`, `text-fg-2`.
- Borders: `border-border`, `border-border-2`.
- Primary action: `--primary` (the brand accent — used by the `Button` default
  variant and links). `--accent` / `--ring` are the **neutral** pair — do not
  confuse them with the brand accent.
- Status semantics (the only colored values): `bg-ok-soft text-ok-ink`,
  `bg-err-soft text-err-ink`, `bg-warn-soft text-warn-ink`, `bg-idle-soft`,
  `bg-slow-soft text-slow-ink`, and log levels `--level-{trace…fatal}`. Never use
  status colors for decoration.
- Numerics (durations, timestamps, IDs) use `font-mono` + `tabular-nums`.

## 3. Hand-written component CSS lives in index.css

App-level classes that aren't Tailwind utilities (`.pill`, `.duration`,
`.time`, `.app-shell`, and whatever the app's own domain components need) are
defined in `index.css`. Reuse them; don't reinvent equivalents inline.

## 4. Theming — light + dark, both required

Dark mode is driven by `.dark` (app) and `[data-theme="dark"]` (Ladle). Any new
token needs both a light (`:root`) and dark counterpart. Never hardcode a color
that won't invert.

## 5. Catalog = Ladle

The component catalog is **Ladle** (`bun run playbook:dev`, stories under
`stories/**`) — not a hand-written static-HTML playbook site. New primitives and
domain components get a `*.stories.tsx`.

## 6. Enforcement

`bun run lint` + `bun run typecheck` exit 0. Visual review via the Ladle catalog.
