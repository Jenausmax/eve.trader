---
description: tanstack query + router — queryclientprovider required, querykeys, api client, code-based routes, link
globs: ["**/*.tsx"]
always: false
---

# TanStack Query & Router

Data fetching is **TanStack Query**; routing is **TanStack Router**. Server state
lives in the Query cache — do not duplicate it into component state or a store.

## 1. QueryClientProvider is mandatory

The app root MUST be wrapped in a `QueryClientProvider`, or every `useQuery`
throws "No QueryClient set" at runtime. Compose it once in the entry
(`main.tsx`), outside the router:

```tsx
// Good — provider present
<StrictMode>
  <PreferencesProvider>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </PreferencesProvider>
</StrictMode>
```

## 2. Fetch through the API client, never `fetch` in a component

All network access goes through the typed client (`@/shared/api/client`), which
is mock-first (falls back to mock data when `VITE_API_BASE_URL` is unset). Query
functions call the client; they never call `fetch` directly.

```tsx
const { data, isLoading, isError, error } = useQuery({
  queryKey: ['items', { ownerId, startUnixMs, endUnixMs }],
  queryFn: () => api.listItems({ ownerId, startUnixMs, endUnixMs, limit: 50 }),
  refetchInterval: 30_000,
});
```

- **queryKey** is a stable array: a string tag + a serializable params object.
  Keep the params in the key so the cache invalidates when they change.
- Always handle `isLoading` / `isError` explicitly (skeleton + error panel).
- Wire mutations through `useMutation` + `queryClient.invalidateQueries`.

## 3. Routing — code-based, `Link` for navigation

Routes are defined in code (`router.tsx`), not file-based (no `routes/` dir, no
`routeTree.gen.ts`). Add a route with `createRoute({ getParentRoute, path, component })`
and register it in `rootRoute.addChildren([...])`.

- Navigate with `<Link to="/items/$itemId" params={{ itemId }}>`; never a raw
  `<a href>` for in-app navigation (breaks client routing + preload).
- Read params with the route's `useParams()` or `getRouteApi('/path').useParams()`.
- Path params use `$name` (e.g. `/items/$itemId`).

## 4. Enforcement

`bun run typecheck` + `bun run lint` exit 0. A page using `useQuery` without a
`QueryClientProvider` ancestor is a runtime bug — verify the provider is mounted.
