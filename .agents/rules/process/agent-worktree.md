---
description: feature work runs in .agents/worktree/<slug> — one worktree per unit; branches use feature|fix|test full prefixes (never feat/); main checkout stays on integration; background agents get a worktree cwd
priority: high
always: true
---

# Agent worktrees

## Default for new feature / fix work

Do **not** implement a multi-file feature on the main checkout's integration
branch. Create an isolated worktree first:

```bash
git worktree add .agents/worktree/<branch-slug> \
  -b feature/<branch-slug> origin/<integration>
```

- Path: `.agents/worktree/<branch-slug>/` (gitignored).
- Prefix: **`feature/`**, **`fix/`**, or **`test/`** — full words. **Never** `feat/`.
- `<branch-slug>`: descriptive kebab-case, no cryptic abbreviations.
- One worktree = one issue/unit = one PR.

Full procedure + remove: skill **`worktree`**
(`~/.agents/skills/worktree/SKILL.md`). Multi-unit dispatch: skill
**`agent-orchestrate`**.

## Main checkout

- Stays on the integration branch (or the session's review branch).
- Status, triage, merges, docs, dispatch — not feature coding via branch switch.

## Background / workers

1. Parent creates the worktree (or worker step 0).
2. Agent **cwd** = worktree path.
3. No main-checkout branch switch; no dev/watch/serve.
4. Parent owns merge + `git worktree remove` after PR lands (or
   `agent-orchestrate` cleanup).

## After squash-merge — cleanup is not optional

A worktree carries a full build tree (~3 GB for a mid-size .NET solution), and
`git worktree remove` silently fails halfway when a compiler holds those files:
registration gone, directory left behind, invisible to `git worktree list`
forever. Always remove via the script, which deletes artifacts first and then
**verifies the directory is gone**:

```bash
python ~/.agents/skills/worktree/scripts/worktree-clean.py remove <branch-slug> --repo . --apply
git branch -D feature/<branch-slug>              # after the PR merged
git push origin --delete feature/<branch-slug>
```

By hand: `git worktree remove` → `git worktree prune` → `ls .agents/worktree/`
to confirm. A directory present there but absent from `git worktree list` is an
orphan — audit it, never delete it blind:

```bash
python ~/.agents/skills/worktree/scripts/worktree-clean.py audit --repo .
```

## Related

- skill `worktree`
- skill `agent-orchestrate`
- `agent-runtime-safety.md`
