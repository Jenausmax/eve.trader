---
description: any new .csproj must be added to <solution>.slnx + wired via ProjectReference
always: true
---

# New project → slnx + ProjectReference checklist

Whenever you create a new `.csproj` (shared lib, module, tests project),
**the working tree is incomplete** until these three steps are done:

1. **Add the new project to `<solution>.slnx`** in the right solution folder
   (e.g. `/src/shared/infra/`, `/src/modules/<Solution>.Modules.X/`,
   `/tests/unit/`). Without this, `dotnet build <solution>.slnx` does
   not see the project — it exists on disk but is invisible to the
   solution. `dotnet build` from the project folder works, which
   is exactly the trap: you think it's fine, CI doesn't see it.

2. **Wire ProjectReferences in every consumer** — the new project
   doesn't get pulled in transitively through magic. Every project
   that needs to `using` the new one needs an explicit
   `<ProjectReference Include="..." />` in its csproj.

3. **No bare `using <Namespace>;`** in a project that doesn't have
   the corresponding `<PackageReference>` / `<ProjectReference>` —
   the build will fail at `using` resolution, not at runtime.

## How to verify

After creating a project, run:

```bash
# Should print the new project name with no errors
dotnet sln <solution>.slnx list

# Should build clean across the whole solution (not just the
# new project in isolation)
dotnet build <solution>.slnx -c Debug
```

If `dotnet sln list` doesn't show your new project, you forgot step 1.
If `dotnet build <solution>.slnx` fails on consumers but `dotnet build
src/<project>/<project>.csproj` succeeds, you forgot step 2.

## Anti-patterns

- ❌ "I'll add to slnx in a follow-up commit" → build broken until then.
  Commit that breaks CI is not a valid commit.
- ❌ "The test project doesn't need to be in slnx" → it does.
  `dotnet test` on a non-registered test project silently no-ops
  (no tests run, exit 0 — false negative on coverage).
- ❌ "I created the .csproj manually without `dotnet new`" →
  fine, but still needs the same three steps. `dotnet new` is
  sugar; the checklist is what matters.
- ❌ "The path is wrong in slnx but builds work in isolation" →
  relative paths in `<Project>` elements are relative to the
  slnx file's directory. `src/shared/foo` is correct, `./src/shared/foo`
  is wrong (path resolution doesn't normalise), `../src/shared/foo`
  is wrong.

## Related

- `.agents/rules/csharp/project-naming-and-setup.md` §"Creating a
  new project — 5 steps" (the original rule this extends)
- `.agents/rules/process/build-verification.md` (the solution-level
  build gate that catches missing registrations)
