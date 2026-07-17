# Documentation

| Document | What it is | Status |
|---|---|---|
| [PROJECT_BRIEF.md](./PROJECT_BRIEF.md) | The original client-supplied requirements (verbatim), plus a requirement → implementation traceability table and the current known limitations | Living — update the table, never the brief |
| [TEST_PLAN.md](./TEST_PLAN.md) | Layered test strategy, ~48 P0 cases, 15 E2E flows, UI-automation standards, and the phased testing backlog | Living |
| [SESSION_MESSAGE_STRUCTURE.md](./SESSION_MESSAGE_STRUCTURE.md) | The ConnectOnion wire protocol: `CONNECT` / `INPUT` / `OUTPUT`, streaming events, `session_id`, reconnect, `last_msg_id` | Authoritative for §1–31; §32–39 are superseded design notes (see its header) |
| [WINUI3_NATIVE_MIGRATION.md](./WINUI3_NATIVE_MIGRATION.md) | How the Electron/React client was replaced by the native WinUI 3 app | **Historical** — the migration is complete; read `CLAUDE.md` for the current architecture |
| [GIT_COMMIT_CONVENTION.md](./GIT_COMMIT_CONVENTION.md) | Conventional Commits types and scopes used in this repo | Living |
| [OPTIMIZATION.md](./OPTIMIZATION.md) | Implemented architecture, performance, reliability, CI, localization, and diagnostics improvements | Living |

## What is *not* here

- **[`../CLAUDE.md`](../CLAUDE.md)** — the authoritative, kept-current description of the architecture
  (solution layout, protocol/WinUI split, persistence model, XAML gotchas). It stays at the repo root
  because tooling looks for it there. **If a document in this folder disagrees with `CLAUDE.md`,
  `CLAUDE.md` is right.**
- **[`../README.md`](../README.md)** — build, run, and getting-started instructions.
- `agent/` and `frontend/` — the vendored ConnectOnion agent examples and the legacy Electron client.
  **Both were deleted in `197a88d`**; the repo is C#-only now. Read them out of git history if you
  need the prior behavior.
