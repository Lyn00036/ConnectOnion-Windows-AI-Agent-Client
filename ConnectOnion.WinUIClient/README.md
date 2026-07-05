# ConnectOnion.WinUIClient

Native Windows desktop client for chatting with [ConnectOnion](https://github.com/openonion/connectonion)-compatible AI agents. Built with **WinUI 3**, **C#**, and **.NET 10**.

The client connects to agents via **Direct URL** or **relay-based address resolution**, communicates over a persistent **WebSocket** using the ConnectOnion protocol, and persists local state in **SQLite**.

## Tech Stack

| Layer | Technology |
| --- | --- |
| UI Framework | WinUI 3 (Windows App SDK) |
| Language | C# 13, .NET 10 |
| UI Pattern | MVVM (no framework — lightweight `ObservableObject` + `RelayCommand` base classes) |
| Local Storage | SQLite (`Microsoft.Data.Sqlite`) |
| HTTP | `HttpClient` |
| WebSocket | `ClientWebSocket` (via `ConnectOnion.Protocol`) |
| Serialization | `System.Text.Json` (source-generated) |
| Icons | `FluentIcons.WinUI` |
| Target Platforms | x64, x86, ARM64 |

## Prerequisites

- **Windows 10** version 1809 (build 17763) or later
- **.NET 10 SDK** (pinned via `global.json` in the repository root)
- **Windows App SDK** runtime (matching the project package reference)
- **Visual Studio 2022** (recommended for WinUI debugging and packaging)

## Quick Start

### Visual Studio (Recommended)

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
│   ├── IdentityPage                     Local Ed25519 identity display
│   └── SettingsPage                     Theme, sidebar, font size, shortcuts
│
├── ViewModels/                         MVVM state & commands
│   ├── ChatViewModel                    Message list, send input, event rendering
│   ├── SessionsViewModel                Session CRUD, active session tracking
│   ├── AgentDetailViewModel             Agent form state, connection testing
│   └── SettingsViewModel                Preference read/write
│
├── Models/                             Data types (mirror TypeScript types)
│   ├── AgentConfig                      Saved agent connection
│   ├── ChatMessage                      Chat bubble (user / agent / event)
│   ├── SessionSummary                   Indexed conversation metadata
│   ├── PreferencesSnapshot              Theme, font size, sidebar, shortcuts
│   ├── ChatEnvelope                     Serialized conversation payload
│   ├── AgentHealthResponse              /health endpoint shape
│   ├── OnboardSubmission                Onboarding invite code payload
│   ├── ShellAgentItem / ShellSessionItem Sidebar list items
│   └── ...
│
├── Services/                           Business logic & persistence
│   ├── AppServices                      Process-wide singleton locator
│   ├── AppDatabase                      SQLite schema init (WAL, FK, 7 tables)
│   ├── AppStorage                       Local filesystem directory setup
│   ├── AgentRepository                  Agent CRUD (SQLite)
│   ├── SessionRepository                Session CRUD (SQLite)
│   ├── ConversationRepository           Conversation envelope persistence (SQLite)
│   ├── PreferencesRepository            Preferences persistence (SQLite)
│   ├── IdentityStore                    Ed25519 key pair persistence (SQLite)
│   ├── ConnectionTester                 /health probe + relay resolution
│   ├── AgentInfoService                 /info fetch & change detection
│   ├── AgentPresenceService             In-memory online status cache
│   ├── ThemeService                     Light/dark theme apply
│   └── AppJsonContext                   System.Text.Json source-gen context
│
├── Controls/                           Reusable UI components
│   ├── ShellSidebar                     Agent list + session list navigation
│   └── AddAgentForm                     Inline agent creation form
│
├── Common/                             Shared utilities
│   ├── ObservableObject                 INotifyPropertyChanged base
│   ├── RelayCommand                     ICommand implementation
│   ├── BoolToVisibilityConverter        XAML value converters
│   └── ChatRoleConverters               Role-based alignment & color converters
│
└── Assets/                             App icons & splash images
```

## Architecture

```
User
  └─ WinUI 3 Window (MainWindow)
       ├─ Custom title bar (Mica backdrop, nav buttons, menu placeholders)
       ├─ ShellSidebar (agent list → session list → content)
       └─ Frame navigation
            ├─ HomePage      Select an agent to start
            ├─ ChatPage      WebSocket-powered chat with streaming events
            ├─ SessionsPage  Manage per-agent conversations
            ├─ AgentDetailPage Edit / test / delete agent
            ├─ IdentityPage  View local Ed25519 public key
            └─ SettingsPage  Theme, sidebar, font, shortcuts
```

**Data flow:**

```
ChatPage → ChatViewModel → ConnectOnion.Protocol (WebSocket)
                                  ↓
                           AppServices.Http (REST)
                                  ↓
ChatPage ← ChatViewModel ← Stream events → ChatMessage list
                                  ↓
                    ConversationRepository → SQLite
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
| `sessions` | Per-agent conversation sessions (id, title, timestamps) |
| `conversations` | Full chat envelopes as JSON blobs |
| `preferences` | Theme, sidebar, font size, shortcut overrides |
| `identity_keys` | Local Ed25519 key pair |
| `app_meta` | Key-value application metadata |

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
- **Session management** — per-agent sessions, touch/rename, conversation persistence
- **Event cards** — activity events (thinking, intent, eval) and tool events (args/result collapsible)
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
| `agent/` | Python agent implementations (email-agent, browser-agent) |
| `frontend/` | Legacy Electron + React client (reference only) |

## Repository

Part of the [ConnectOnion Desktop](https://github.com/openonion/connectonion) monorepo. See the [root README](../README.md) for build instructions, CI details, and migration history.
