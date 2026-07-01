# ConnectOnion Desktop Frontend

This package contains both the React renderer and the Electron desktop shell for the ConnectOnion Windows desktop client.

## Structure

```text
frontend/
  electron/        Electron main process, preload bridge, and window IPC
  src/             React renderer application
  src/features/    Chat, agent, session, and settings modules
  src/shared/      Shared UI, hooks, and utilities
```

## Development

```powershell
npm install
npm run dev
```

Open the renderer at:

```text
http://127.0.0.1:3000
```

Run the Electron shell:

```powershell
npm run desktop
```

## Validation

```powershell
npm run lint
npm run build
```

## Current Baseline

- React + Vite renderer
- Electron desktop shell
- ConnectOnion TypeScript SDK chat integration
- Electron preload IPC boundary
- localStorage-backed agents, sessions, conversations, and settings

Feature repositories for agents, sessions, messages, settings, files, and credentials can be layered on top of this baseline.
