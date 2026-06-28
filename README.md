# ConnectOnion Desktop

ConnectOnion Desktop is a Windows desktop client for chatting with external ConnectOnion agents.

Current desktop-client baseline:

- Chat supports ConnectOnion Relay connections and Direct URL connections
- In Electron, saved agents, session metadata, preferences, and chat conversation blobs persist through the local SQLite bridge
- Browser-only development still falls back to renderer `localStorage`
- Legacy `localStorage` agent/session/preference/conversation data is migrated once when the Electron bridge is available
- Example ConnectOnion agents run as separate local/demo agent hosts

## Current Architecture

```text
User
  -> Electron BrowserWindow
  -> React frontend
      -> ConnectOnion TypeScript SDK
          -> Relay or Direct URL
          -> Remote or local ConnectOnion agent
          -> Agent LLM/tools
      -> Electron preload IPC bridge
          -> Electron main SQLite storage service
```

SQLite lives behind Electron IPC. React code should not import Node, Electron,
or SQLite packages directly. The current IPC surface exposes storage status,
agent CRUD, session CRUD, settings key/value persistence, and window controls.
The renderer prefers those Electron APIs and falls back to `localStorage` for
browser-only development or IPC/runtime failures.

Storage framework files:

```text
frontend/electron/storage/
  connection.ts       Opens SQLite and applies database pragmas
  migrations.ts       Versioned schema migrations
  service.ts          Desktop storage lifecycle
  agentRepository.ts  Agent CRUD and selected-agent metadata
  sessionRepository.ts Session CRUD and selected-session metadata
  settingsRepository.ts Settings key/value persistence

frontend/electron/ipc/
  agentChannels.ts    Agent IPC channel names
  agentIpc.ts         Main-process agent IPC handlers
  sessionChannels.ts  Session IPC channel names
  sessionIpc.ts       Main-process session IPC handlers
  settingsChannels.ts Settings IPC channel names
  settingsIpc.ts      Main-process settings IPC handlers
  storageChannels.ts  IPC channel names
  storageIpc.ts       Main-process IPC handlers

frontend/electron/preload.ts
  Exposes window.desktopStorage, desktopAgents, desktopSessions, desktopSettings, and desktopWindow

frontend/src/shared/storage/
  types.ts            Renderer-safe storage types
  desktopStorage.ts   Renderer adapter for the preload API
  repository.ts       Repository interface for future feature modules
  desktopFallback.ts  SQLite-first/localStorage-fallback helper
  legacyLocalStorage.ts One-time legacy localStorage cleanup
```

## Project Structure

```text
connectonion-desktop/
  agent/
    email-agent/        Example email ConnectOnion agent (local/demo)
    browser-agent/      Example browser-automation agent (local/demo)
    simple-agent/       Minimal connectable assistant agent (local/demo)
    memory-agent/       Assistant with persistent memory (local/demo)
  frontend/
    electron/           Electron desktop shell
    src/
      app/              React providers and route table
      layouts/          Desktop shell layout
      pages/            Top-level app views
      features/         Agent, chat, session, and settings modules
      components/ui/    Shared shadcn UI primitives
      shared/           Shared components, hooks, storage, and utilities
      index.css         Tailwind theme and global styles
  .github/workflows/ci.yml
```

## Requirements

- Node.js 22+
- npm
- Python 3.12+ only if you want to run the example email agent

## Before Each Development Session

Start from a clean working tree, then update the branch and install the current
frontend dependencies before making changes:

```powershell
git status
git pull
cd .\frontend
npm install
```

If `git status` shows unfinished work, commit or stash it before `git pull`.
Run `npm install` again whenever `frontend/package.json` or
`frontend/package-lock.json` changes. If PowerShell blocks `npm.ps1`, use
`npm.cmd` instead of `npm`.

## Read The ConnectOnion Docs First

Before changing agent connection, hosting, streaming, deployment, credentials, or local persistence behavior, read the official ConnectOnion repositories and docs in the sibling checkouts:

Official repositories:

```text
https://github.com/openonion/connectonion
https://github.com/openonion/connectonion-ts
```

Local sibling checkouts:

```text
../connectonion/
../connectonion-ts/
```

Use this checklist before implementation:

```text
1. Read the Python framework docs for agent hosting and deployment.
2. Read the TypeScript SDK docs for connect(), React hooks, UI events, and session behavior.
3. Confirm the exact SDK API from the local source before importing or calling it.
4. Confirm whether the desktop client should connect by address, relay URL, or direct URL.
5. Confirm which events the agent/SDK can emit before designing chat UI states.
6. Treat Electron SQLite as the desktop source of truth and document any browser-only `localStorage` fallback.
7. Keep secrets in a secure credential layer, not plain SQLite or renderer state.
```

High-priority Python framework docs:

```text
../connectonion/docs/README.md
../connectonion/docs/quickstart.md
../connectonion/docs/api.md
../connectonion/docs/network/host.md
../connectonion/docs/network/deploy.md
../connectonion/docs/network/connect.md
../connectonion/docs/network/protocol.md
../connectonion/docs/network/websocket-protocol.md
../connectonion/docs/network/client-reconnection.md
../connectonion/docs/cli/README.md
../connectonion/docs/cli/auth.md
```

High-priority TypeScript SDK docs:

```text
../connectonion-ts/docs/getting-started.md
../connectonion-ts/docs/connect.md
../connectonion-ts/docs/react.md
../connectonion-ts/docs/api.md
../connectonion-ts/docs/tools.md
../connectonion-ts/docs/examples.md
../connectonion-ts/docs/troubleshooting.md
```

Suggested prompt for future development work:

```text
Before implementing this change, inspect the local connectonion and connectonion-ts repositories.
Use their docs and source as the source of truth.
Do not invent ConnectOnion APIs.
Preserve the desktop architecture: React renderer -> preload IPC -> Electron main -> SQLite/secure storage, and React renderer -> ConnectOnion TS SDK -> remote/local agent.
Call out any mismatch between the docs, installed package version, and this desktop client.
```

## Useful ConnectOnion CLI Commands

Run Python `co` commands inside the relevant agent project when the generated `.env` or `.co/` files should belong to that agent. For this repository, run email-agent credentials and deploy commands from:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
```

Common commands:

```text
co auth              Authenticate with OpenOnion and refresh OPENONION_API_KEY.
co auth google       Connect Google OAuth for Gmail/Calendar tools.
co auth microsoft    Connect Microsoft OAuth for Outlook/Calendar tools.
co status            Show account, balance, and deployed agents.
co doctor            Diagnose installation, auth, environment, and connectivity.
co create <name>     Create a new ConnectOnion agent project.
co init              Add or refresh ConnectOnion project files in an existing directory.
co deploy            Deploy the current agent to ConnectOnion Cloud.
co copy --list       List built-in tools, plugins, prompts, skills, and templates.
co copy <name>       Copy a built-in item into the current project for customization.
co skills discover   Discover available skills from known local skill directories.
co skills copy <n>   Copy a skill into the ConnectOnion skill library.
co setup             Initialize global identity and skill library under ~/.co.
co sub               Sync subscribed published agent skills.
co browser "<task>"  Run a browser automation task.
```

Important notes:

- `co auth` writes managed-key credentials such as `OPENONION_API_KEY`.
- `co auth google` and `co auth microsoft` write OAuth credentials for tool integrations.
- `co deploy` expects a valid `.co/host.yaml`, an authenticated account, and a hostable agent entrypoint.
- In a git repository, `co deploy` packages tracked project files and skips local-only secrets such as `.env` and `.co/keys`.
- `co doctor` is the first command to run when auth, deployment, or environment setup looks wrong.
- Do not commit `.env`, `.co/keys`, OAuth tokens, API keys, or recovery phrases.
- Treat `co reset` as destructive; do not run it unless you intentionally want to replace the local account identity.

## Start The Desktop Client

Electron desktop shell (recommended for normal development):

```powershell
cd .\frontend
npm run desktop
```

Browser-only Vite development:

```powershell
cd .\frontend
npm run dev
```

Open the browser version at:

```text
http://127.0.0.1:3000
```

Useful frontend scripts:

```text
npm run dev       Start Vite dev server on 127.0.0.1:3000
npm run electron  Start Electron shell
npm run desktop   Start Vite and Electron together
npm run preview   Serve the production build on 127.0.0.1:4173
npm run lint      Run ESLint
npm run test      Run Vitest unit/integration tests
npm run e2e       Run Playwright end-to-end smoke tests
npm run build     Type-check and build frontend
```

## Run Frontend Checks

The frontend test stack uses Vitest with jsdom and Testing Library for storage
and UI behavior, plus Playwright for browser smoke coverage.

Run the normal local checks from `frontend/`:

```powershell
cd .\frontend
npm run lint
npm run test
npm run build
```

Playwright tests do not start a server by themselves. Start either the Vite dev
server or the production preview server first, then run e2e in another terminal:

```powershell
cd .\frontend
npm run dev

# second terminal
cd .\frontend
npm run e2e
```

To match CI more closely, build and preview first:

```powershell
cd .\frontend
npm run build
npm run preview

# second terminal
cd .\frontend
$env:PLAYWRIGHT_BASE_URL = "http://127.0.0.1:4173"
npm run e2e
```

If Playwright's browser binaries are missing on a fresh machine, install the
Chromium browser once:

```powershell
cd .\frontend
npx playwright install chromium
```

## Desktop Database Location

The Electron desktop app initializes its local SQLite database when the app starts. On Windows, it is created under the Electron user data directory:

```text
%APPDATA%\connectonion_desktop\storage\connectonion-desktop.sqlite3
```

SQLite may also create `connectonion-desktop.sqlite3-wal` and `connectonion-desktop.sqlite3-shm` next to it; those sidecar files are normal.

Current Electron persistence boundaries:

```text
agents table                                Saved agent configurations
app_metadata.agents.selectedId              Selected agent
sessions table                              Per-agent session metadata
app_metadata.sessions.selectedId            Selected session
settings table                              Preferences and conversation blobs
co:agent:{address}:session:{sessionId}      Conversation blob key in settings
```

In browser-only Vite development, and as a runtime fallback if the Electron
bridge is unavailable, the renderer still uses legacy `localStorage` keys:

```text
connectonion.agents
connectonion.selectedAgentId
connectonion.sessions
connectonion.activeSessionId
connectonion.theme
connectonion.sidebarVisible
connectonion.messageFontSize
connectonion.shortcuts
co:agent:{address}:session:{sessionId}
```

When running inside Electron, the app migrates those legacy values into SQLite
once and then clears the migrated legacy keys after all migration markers are
present. The schema also includes a `messages` table, but the current chat path
stores conversation envelopes in `settings`; there is not yet a per-message
repository writing individual rows to `messages`.

## Add An Agent

On the chat page, add an agent from the sidebar:

```text
Agent name        Display name stored locally
Agent address     0x... address; optional when Direct URL is provided
Direct URL        Optional local/deployed host URL; can be used without an address
```

When `Direct URL` is set, the desktop client bypasses the relay and connects to
`{Direct URL}/ws`. Without it, the client connects through the relay by agent address.

For a local agent, `Direct URL` can be:

```text
http://127.0.0.1:8010
```

The agent host should expose ConnectOnion-compatible endpoints such as:

```text
GET /health
GET /info
WS  /ws
```

## Example Email Agent

The repository keeps `agent/email-agent` as a separate ConnectOnion agent example for local and deployed testing.

Install it from the repository root:

```powershell
cd .\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -r .\requirements.txt
cd .\email-agent
```

Then choose one startup method:

```powershell
# Full ConnectOnion host; reads .env and .co/host.yaml (currently port 8000)
python -X utf8 agent.py

# Or run the ASGI app directly for local Direct URL testing
python -m uvicorn agent:app --host 127.0.0.1 --port 8010
```

On Windows, start this example agent's `agent.py` with `-X utf8`. It forces
UTF-8 mode and avoids failures caused by the system GBK encoding when prompts,
logs, or model output contain non-ASCII characters.

Use `http://127.0.0.1:8000` as the Direct URL for `python -X utf8 agent.py`, or
`http://127.0.0.1:8010` for the Uvicorn command.

Relay note: `python -X utf8 agent.py` enables the `relay_url` configured in
`.co/host.yaml`. On Windows machines with many physical, Docker, WSL, VPN, or
link-local network adapters, ConnectOnion may announce more than the Relay limit
of 10 endpoints and repeatedly report `Too many endpoints (max 10)`. This is an
endpoint-discovery limit, not an invalid Relay URL. The local HTTP and WebSocket
host can still be used through Direct URL. For local-only Direct URL testing,
prefer the Uvicorn command; see the email-agent README for Relay-specific fixes.

If you use ConnectOnion OAuth helpers, run them from `agent/email-agent` so credentials are written to that service's `.env`:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
co auth
co auth google
```

Do not commit `.env`, `.co/`, API keys, or local credential files.

## Other Local Agents

Besides the email agent, the repository ships additional connectable example
agents adapted from the official ConnectOnion examples. They expose
ConnectOnion-compatible `/health`, `/info`, `/input`, and `/ws` endpoints, so
the desktop client connects to them through Direct URL the same way.

```text
agent/simple-agent/    Minimal general assistant (lowest setup; good for multi-agent tests)
agent/memory-agent/    Assistant with persistent local memory across turns
agent/browser-agent/   Browser automation assistant; requires browser tooling setup
```

Run one of them locally with Uvicorn, then add its Direct URL in the desktop
client:

```powershell
cd .\agent\simple-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn agent:app --host 127.0.0.1 --port 8020   # simple-agent

# or
cd .\agent\memory-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn agent:app --host 127.0.0.1 --port 8021   # memory-agent

# or
cd .\agent\browser-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn agent:app --host 127.0.0.1 --port 8022   # browser-agent
```

To run several agents at once, give each a different port (for example
email 8010, simple 8020, memory 8021, browser 8022) and add each as a separate
agent. See each agent's own README for details.

## What Is Implemented

- Electron desktop shell
- React, Vite, Tailwind, and shadcn-based desktop UI
- Relay and Direct URL chat through the ConnectOnion TypeScript SDK
- Local agent add/select/remove
- Per-agent session creation, selection, deletion, and `/chat/:sessionId` routing
- SQLite-backed agent, session, settings, and conversation persistence in Electron
- Browser/local failure fallback persistence through `localStorage`
- One-time migration and cleanup for legacy `localStorage` data
- Electron SQLite initialization, migrations, storage-status IPC, and typed preload APIs
- Functional settings for theme, sidebar visibility, message font size, and shortcut overrides
- Frameless Electron window controls
- Vitest coverage for storage adapters, feature persistence, and connection tests
- Playwright smoke coverage for the chat shell and settings navigation
- Standalone email agent with a single `agent.py` host/ASGI entrypoint
- Additional simple, memory, and browser local demo agents
- Frontend CI lint/test/build/e2e

## Not Implemented Yet

- Per-message repository backed by the SQLite `messages` table
- File attachments and drag-and-drop metadata persistence
- Secure local credential storage
- Packaged installer
- Production release pipeline

## References

- [Requirement-to-solution mapping](./connectonion_requirement_solution_mapping.md)
- [Email agent example](./agent/email-agent/README.md)
- [ConnectOnion Python repository](https://github.com/openonion/connectonion)
- [ConnectOnion TypeScript repository](https://github.com/openonion/connectonion-ts)
- [ConnectOnion Python docs](../connectonion/docs/README.md)
- [ConnectOnion TypeScript docs](../connectonion-ts/docs/getting-started.md)
