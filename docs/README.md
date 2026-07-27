# Documentation

| Document | What it is | Status |
|---|---|---|
| [PROJECT_BRIEF.md](./PROJECT_BRIEF.md) | The original client-supplied requirements (verbatim), plus a requirement → implementation traceability table and the current known limitations | Living — update the table, never the brief |
| [TEST_PLAN.md](./TEST_PLAN.md) | Layered test strategy, ~48 P0 cases, 15 E2E flows, UI-automation standards, and the phased testing backlog | Living |
| [SESSION_MESSAGE_STRUCTURE.md](./SESSION_MESSAGE_STRUCTURE.md) | The ConnectOnion wire protocol: `CONNECT` / `INPUT` / `OUTPUT`, streaming events, `session_id`, reconnect, `last_msg_id` | Authoritative for §1–31; §32–39 are superseded design notes (see its header) |
| [WINUI3_NATIVE_MIGRATION.md](./WINUI3_NATIVE_MIGRATION.md) | How the Electron/React client was replaced by the native WinUI 3 app | **Historical** — the migration is complete; read `CLAUDE.md` for the current architecture |
| [GIT_COMMIT_CONVENTION.md](./GIT_COMMIT_CONVENTION.md) | Conventional Commits types and scopes used in this repo | Living |
| [OPTIMIZATION.md](./OPTIMIZATION.md) | Implemented architecture, performance, reliability, CI, localization, and diagnostics improvements | Living |
| [PERFORMANCE.md](./PERFORMANCE.md) | Launch-time and memory benchmark: method, budgets, current baseline, and how to run `scripts/Measure-Performance.ps1` | Living — re-ratify budgets when a run sets a new baseline |
| [RELEASE.md](./RELEASE.md) | How a `v*` tag becomes a signed MSIX release: package identity (and why it can never change), versioning, signing secrets and certificate rotation, the size budget, and the validation matrix | Living |
| [TRIMMING.md](./TRIMMING.md) | Why Release publishes untrimmed, the acceptance gate for changing that, the current warning inventory, and how to reproduce the evidence with `scripts/Invoke-TrimAudit.ps1` | Living — the gate, not a recommendation |
| [MEMORY_LEAK_INVESTIGATION.md](./MEMORY_LEAK_INVESTIGATION.md) | Conversation-navigation leak investigation, root causes, measured before/after data, and the automated plateau test | Living — update when a new leak signature or threshold is established |
| [CONCURRENCY.md](./CONCURRENCY.md) | Where threads come from, which primitive guards which shared state, and the sharp edges (including the post-`Closed` dispatcher race) | Living |

## What is *not* here

- **[`../CLAUDE.md`](../CLAUDE.md)** — the authoritative, kept-current description of the architecture
  (solution layout, protocol/WinUI split, persistence model, XAML gotchas). It stays at the repo root
  because tooling looks for it there. **If a document in this folder disagrees with `CLAUDE.md`,
  `CLAUDE.md` is right.**
- **[`../AGENTS.md`](../AGENTS.md)** — the same document, for Codex. It is kept byte-identical to
  `CLAUDE.md` apart from the title and the opening line. **Edit both or neither**; two architecture
  documents that disagree is worse than one that is slightly out of date.
- **`ConnectOnion.WinUIClient/Shell/README.md`** and **`ConnectOnion.WinUIClient/Controls/README.md`**
  — folder-local conventions, including why those folders deliberately do not match their namespaces.
- **[`../README.md`](../README.md)** — build, run, and getting-started instructions.
- `agent/` and `frontend/` — the vendored ConnectOnion agent examples and the legacy Electron client.
  **Both were deleted in `197a88d`**; the repo is C#-only now. Read them out of git history if you
  need the prior behavior.
