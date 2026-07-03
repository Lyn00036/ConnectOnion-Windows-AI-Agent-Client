# ConnectOnion Desktop

ConnectOnion Desktop is a Windows desktop client for chatting with external
ConnectOnion agents.

Current desktop-client baseline:

- Chat supports ConnectOnion Relay connections and Direct URL connections
- Saved agents, session metadata, preferences, and chat conversation blobs
  persist through renderer `localStorage`
- Shared remote agents can be added by filling in their deployed URL/address
- The Electron shell exposes window controls through a narrow preload IPC bridge

## Current Architecture

```text
User
  -> Electron BrowserWindow
  -> React frontend
      -> ConnectOnion TypeScript SDK
          -> Relay or Direct URL
          -> Remote ConnectOnion agent
          -> Agent LLM/tools
      -> localStorage persistence
      -> Electron preload IPC bridge
          -> Electron window controls
```

The renderer stores agents, sessions, preferences, and conversation envelopes in
`localStorage`. React code should not import Node or Electron packages directly.
Electron IPC is currently limited to desktop window controls.

Current `localStorage` keys:

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

## Project Structure

```text
connectonion-desktop/
  agent/
    email-agent/        Email ConnectOnion agent source
    browser-agent/      Browser automation agent source
    simple-agent/       Minimal connectable assistant agent source
    memory-agent/       Assistant with persistent memory source
  frontend/
    electron/           Electron desktop shell
    src/
      app/              React providers and route table
      layouts/          Desktop shell layout
      pages/            Top-level app views
      features/         Agent, chat, session, and settings modules
      components/ui/    Shared shadcn UI primitives
      shared/           Shared components, hooks, and utilities
      index.css         Tailwind theme and global styles
  .github/workflows/ci.yml
```

## Requirements

- Node.js 22+
- npm

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

Before changing agent connection, hosting, streaming, deployment, credentials,
or persistence behavior, read the official ConnectOnion repositories and docs in
the sibling checkouts:

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
6. Treat renderer localStorage as the current desktop persistence layer.
7. Keep secrets out of renderer state and localStorage.
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
Preserve the desktop architecture: React renderer -> ConnectOnion TS SDK -> remote agent, with renderer localStorage for current persistence.
Call out any mismatch between the docs, installed package version, and this desktop client.
```

## Start The Desktop Client

Electron desktop shell:

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

## Remote Agents

The shared `email-agent` and `browers-agent` are deployed remotely. Developers
do not need to start these agents locally. Add them from the desktop app's
Agents page by filling in the deployed URL and address values below.

| Agent | Direct URL | Address |
| --- | --- | --- |
| email-agent | http://124.156.170.117/email | 0x66f0bb49d47cb1859678f1e39c0d40a3b2e2e96921e7de9ca7dae8e0aacaa2ca |
| browser-agent | http://124.156.170.117/browser | 0xf83dfaec8890059e0bc247e78449000a4d81e808ad1d45042cfcbf28406713cb |

When creating an agent in the desktop app:

- Prefer putting the deployed URL in the Direct URL field.
- The relay Address can be filled in as a fallback, but relay-address
  connections may need the remote agent to be restarted or refreshed every
  24 hours.
- For day-to-day desktop use, Direct URL is recommended because it bypasses the
  relay and connects straight to the remote agent host.

## Add An Agent

On the chat page, add an agent from the sidebar:

```text
Agent name        Display name stored locally
Agent address     0x... address; optional when Direct URL is provided
Direct URL        Optional deployed host URL; can be used without an address
```

When `Direct URL` is set, the desktop client bypasses the relay and connects to
`{Direct URL}/ws`. Without it, the client connects through the relay by agent
address. Relay-address connections may require the remote agent to be restarted
or refreshed every 24 hours, so prefer Direct URL whenever the remote host is
reachable.

The agent host should expose ConnectOnion-compatible endpoints such as:

```text
GET /health
GET /info
WS  /ws
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

## What Is Implemented

- Electron desktop shell
- React, Vite, Tailwind, and shadcn-based desktop UI
- Relay and Direct URL chat through the ConnectOnion TypeScript SDK
- Agent add/select/remove
- Per-agent session creation, selection, deletion, and `/chat/:sessionId` routing
- localStorage-backed agent, session, settings, and conversation persistence
- Functional settings for theme, sidebar visibility, message font size, and shortcut overrides
- Frameless Electron window controls
- Vitest coverage for storage adapters, feature persistence, and connection tests
- Playwright smoke coverage for the chat shell and settings navigation
- Frontend CI lint/test/build/e2e

## Not Implemented Yet

- File attachments and drag-and-drop metadata persistence
- Secure local credential storage
- Packaged installer
- Production release pipeline

## References

- [Requirement-to-solution mapping](./connectonion_requirement_solution_mapping.md)
- [Email agent source](./agent/email-agent/README.md)
- [ConnectOnion Python repository](https://github.com/openonion/connectonion)
- [ConnectOnion TypeScript repository](https://github.com/openonion/connectonion-ts)
- [ConnectOnion Python docs](../connectonion/docs/README.md)
- [ConnectOnion TypeScript docs](../connectonion-ts/docs/getting-started.md)
