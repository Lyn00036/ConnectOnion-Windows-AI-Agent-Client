# ConnectOnion.WinUIClient

Native Windows desktop client for chatting with [ConnectOnion](https://github.com/openonion/connectonion)-compatible AI agents. Built with **WinUI 3**, **C#**, and **.NET 10**.

The client connects to agents via **Direct URL** or **relay-based address resolution**, communicates over a persistent **WebSocket** using the ConnectOnion protocol, and persists local state in **SQLite**.

## Tech Stack

| Layer | Technology |
| --- | --- |
| UI Framework | WinUI 3 (Windows App SDK) |
| Language | C# 14, .NET 10 |
| UI Pattern | MVVM via CommunityToolkit.Mvvm source generators (`[ObservableProperty]`/`[RelayCommand]`); `Common/ObservableObject` bridges the toolkit base |
| Local Storage | SQLite (`Microsoft.Data.Sqlite`) |
| HTTP | `HttpClient` |
| WebSocket | `ClientWebSocket` (via `ConnectOnion.Protocol`) |
| Serialization | `System.Text.Json` (source-generated) |
| Icons | `FluentIcons.WinUI` |
| Target Platforms | x64, x86, ARM64 |

## Prerequisites

- **Windows 10** version 1809 (build 17763) or later
- **.NET 10 SDK** (pinned via `global.json` in the repository root)
- **Windows App SDK 2.2** runtime (matching the project package reference)
- **Visual Studio 2026** (recommended for WinUI debugging and packaging), with the
  **.NET Desktop Development** workload and the **Windows App SDK C# Templates** component

See [Installing The Requirements](../README.md#installing-the-requirements) in the
root README for the `winget` commands that set all of this up.

## Quick Start

### Visual Studio 2026 (Recommended)

1. Open `ConnectOnion.WinUIClient.sln`
2. Set startup project to `ConnectOnion.WinUIClient`
3. Set configuration to `Debug`, platform to `x64`
4. Ensure **Build** and **Deploy** are enabled in Configuration Manager
5. Press **F5**

### Command Line (Unpackaged)

```powershell
# Restore
dotnet restore ConnectOnion.WinUIClient.sln -p:Platform=x64

# Build
dotnet build ConnectOnion.WinUIClient.sln --configuration Debug --no-restore -p:Platform=x64

# Run (unpackaged mode)
dotnet run --project ConnectOnion.WinUIClient.csproj -p:Platform=x64 -p:RunUnpackaged=true
```

> If the app exits immediately with `REGDB_E_CLASSNOTREG`, the Windows App SDK runtime is missing or mismatched.

## Project Structure

```
ConnectOnion.WinUIClient/
├── App.xaml / App.xaml.cs              Application entry point
├── MainWindow.xaml / MainWindow.xaml.cs Shell window, custom title bar, navigation frame
├── Package.appxmanifest                 MSIX package manifest
│
├── Views/                              XAML pages
│   ├── HomePage                         Agent selection (landing)
│   ├── ChatPage                         Real-time chat with streaming events
│   ├── SessionsPage                     Per-agent session list & management
│   ├── AgentDetailPage                  Agent edit / connection test / info
│   └── SettingsPage                     Theme, sidebar, font size, shortcuts, identity
│
├── ViewModels/                         MVVM state & commands
│   ├── ChatViewModel                    Message list, run subscription, send input
│   ├── SessionsViewModel                Session CRUD, active session tracking
│   ├── AgentDetailViewModel             Agent form state, connection testing
│   ├── KeyboardShortcutsViewModel       Shortcut catalog & user overrides
│   └── SettingsViewModel                Preference read/write
│
├── Models/                             Data types
│   ├── AgentConfig                      Saved agent connection
│   ├── ChatMessage                      Chat bubble (user / agent / event)
│   ├── ChatAttachment                   Attachment metadata on a bubble (never base64)
│   ├── PendingAttachment                Picked / dropped attachment awaiting send
│   ├── ToolActivity                     Per-turn tool execution timeline aggregate
│   ├── SessionSummary                   Indexed conversation metadata
│   ├── PreferencesSnapshot              Theme, font size, sidebar, shortcuts
│   ├── ShellAgentItem / ShellSessionItem Sidebar list items
│   └── Notifications/                   Notification request / decision / settings models
│
├── Data/                               SQLite persistence
│   ├── AppDatabase                      Schema init (WAL, FK, idempotent CREATE IF NOT EXISTS)
│   ├── AppStorage                       Local filesystem directory setup
│   ├── AgentRepository                  Agent CRUD
│   ├── SessionRepository                Session CRUD
│   ├── ConversationRepository           Message / attachment rows, incremental upsert
│   ├── PreferencesRepository            Preferences persistence
│   ├── IdentityStore                    Ed25519 key pair (private seed DPAPI-protected)
│   └── AppJsonContext                   System.Text.Json source-gen context
│
├── Services/                           Business logic
│   ├── AppServices                      Process-wide singleton locator
│   ├── ConversationCache                LRU cache of the last few idle conversations
│   ├── ConnectionTester                 /health probe + relay resolution
│   ├── AgentInfoService                 /info fetch & change detection
│   ├── AgentPresenceService             In-memory online status cache
│   ├── ThemeService                     Light/dark theme apply, raw Color lookup
│   ├── Runtime/                         App-level turn lifecycle (owns sockets and runs)
│   │   ├── AgentSessionManager          Send → run → project → persist, page-independent
│   │   ├── AgentConnectionRegistry      Per-conversation WebSockets, idle eviction
│   │   ├── ChatTurnProjection           The single stream-event → ChatMessage mapping
│   │   └── ToolActivityProjector        tool_call / tool_result → timeline aggregation
│   ├── Attachments/                     Picker, drop, validation, encoding, image cache
│   └── Notifications/                   Windows + in-app toasts, dedup, policy, routing
│
├── Controls/                           Reusable UI components
│   ├── ShellSidebar                     Agent list + session list navigation
│   ├── ChatComposer                     Input, attachment picker / drop target, mic
│   ├── ToolActivityView                 Collapsible tool execution timeline
│   ├── SettingsOverlay                  Modal settings surface (theme, shortcuts, identity)
│   └── AddAgentForm                     Inline agent creation form
│
├── Common/                             Shared utilities
│   ├── ObservableObject                 Bridge over CommunityToolkit.Mvvm's ObservableObject
│   ├── ChatMessageTemplateSelector      One DataTemplate per bubble kind
│   ├── LocalPathToImageSourceConverter  Cached path → size-capped BitmapImage
│   ├── BoolToVisibilityConverter        XAML value converters
│   └── ChatRoleConverters               Role-based alignment & color converters
│
├── Styles/                             Colors.xaml + Brushes.xaml (single color source of truth)
└── Assets/                             App icons & splash images
```

## Architecture

```
User
  └─ WinUI 3 Window (MainWindow)
       ├─ Custom title bar (Mica backdrop, nav buttons, File/Edit/View/Help menus)
       ├─ System tray icon (minimize to tray, restore, exit)
       ├─ ShellSidebar (agent list → session list → content)
       ├─ Find overlay (Ctrl+F, driven against whatever page implements IFindHost)
       ├─ SettingsOverlay (theme, font, shortcuts, local identity)
       └─ Frame navigation
            ├─ HomePage      Select an agent to start
            ├─ ChatPage      WebSocket-powered chat with streaming events
            ├─ SessionsPage  Manage per-agent conversations
            ├─ AgentDetailPage Edit / test / delete agent
            └─ SettingsPage  Theme, sidebar, font, shortcuts, identity
```

**Data flow:** a turn belongs to the app, not to the page. `ChatPage` subscribes to a run;
it does not own the socket, and closing it does not cancel the turn.

```
ChatPage → ChatViewModel ─send─→ AgentSessionManager (app-level)
                                      ├─ AgentConnectionRegistry → ConnectOnion.Protocol (WebSocket)
                                      └─ ConversationRunRegistry  → run lifecycle
                                                 │
                              stream events ─────┤
                                                 ↓
                                        ChatTurnProjection  (one mapping, two drivers)
                                          ↙                  ↘
                       live: ChatViewModel                    headless: persist path
                       → ObservableCollection<ChatMessage>    → ConversationRepository → SQLite
                                                                (incremental upsert per turn)
```

## Local Data

All local data is stored in a single SQLite database:

```
%AppData%\ConnectOnion\connectonion.db
```

Schema tables:

| Table | Purpose |
| --- | --- |
| `agents` | Saved agent connections (name, address, Direct URL, invite code) |
| `sessions` | Per-agent conversation sessions (id, title, timestamps, approval `mode`) |
| `messages` | One row per rendered chat bubble, keyed `(conversation_id, id)` |
| `message_attachments` | Attachment metadata only (kind, name, mime, size, cache path) — never base64 |
| `executions` | One row per turn (prompt, result, status, duration) |
| `trace_events` | Raw stream events of a turn, for diagnostics |
| `preferences` | Theme, sidebar, font size, shortcut overrides |
| `usage_events` | Token-usage ledger (per `llm_result`); no FK, never cascaded on delete |
| `identity_keys` | Local Ed25519 key pair (private seed DPAPI-encrypted at rest) |
| `app_meta` | Key-value application metadata |

Received `agent_image` attachments are cached under `%AppData%\ConnectOnion\cache\images\`,
named by the content's SHA-256 — the database holds the path, never the payload.

## Connecting to an Agent

1. Open the app — the **HomePage** shows saved agents
2. Click **"Add another agent"** to create a new connection
3. Fill in:
   - **Agent name** — local display name
   - **Agent address** — `0x...` relay address for lookup
   - **Direct URL** — (recommended) deployed host URL, e.g. `http://124.156.170.117/email`
   - **Invite code** — optional, for agents requiring onboarding
4. The client probes `{Direct URL}/health` and shows reachability status
5. Select an agent → a new session auto-creates → chat begins

Agent hosts must expose:
- `GET /health`
- `GET /info`
- `WS /ws`

## Features

- **Agent management** — add, edit, delete, connection testing (Direct URL + relay)
- **Real-time chat** — persistent WebSocket, streaming events (thinking, tool calls, results)
- **Page-independent turns** — a turn is owned by `AgentSessionManager`; it keeps running,
  persists, and notifies even with its chat page closed
- **Session management** — per-agent sessions, touch/rename, conversation persistence
- **Event cards** — activity events (thinking, intent, eval) and a collapsible per-turn tool timeline
- **Interactive turns** — `ask_user` / `approval_needed` / `plan_review` as inline chat bubbles;
  the chosen answer is persisted into history
- **Multimodal attachments** — outgoing image/file pick and drag-and-drop with capability
  preflight; incoming `agent_image` decoded and cached to disk
- **Notifications** — turn-completed / approval-required toasts (Windows + in-app), deduped
- **Token-usage ledger** — per-model usage panel in Settings, independent of chat/agent deletion
- **Find in chat** — Ctrl+F overlay with match highlighting
- **Light/dark theme** — persisted preference with shell-aware title bar colors
- **Custom title bar** — Mica backdrop, back/forward navigation, sidebar toggle
- **Responsive sidebar** — width adapts to window size
- **Message copy** — copy any chat bubble content
- **Message font size** — small / medium / large
- **Keyboard shortcuts** — configurable overrides

## Related Projects

| Project | Purpose |
| --- | --- |
| `ConnectOnion.Protocol` | C# protocol library (WebSocket state machine, Ed25519 signing, relay resolution) |
| `ConnectOnion.Protocol.Conformance` | Cross-language canonical-JSON signing verification (C# vs JS) |
| `ConnectOnion.Protocol.LiveTest` | Optional live WebSocket integration test |
| `tests/ConnectOnion.Protocol.Tests` | xunit tests for the wire protocol |
| `tests/ConnectOnion.WinUIClient.UnitTests` | Headless xunit tests for this app's WinUI-free surfaces |
| `tests/ConnectOnion.IntegrationTests` | SQLite schema/repository tests against a real database file |
| `agent/` | Python agent implementations (email-agent, browser-agent) |
| `frontend/` | Legacy Electron + React client (reference only) |

## Repository

Part of the [ConnectOnion Desktop](https://github.com/openonion/connectonion) monorepo. See the [root README](../README.md) for build instructions, CI details, and migration history.
