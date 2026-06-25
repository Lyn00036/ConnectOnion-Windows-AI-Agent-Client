# ConnectOnion Desktop

ConnectOnion Desktop is a Windows desktop client for chatting with external ConnectOnion agents.

Current desktop-client baseline:

- Chat supports ConnectOnion Relay connections and Direct URL connections
- Saved agents, session metadata, and chat state currently persist in renderer `localStorage`
- Electron initializes a local SQLite database, but feature CRUD is not wired to it yet
- The example email agent runs as a separate ConnectOnion agent host

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
or SQLite packages directly. The current IPC surface exposes storage status and
schema metadata only; agents, sessions, and chat data have not been migrated
from `localStorage` to SQLite.

Storage framework files:

```text
frontend/electron/storage/
  connection.ts       Opens SQLite and applies database pragmas
  migrations.ts       Versioned schema migrations
  service.ts          Desktop storage lifecycle

frontend/electron/ipc/
  storageChannels.ts  IPC channel names
  storageIpc.ts       Main-process IPC handlers

frontend/electron/preload.ts
  Exposes window.desktopStorage through contextBridge

frontend/src/shared/storage/
  types.ts            Renderer-safe storage types
  desktopStorage.ts   Renderer adapter for the preload API
  repository.ts       Repository interface for future feature modules
```

## Project Structure

```text
connectonion-desktop/
  agent/
    email-agent/        Example ConnectOnion agent for local/demo testing
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
6. Treat Electron SQLite as the target desktop source of truth and document any feature still using localStorage.
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
npm run lint      Run ESLint
npm run build     Type-check and build frontend
```

## Desktop Database Location

The Electron desktop app initializes its local SQLite database when the app starts. On Windows, it is created under the Electron user data directory:

```text
%APPDATA%\connectonion_desktop\storage\connectonion-desktop.sqlite3
```

SQLite may also create `connectonion-desktop.sqlite3-wal` and `connectonion-desktop.sqlite3-shm` next to it; those sidecar files are normal.

Current persistence boundaries:

```text
connectonion.agents                         Saved agent configurations
connectonion.selectedAgentId                Selected agent
connectonion.sessions                       Per-agent session metadata
connectonion.activeSessionId                Selected session
co:agent:{address}:session:{sessionId}      Relay/direct chat state
```

These keys currently live in renderer `localStorage`. The SQLite schema already
contains agents, sessions, messages, settings, and metadata tables, but the
feature stores have not been connected to those tables yet.

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

## What Is Implemented

- Electron desktop shell
- React, Vite, Tailwind, and shadcn-based desktop UI
- Relay and Direct URL chat through the ConnectOnion TypeScript SDK
- Local agent add/select/remove
- Per-agent session creation, selection, deletion, and `/chat/:sessionId` routing
- Renderer-side agent, session, and chat persistence through `localStorage`
- Electron SQLite initialization, migrations, and storage-status IPC
- Standalone email agent with a single `agent.py` host/ASGI entrypoint
- Frontend CI lint/build

## Not Implemented Yet

- Agent/session/chat repositories backed by SQLite
- Migration of existing renderer `localStorage` data into SQLite
- Functional desktop Settings workflow
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
