---
description: agent never runs long-lived dev/watch processes — they risk killing the harness
priority: high
always: true
---

# Agent runtime safety — never run dev / watch / serve processes

## The trap

Long-lived dev / watch / serve processes (Vite dev server, Vitest watch, dotnet watch, tsc --watch, file watchers, port-binding servers) **must not be started by the agent**.

When the agent later tries to "clean up" the spawned process — `taskkill`, `kill -9`, `pkill node`, `pkill bun`, `Stop-Process` — it can kill **the agent's own runtime**:

| Harness | Runtime | What `pkill node` does |
|---|---|---|
| **харнесс на Node** | Node.js | **Убивает сам харнесс** |
| Cursor / Claude Code | Node.js | **Kills the agent runtime** |
| VS Code + Copilot | Node.js | **Kills VS Code extension host** |

Confirmed incident (2026-07-07, web init): `taskkill //F //IM node.exe` killed the сам харнесс агента mid-session. User lost context, had to restart.

## What is forbidden

The agent **must not** run any command that:

- Starts an HTTP server, dev server, file watcher, or background daemon
- Binds to a port (`vite`, `npm run dev`, `dotnet run`, `webpack --watch`, `tsc --watch`, `vitest --watch`, `bun --hot`)
- Loops on a timer (`while true; ...`)
- Spawns a subprocess that doesn't exit on its own

Examples that violate this rule:

```bash
# ❌ Forbidden — leaves vite dev server running
bun run dev
npx vite
npm start
dotnet run --project src/host/<Solution>.Host
dotnet watch
tsc --watch --noEmit
vitest --watch
bun --hot

# ❌ Forbidden — agent must NOT run browser automation, even headless
# Risk: downloads 100MB Chromium, leaves processes running, can kill the
# agent runtime. User cannot have a clean browser profile.
playwright ...
chromium ...
google-chrome ...
puppeteer ...
```

Examples that violate this rule specifically with browser tools:

```javascript
// ❌ FORBIDDEN — don't import, install, or run playwright / puppeteer / chrome
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage();
// ... any of this
await browser.close();
```

## **Browser automation tools: hard ban**

The agent **must NEVER** install, import, launch, or interact with browser automation tools:

- `playwright` / `@playwright/test`
- `puppeteer` / `puppeteer-core`
- `chromium` / `chrome-headless-shell` / `google-chrome`
- Any tool that spawns a browser binary, downloads Chromium,
  captures screenshots, or executes page.evaluate

**For visual debugging**, the agent must use **static analysis only**:

- `cat dist/index.html` — read SSR HTML
- `grep dist/assets/*.css | grep selector` — what CSS is compiled
- `curl -sf https://example.com` (if server is already running by user/CI) — HTTP only
- Read source files (`*.tsx`, `*.css`) — verify intent vs what's compiled
- `bunx tsc --noEmit` — type errors
- `bun run build` — compile errors

**NEVER** to debug:

- Run a headless browser
- Take a screenshot (even if you don't show it to user)
- Use `page.evaluate()`, `page.locator()`, `page.goto()`, etc.
- Download browser binaries

If the agent cannot figure out a visual issue from static analysis (CSS file, HTML file, source code) → **ask the user** to describe what they see, what the DOM looks like in DevTools, or paste the relevant computed styles. Do not fire up a browser.

## What is allowed

Short-lived, self-terminating commands that exit on their own:

```bash
# ✅ Build (exits when done)
bun run build
dotnet build <solution>.slnx -c Debug
tsc --noEmit

# ✅ Tests in single-run mode (exit when tests finish)
bun test --run
vitest run
dotnet test --no-build

# ✅ Linters / formatters (exit immediately)
eslint .
dotnet format <solution>.slnx --verify-no-changes

# ✅ Background commands for diagnostics ONLY if they self-terminate
# and the agent never tries to kill them
curl -s http://localhost:8080/health   # assumes server already running
```

## Verification — non-blocking only

When the agent needs to verify a dev server works, it must:

1. **Never start it itself.** Ask the user to run `bun run dev` in their own terminal.
2. **Read configuration files** (`vite.config.ts`, `appsettings.json`, `Program.cs`) to verify the config is correct statically.
3. **Build for production** (`bun run build`, `dotnet build`) to catch type errors and config issues without running.
4. **Run unit tests** (`vitest run`, `dotnet test`) which exit on completion.

If a build fails because of a runtime-only issue (e.g. dev server can't start due to port conflict), the agent should NOT debug by starting the dev server itself — instead ask the user to reproduce and share logs.

## Health checks — exceptions

The agent may use **short-lived** health probes when a server is **already running** (e.g. started by user, CI, or previous session):

```bash
# ✅ Read-only probe, exits in <1s
curl -sf http://localhost:5173/ || echo "not running"
curl -sf http://localhost:5000/health
```

This is a probe, not a server. The server was started by someone else. The agent never starts it.

## Process termination — never blanket-kill

The agent must NEVER run:

```bash
taskkill //F //IM node.exe        # убивает сам харнесс (рантайм Node)
taskkill //F //IM bun.exe         # may kill bun-spawned agent helpers
pkill node                       # same
pkill -9 node                    # same
kill -9 $(pgrep node)            # same
```

**Always terminate by exact PID** if a self-spawned process must be killed:

```bash
# ✅ Kill only the specific PID we tracked
kill $PID
# ✅ Or use a named background job and kill it:
kill %1   # only kills the most recent background job in this shell
```

## Rule of thumb

> **If the command doesn't exit on its own within seconds, the agent doesn't run it.**
> Build, lint, test — yes. Dev server, watch, serve — never.

## Exceptions (none today)

No exceptions to this rule for the current project. If a future need arises (e.g. running an agent harness for end-to-end tests), document it in this file with explicit guard rails.

## Related rules

- `process/build-verification.md` — build gate is `dotnet build` (exits), never `dotnet run`
- `process/worker-audit.md` — self-audit gate runs `dotnet build` + tests (both exit)
- `csharp/anti-patterns.md` §"background-services" — Worker agents must NOT spawn background services

## Incident log

- **2026-07-07** — During фронтовой scaffold, agent ran `bun run dev &` to verify Vite started, then ran `taskkill //F //IM node.exe` to clean up. Killed сам харнесс агента. User lost session context. **RULE ADDED.**
- **2026-07-08** — Agent repeatedly used playwright (Chromium headless browser) to inspect rendered CSS / layout. Rule from 2026-07-07 only covered `bun run dev` / `vite` directly — didn't cover browser automation. User explicitly banned screenshots ("запрещаю делать скрины") and demanded playwright added to forbidden list. **RULE UPDATED:** browser automation tools added to forbidden list (see "Browser automation tools: hard ban" below). User installed OS-level rule blocking agent from running playwright.