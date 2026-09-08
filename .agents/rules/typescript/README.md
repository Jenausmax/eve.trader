# TypeScript / React rules

Project-neutral rules for the shared frontend stack (React 19 + Vite + bun +
Base UI/shadcn + Tailwind 4 + TanStack Query/Router + i18next). Auto-loaded via
the `typescript/*.md` glob of the rules tree. Worked examples use a generic
console app; the conventions are shared across FE repos.

## Starter set

- `react-and-components.md` — function components, named exports, prop
  interfaces, hooks, `cn()`.
- `tanstack-query-and-router.md` — QueryClientProvider requirement, queryKeys,
  api-client-first fetching, code-based routes, `Link`.
- `styling-and-design-system.md` — prefer DS primitives, OKLCH tokens in
  `index.css` are the source of truth, Tailwind utilities, theming, Ladle.
- `workspace-and-i18n.md` — bun only, the typecheck/lint/test gate, no
  dev/watch in agent runtime, i18next copy, type imports.

## Backlog (expand later)

- Forms (react-hook-form + zod) conventions.
- Testing (vitest + Testing Library) patterns + when to test what.
- API client codegen (openapi-typescript / kubb) once design-first OpenAPI lands.
- zustand store conventions (if/when client-only state outgrows context).
