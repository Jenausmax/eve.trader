---
description: react 19 component shape — functional components, named exports, hooks, prop interfaces, cn()
globs: ["**/*.tsx"]
always: false
---

# React 19 components

Applies to the frontend (`web/`, React 19 + Vite). Worked examples use a
generic console app; the conventions are shared across the FE stack.

## 1. Component shape

- **Function components only.** No class components. No `React.FC` — type props
  via an explicit interface parameter.
- **Named exports for components and pages.** No `export default`.
- One component family per file; small presentational helpers may live beside
  the component in the same file (mirrors the C# "1 type/file" spirit but is
  looser for co-located sub-components).

```tsx
// Good
export interface ItemRowProps {
  item: ItemSummary;
}

export function ItemRow({ item }: ItemRowProps) {
  return <tr>{/* … */}</tr>;
}
```

```tsx
// Bad — default export, React.FC, inline anonymous props
const ItemRow: React.FC<{ item: ItemSummary }> = ({ item }) => <tr />;
export default ItemRow;
```

## 2. Props

- Name the interface `<Component>Props`; export it so stories/tests can reuse it.
- Prefer required props; make optional only what is genuinely optional (`?`).
- Accept `className?: string` on reusable primitives and merge with `cn()`.

## 3. Class names — always `cn()`

Merge Tailwind classes through the shared `cn()` helper (`clsx` +
`tailwind-merge`). Never concatenate class strings by hand.

```tsx
import { cn } from '@/shared/lib/utils';

<span className={cn('pill', status === 'error' && 'bg-err-soft text-err-ink', className)} />
```

## 4. Hooks

- Call hooks unconditionally at the top of the component (rules-of-hooks).
- Extract reusable logic into `use*` hooks under `shared/lib/`.
- Keep effects minimal; prefer derived state and TanStack Query over `useEffect`
  data-fetching (see `tanstack-query-and-router.md`).

## 5. Enforcement

`bun run lint` (eslint-plugin-react-hooks + typescript-eslint, `--max-warnings 0`)
and `bun run typecheck` (`tsc --noEmit`) must both exit 0.
