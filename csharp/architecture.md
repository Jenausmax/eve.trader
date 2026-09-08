---
description: architecture discipline — each project DECLARES one style (modular-monolith | layered | vertical-slice), 6 universal laws hold for all, per-style invariants, enforced by NetArchTest. undeclared ⇒ agent STOPS and asks, never freelances structure
globs: ["**/*.csproj", "**/*.cs"]
always: true
---

# Architecture

The agent does **not** invent architecture. Every project **declares one style**;
the style's invariants + the universal laws below are enforced by a NetArchTest
project, not left to review. Global sets the laws and the menu; the project
records the choice and the concrete layer names (in its own layers rule).

## Declare the style — one of three

A project's architecture is exactly one of:

| Style | Partition by |
|-------|-------------|
| **modular-monolith** | bounded context (module) |
| **layered** (n-tier) | horizontal technical layer, app-wide |
| **vertical-slice** | feature / use-case |

The choice lives in the project's layers rule (one line, e.g.
`architecture: modular-monolith`). **You cannot mix styles or freelance a
fourth.** Adding a persistence project, a new top-level folder, or a new layer
is a decision that must fit the declared style.

### Undeclared ⇒ STOP and ask

If no style is declared (greenfield, or a project with no layers rule): **do not
create any structure.** Stop, ask the user which of the three, record the answer
in the project's layers rule, then proceed. Never default silently.

## The 6 laws (hold for every style)

1. **Dependencies point one way — inward, no cycles.**
   `presentation → application → domain`; `infrastructure → application → domain`.
   Domain is innermost; nothing inner depends on anything outer.
2. **Domain/core has zero framework/IO dependencies** — no EF, no ASP.NET, no
   HttpClient, no serialization library. Pure types + business logic only.
3. **Feature/module units don't reference each other** — shared contracts go to
   a shared layer; cross-unit interaction is through abstractions, never a direct
   project reference to a sibling's internals.
4. **Concretes are wired only at the composition root** (host / entry point) —
   features/modules depend on abstractions, never on a concrete provider/backend
   project (see `di-installer.md`).
5. **Entry points are composition-only** — `Program.cs` wires DI + the pipeline;
   no business logic.
6. **The architecture is enforced by a NetArchTest project** — laws 1–4 plus the
   declared style's invariants are asserted in tests. Add a test when a new
   boundary appears; a violated law fails the build, not code review.

## Per-style invariants

### modular-monolith
- A module = one bounded context = one persistence boundary (e.g. one schema/DB).
- Modules communicate **only through a shared contracts/kernel layer** — never a
  direct `ModuleA → ModuleB` reference (Law 3).
- Each module is internally layered (its own domain/application/infrastructure/
  api or equivalent — the project names them).
- Exactly **one** composition host wires all modules.

### layered (n-tier)
- Horizontal layers span the whole app: `presentation → application → domain ←
  infrastructure`. Dependencies strictly downward (Laws 1–2).
- No feature-modularisation required; cross-cutting concerns are a bottom layer
  everything may use.

### vertical-slice
- Organise by feature/use-case; a slice owns its `request → handler → response`
  end-to-end.
- Cross-slice reuse goes through an **explicit shared layer**, never a
  `SliceA → SliceB` reference.
- A slice never reaches into another slice's internals; within a slice,
  dependencies still point inward (Law 1).

## Related

- (project) layers rule — declares the style + concrete layer names/paths
- `di-installer.md` — composition root, concretes wired only in the host
- `repository-spec.md`, `ef-core.md` — persistence placement within the chosen style
- `folder-organization.md` — file/folder micro-structure
- `testing-stack-and-pyramid.md` — the NetArchTest project that enforces this
