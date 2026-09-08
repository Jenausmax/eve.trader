---
description: engineering-zone edit permission depends on whether the owner is in the loop — interactive sessions with the owner present may edit with in-session authorization; batch/unattended runs need separately-obtained written permission per change. the frozen-zone file list is declared per project
priority: high
always: true
---

# Engineering-zone access — two tiers

The engineering zone (the frozen core — defined per project, see below) is
**not** uniformly off-limits. Access depends on **whether the owner is in the
loop turn-by-turn**, not on which agent tool you are.

## The two tiers

| Tier | When | Owner in loop? | Edit the zone? |
|---|---|---|---|
| **A — interactive** | a live session where the owner authorises turn-by-turn (verbally, via a picker answer, or a phase-scoped override) | ✅ yes | ✅ **yes, when the owner authorises in-session** |
| **B — batch / unattended** | CI runners, scheduled/cron runs, detached subagents, or any run with no live owner | ❌ no | ❌ **no** — requires **separately-obtained written permission** (a prior chat record, an ADR, a CODEOWNERS note) per change |

The tier is set by owner-presence, not the harness brand — the same tool is
Tier A with the owner present and Tier B when it runs unattended. Session
presence alone is not authorisation; the owner must actually approve.

## What "engineering zone" means (declared per project)

The exact frozen list is **declared per project** (e.g. in a project
zone-boundaries rule). A typical default core:

- the shared kernel / contracts **internals** (constant/port classes may be
  editable per their own sub-rules)
- host **composition roots** (`Program.cs`)
- build tooling — `Directory.Build.props` / `.targets` / `Directory.Packages.props`, the build-tools project
- `.editorconfig` (production severities — see `analyzers.md` owner-approval)
- the solution file (`<solution>.slnx`), CI config, `CODEOWNERS`
- the rules themselves (`.agents/rules/**`, `CLAUDE.md`)

If a project hasn't declared its zone, treat this default list as the zone and
confirm scope with the owner.

## Authorisation model

### Tier A — owner present

Authorisation is **in-session** and may be:

1. **Explicit per-change** — owner says "yes, change X". Covers that change only.
2. **Phase-scoped override** — owner authorises a whole phase's zone touches up
   front (e.g. "this phase: `Program.cs` of 3 hosts + a new shared project").
   Logged in the phase's context/state record.
3. **Standing** — none. There is no blanket "interactive can always edit the
   zone". Each non-trivial change wants a sentence of owner intent on the record.

When authorised, the agent **edits directly** — it does not produce a patch to
hand back, does not re-ask if authorisation was already given this session, and
does not treat the zone as forbidden by reflex. Owner present + "do it" **is**
the permission.

After editing, the safety gates still apply: the build gate
(`build-verification.md`), the self-audit gate (`worker-audit.md`), and
`.editorconfig` owner-approval (`analyzers.md`).

### Tier B — unattended

The owner isn't in the loop. Zone edits require **separately-obtained written
permission**: a prior chat record, an ADR, a CODEOWNERS note, or a documented
phase override from **outside** the current run. "I think the owner would want
this" is not permission. On hitting a zone file without prior written
authorisation, **stop and report**.

## Why two tiers

The zone is frozen for **unattended** runs because they can't ask and a wrong
edit propagates before anyone sees it. An **interactive** session has the owner
on the other side of every turn — they catch a bad edit immediately, so allowing
direct edits is cheap and forcing a stop-and-ask on every zone touch (common:
composition root, build tools, kernel) is expensive. The build gate + arch tests
+ self-audit remain the net regardless of tier: a bad zone edit fails the build
before it ships.

## Good / Bad

```
# ✅ Tier A (owner present)
owner: "fix the TEMP DIAGNOSTICS block in the build targets"
agent: edits src/build/App.Build.Tools/App.Build.Tools.targets directly,
       runs the build gate, commits.

# ✅ Tier A — phase override already on the record
agent: rewrites src/host/App.Host/Program.cs per the plan; does not re-ask —
       the phase override in the context record covers it.

# ❌ Tier A — no authorisation yet
agent: about to edit src/shared/App.Shared.Kernel/** on its own initiative
       → STOP, surface the proposed change, get a "yes" first.

# ❌ Tier B (unattended)
agent: edits src/engine/** because "it looks like the fix"
       → violation; stop and report for separately-obtained written permission.

# ❌ Tier A — treating the zone as reflexively forbidden after authorisation
owner: "just edit Program.cs here, no subagents"
agent: "that's the engineering zone, STOP and ask"
       → wrong; the owner already authorised in-session, edit directly.
```

## Related

- (project) zone-boundaries rule — the concrete frozen-file list for the repo
- `build-verification.md` — the single build gate (both tiers)
- `worker-audit.md` — self-audit gate
- `analyzers.md` — `.editorconfig` owner-approval (orthogonal to tier)
