---
description: multi-unit or parallel work loads agent-orchestrate — single unit stays inline; parent owns topology, gates re-check, merge-ask, cleanup
priority: high
always: true
---

# Agent orchestrate (when)

## Default

| Task shape | Action |
|------------|--------|
| One unit, no parallel, main checkout fine | Work **inline** — do not orchestrate |
| Multi-unit, parallel requested, or clear independent slices | Load skill **`agent-orchestrate`** and run its lifecycle |
| User says «разбей» / «оркестрируй» / «параллельно» / «несколько агентов» | Load **`agent-orchestrate`** |

Full lifecycle (worker models once/session → decompose → topology → brief →
dispatch → wait → re-verify gates → 1 retry → merge ask → cleanup): skill
`~/.agents/skills/agent-orchestrate/SKILL.md`.

## Hard constraints (even without loading the skill)

1. **Before first worker dispatch in a session:** ask which model(s) to use
   for background agents (once/session). Do not silently default after the
   user named a model. Details: skill step **0**.
2. Workers never merge to the integration branch.
3. Parent re-runs repo gates — never trust worker self-report alone.
4. Isolation for independent write work uses skill **worktree**
   (`feature|fix|test/<full-slug>`, never `feat/`).
5. No long-lived dev/watch/serve (`agent-runtime-safety.md`).
6. Orca is not used for this workflow.

## Related

- skill `agent-orchestrate`
- skill `worktree` + rule `agent-worktree.md`
