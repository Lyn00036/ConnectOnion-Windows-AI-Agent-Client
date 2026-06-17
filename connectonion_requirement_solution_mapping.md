# ConnectOnion Desktop Requirement Mapping

This document describes the current baseline for the ConnectOnion Windows desktop client.

## Architecture Decision

Recommended Architecture: Electron + React + TypeScript + ConnectOnion TypeScript SDK + Electron-hosted SQLite

The desktop app connects directly to external ConnectOnion agents. Agents may run locally, remotely, or on ConnectOnion Cloud. Desktop-local data is stored in SQLite through the Electron main process.

```text
Electron Desktop Client
  -> React UI
  -> preload IPC bridge
  -> Electron main SQLite service
  -> ConnectOnion TypeScript SDK
  -> Agent host / ConnectOnion Cloud
```

## Local Persistence

The client stores desktop state in SQLite. The renderer never imports SQLite directly; it talks to a typed preload API exposed as `window.desktopStorage`.

Storage framework:

```text
frontend/electron/storage/connection.ts
frontend/electron/storage/migrations.ts
frontend/electron/storage/service.ts
frontend/electron/ipc/storageChannels.ts
frontend/electron/ipc/storageIpc.ts
frontend/electron/preload.ts
frontend/src/shared/storage/types.ts
frontend/src/shared/storage/desktopStorage.ts
frontend/src/shared/storage/repository.ts
```

Initial schema tables:

- `schema_migrations`
- `app_metadata`
- `agents`
- `sessions`
- `messages`
- `settings`

Feature repositories are intentionally not implemented yet. Future modules should add typed repositories and IPC handlers instead of exposing raw SQL to React.

For sensitive credentials, use an OS-backed secure store such as Electron safeStorage, keytar, Windows Credential Manager, macOS Keychain, or libsecret. Do not store API keys or OAuth refresh tokens directly in SQLite unless they are encrypted and access controlled.

## Agent Connection

The frontend uses the ConnectOnion TypeScript SDK.

Agent form fields:

```text
Agent name        Local display name
Agent address     0x... address or stable agent name
Direct URL        Optional direct local/deployed agent URL
```

For local development, `Direct URL` can point to the example email agent:

```text
http://127.0.0.1:8010
```

The agent host should expose ConnectOnion-compatible endpoints:

```text
GET /health
GET /info
WS  /ws
```

## Frontend Modules

```text
frontend/src/features/agents
  Local agent registry and selected-agent state

frontend/src/features/chat
  Chat UI and ConnectOnion SDK connection

frontend/src/features/sessions
  Placeholder for future local session history

frontend/src/features/settings
  Placeholder for future desktop preferences
```

## Example Agent

`agent/email-agent` is a separate ConnectOnion agent example for local and deployed testing.

It can be run locally:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

Or deployed separately with ConnectOnion tooling:

```powershell
cd .\agent\email-agent
co deploy
```

After it is running or deployed, add its address or direct URL in the desktop client.

## Implementation Status

Implemented:

- Electron + React desktop shell
- ConnectOnion TypeScript SDK chat path
- SQLite storage lifecycle
- Versioned SQLite migrations
- Typed preload IPC bridge
- Renderer storage adapter
- Example standalone email agent
- Frontend lint/build CI

Not implemented yet:

- SQLite-backed agent repository
- Local chat history persistence
- SQLite-backed session list
- Settings persistence
- Secure credential storage
- Desktop packaging pipeline

## Validation

Frontend validation commands:

```powershell
cd .\frontend
npm run lint
npm run build
```
