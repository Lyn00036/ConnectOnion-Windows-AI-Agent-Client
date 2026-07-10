# ConnectOnion Desktop

ConnectOnion Desktop is a native Windows desktop client for chatting with
ConnectOnion-compatible agents. The current client is built with WinUI 3, C#,
.NET 10, SQLite, and a C# ConnectOnion protocol library.

The old `frontend/` Electron + React implementation is kept in the repository as
legacy reference code. New desktop work should happen in the WinUI projects.

## Current Architecture

```text
User
  -> WinUI 3 window (MainWindow with Mica backdrop and custom title bar)
  -> ShellSidebar: agent list + session list navigation
  -> XAML pages: Home, Chat, AgentDetail, Sessions, Identity, Settings
  -> ViewModels: MVVM state and commands
  -> Services:
      -> SQLite repositories (Data/) for local app state
      -> ConnectionTester for /health and relay reachability probes
      -> AgentPresenceService for in-memory online status caching
      -> AgentInfoService for /info fetch, caching, and change detection
      -> IdentityStore for the local Ed25519 identity (DPAPI-encrypted seed)
      -> ThemeService for light/dark theme management
      -> Attachment pipeline: PickerService / DropService / ValidationService
         / Encoder / ImageCacheService for outgoing + incoming multimodal content
      -> ConnectOnion.Protocol for WebSocket protocol handling
          -> ClientWebSocket / HttpClient
          -> canonical JSON signing
          -> relay resolution or Direct URL connection
```

Local app data is stored in SQLite at:

```text
%AppData%\ConnectOnion\connectonion.db
```

The database is created automatically during development startup. There is no
legacy JSON migration path at the moment because the app is still in active
development and local data does not need to be preserved.

Current SQLite tables:

| Table | Key columns |
| --- | --- |
| `app_meta` | key, value |
| `agents` | id, name, address, direct_url, invite_code, info_json, info_updated_at, sort_order |
| `sessions` | id, agent_id, title, remote_session_id, last_processed_event_id, created_at, updated_at, sort_order |
| `preferences` | theme, sidebar_visible, message_font_size, shortcut_overrides_json, microphone_device_id |
| `messages` | conversation_id, id, role, content, agent_name, event_kind/title/detail/meta/args/result/status, is_onboarding, created_at |
| `message_attachments` | conversation_id, message_id, id, kind, file_name, mime_type, size_bytes, local_cache_path, remote_uri, status |
| `executions` | id, conversation_id, remote_session_id, prompt, result, status, duration_ms, created_at |
| `trace_events` | id, conversation_id, execution_id, session_id, type, payload_json, ts |
| `identity_keys` | address, private_seed (DPAPI-protected, base64-encoded) |

`messages` is one row per rendered chat bubble (not a serialized conversation
envelope) — `ConversationRepository` deletes and reinserts the full message
list per save. `message_attachments` stores attachment metadata only; the
base64 payload is never persisted. The database uses WAL journal mode and
enforces foreign keys.

## Project Structure

```text
connectonion-desktop/
  ConnectOnion.WinUIClient/
    ConnectOnion.WinUIClient.sln      Visual Studio solution
    ConnectOnion.WinUIClient.csproj   WinUI 3 desktop app, net10.0-windows
    App.xaml[.cs]                     App startup
    MainWindow.xaml[.cs]              Shell window with Mica backdrop, custom title bar
    MainWindow.Tray.cs               System tray icon, close dialog, minimize/restore
    MainWindow.FileMenu.cs           File menu handlers and keyboard shortcuts
    MainWindow.EditMenu.cs           Undo/redo/cut/copy/paste/select-all + accelerators
    MainWindow.ViewMenu.cs           Sidebar toggle, zoom, full screen, find overlay
    MainWindow.DragDrop.cs           Window-level drag-and-drop attachment handling
    Views/
      HomePage                        Agent selection landing page
      ChatPage                        Real-time chat with streaming events
      AgentDetailPage                 Agent edit, /info display, connection test, delete
      SessionsPage                    Per-agent session management
      IdentityPage                    Local Ed25519 identity display
      SettingsPage                    Theme, sidebar, font size, shortcuts
    ViewModels/
      ChatViewModel.cs                 Session/connection lifecycle, SendAsync
      ChatViewModel.StreamEvents.cs    Stream-event -> ChatMessage mapping, interactive turns
      SessionsViewModel.cs             Session CRUD, active session tracking
      AgentDetailViewModel.cs          Agent form state, /info loading, connection testing
      SettingsViewModel.cs             Preference read/write
    Controls/
      ShellSidebar.xaml[.cs]          Agent list + session list navigation
      AddAgentForm.xaml[.cs]          Inline agent creation form
      ChatComposer.xaml[.cs]          Message input, attachment picker/drop, mic device
    Models/
      AgentConfig.cs                  Saved agent connection
      AgentHealthResponse.cs          /health endpoint shape
      ChatMessage.cs                  Chat bubble (user/agent/event), generic Event* fields
      ChatAttachment.cs               Resolved attachment metadata for a chat bubble
      PendingAttachment.cs            Picked/dropped attachment awaiting send
      AskUserEntries.cs               ask_user interactive turn field models
      OnboardSubmission.cs            Onboarding invite code payload
      PreferencesSnapshot.cs          Theme, font size, sidebar, shortcuts, mic device
      SessionSummary.cs               Indexed conversation metadata
      ShellAgentItem.cs               Sidebar agent list item
      ShellSessionItem.cs             Sidebar session list item
    Data/
      AppDatabase.cs                  SQLite initialization and schema
      AppStorage.cs                   Local filesystem directory setup
      AgentRepository.cs              Agent CRUD (SQLite)
      SessionRepository.cs            Session CRUD (SQLite)
      ConversationRepository.cs       Message/attachment persistence (SQLite)
      PreferencesRepository.cs        Preference persistence (SQLite)
      IdentityStore.cs                Local Ed25519 identity persistence, DPAPI-encrypted seed
    Services/
      AppServices.cs                  Process-wide singleton service locator
      ConnectionTester.cs             Direct URL and relay reachability probes
      AgentInfoService.cs             /info fetch, 5-minute cache, change detection
      AgentPresenceService.cs         In-memory online status cache with TTL
      ThemeService.cs                 Light/dark theme management, raw Color lookup
      AttachmentPickerService.cs      FileOpenPicker for outgoing images/files
      AttachmentDropService.cs        Drag-and-drop attachment handling
      AttachmentValidationService.cs  Preflight checks against agent /info capabilities
      AttachmentEncoder.cs            Off-UI-thread data: URL encoding
      AttachmentImageCacheService.cs  Decode/cache incoming agent_image attachments
      AttachmentIntake.cs             Shared picker/drop -> validated attachment pipeline
      MimeTypeResolver.cs             Extension -> MIME type lookup
    Rendering/
      WinUiMarkdownRenderer.cs        Markdown -> WinUI inline/block rendering for chat
    Common/
      ObservableObject.cs             INotifyPropertyChanged base class
      RelayCommand.cs                 ICommand implementation
      BoolToVisibilityConverter.cs    XAML value converters
      ChatRoleConverters.cs           Role-based alignment and color converters
      RowStateToBackgroundConverter.cs  Unified hover/selected row background
      LocalPathToImageSourceConverter.cs  Cached image path -> BitmapImage
    Styles/
      Colors.xaml                     Raw Light/Dark Color resources (brand palette)
      Brushes.xaml                    Semantic brushes + re-pointed Fluent theme keys
    Properties/PublishProfiles/       win-x86, win-x64, win-arm64 profiles
    Package.appxmanifest              MSIX package manifest

  ConnectOnion.Protocol/
    AgentConnectionService.cs         WebSocket state machine
    AgentIdentity.cs                  Ed25519 identity and signing
    CanonicalJson.cs                  Canonical JSON serializer
    EndpointResolver.cs               Relay lookup and endpoint probing
    WireMessage.cs                    Protocol message definitions
    InputMessageBuilder.cs            Builds the INPUT wire frame (text/images/files)
    AttachmentModels.cs               DataUrlCodec + agent_image/files_received parsers
    InteractiveModels.cs              ask_user, approval, plan review models
    Hex.cs                            Lowercase hex helpers

  ConnectOnion.Protocol.Conformance/
    Program.cs                        C# vs JS signing conformance runner
    ref-sign.js                       Reference JavaScript signer

  ConnectOnion.Protocol.Tests/        xunit tests for ConnectOnion.Protocol (not in .sln/CI yet)
  ConnectOnion.Protocol.LiveTest/
    Program.cs                        Optional live WebSocket integration test

  agent/                              Python agent implementations (email, browser, memory, simple)
  frontend/                           Legacy Electron + React client
  .github/workflows/ci.yml            Windows/.NET CI workflow
  global.json                         SDK selection for local builds
```

## Requirements

- Windows 10 version 1809, build 17763, or later
- .NET 10 SDK
- Windows App SDK runtime matching the project package line
- Visual Studio 2022 is recommended for WinUI debugging and packaging
- Node.js 22 is only needed for the protocol conformance test runner

The repository pins the SDK family through `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

This means any installed .NET 10 feature-band SDK newer than `10.0.100` can be
used automatically.

## Visual Studio Workflow

Open:

```text
ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln
```

Recommended settings:

- Startup project: `ConnectOnion.WinUIClient`
- Configuration: `Debug`
- Platform: `x64`
- Configuration Manager: `Build` and `Deploy` enabled for the WinUI project

Use Visual Studio F5 for the normal packaged WinUI debug loop. This is the most
reliable path for XAML debugging, package deployment, and Windows App SDK
activation.

If Visual Studio says the project must be deployed before debugging, check
Configuration Manager for the active platform you are actually running. `Deploy`
must be checked for `ConnectOnion.WinUIClient` under that platform.

## Command Line Workflow

Restore and build the solution:

```powershell
dotnet restore ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln -p:Platform=x64

dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln `
  --configuration Debug `
  --no-restore `
  -p:Platform=x64
```

Run the app from the command line in unpackaged mode:

```powershell
dotnet run `
  --project ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.csproj `
  -p:Platform=x64 `
  -p:RunUnpackaged=true
```

For command-line unpackaged runs, make sure the Windows App SDK runtime is
installed. If the app exits immediately or shows `REGDB_E_CLASSNOTREG`, the
usual cause is a missing or mismatched Windows App SDK runtime for the current
architecture.

Build a Release validation shape similar to CI:

```powershell
dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln `
  --configuration Release `
  --no-restore `
  -p:Platform=x64 `
  -p:AppxPackageSigningEnabled=false `
  -p:GenerateAppxPackageOnBuild=false `
  -p:PublishReadyToRun=false `
  -p:PublishTrimmed=false
```

## Tests And Validation

Run the protocol conformance gate:

```powershell
dotnet run `
  --project ConnectOnion.Protocol.Conformance\ConnectOnion.Protocol.Conformance.csproj `
  --configuration Release
```

This verifies that the C# protocol implementation and the JavaScript reference
signer produce matching canonical JSON Ed25519 signatures.

Run the optional live protocol test:

```powershell
dotnet run `
  --project ConnectOnion.Protocol.LiveTest\ConnectOnion.Protocol.LiveTest.csproj
```

The live test requires a reachable ConnectOnion-compatible agent. It is not part
of the default CI gate.

Check NuGet package vulnerability status:

```powershell
dotnet list ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.csproj package `
  --vulnerable `
  --include-transitive
```

## CI

The GitHub Actions workflow is `.github/workflows/ci.yml`.

Current CI behavior:

- Runs on `windows-latest`
- Installs .NET `10.0.x`
- Installs Node.js 22 for the JavaScript conformance signer
- Restores NuGet packages
- Builds `ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln`
- Uses `Platform=x64`
- Disables package signing and app package generation for CI builds
- Runs `ConnectOnion.Protocol.Conformance`

The CI workflow intentionally does not build the old `frontend/` app.

## Connect To An Agent

Add agents from the Home page or sidebar. Click **"Add another agent"** or use the
`+` button in the sidebar to create a new agent connection.

Connection fields:

```text
Agent name     Local display name
Agent address  0x... address for relay lookup
Direct URL     Optional deployed host URL
Invite code    Optional onboarding code when the agent requires one
```

Prefer `Direct URL` for day-to-day development when the remote host is known.
The client probes `{Direct URL}/health` during connection testing and connects
to `{Direct URL}/ws` for chat. If a Direct URL contains a base path, such as
`http://host/email`, that base path is preserved.

Select an agent from the sidebar to open its detail page, see `/info` data,
and start a new chat session. The client auto-creates a session when you
select an agent and begin chatting.

Agent hosts should expose:

```text
GET /health
GET /info
WS  /ws
```

Known deployed development agents:

| Agent | Direct URL | Address |
| --- | --- | --- |
| email-agent | `http://124.156.170.117/email` | `0x66f0bb49d47cb1859678f1e39c0d40a3b2e2e96921e7de9ca7dae8e0aacaa2ca` |
| browser-agent | `http://124.156.170.117/browser` | `0xf83dfaec8890059e0bc247e78449000a4d81e808ad1d45042cfcbf28406713cb` |
| remote-admin-agent | `http://124.156.170.117/remote-admin` | `0xbd53aac6952595726f2853542a2dde199da7327cd03903f328e6572d0b268506` |
| multimodal_agent | `http://124.156.170.117/multimodal` | `0x01bd18d01e3a05d5d4387446f5806e59d0b0a910803bad3f1515c6842fc670fb` |

> **Access notes:** `remote-admin-agent` is not self-service — contact **Kai Pan** to get
> added to the whitelist before connecting. All other agents above accept the invite
> code `COMP9900` during onboarding.

## Implemented

- Native WinUI 3 desktop shell with Mica backdrop and custom title bar
- Home, Chat, Agent Detail, Sessions, Identity, and Settings pages
- Agent add, edit, delete, select, and connection testing (Direct URL + relay)
- Agent /info fetch with 5-minute cache and change detection
- Agent online presence with TTL-based caching and per-agent status indicators
- Per-agent sessions with message/attachment persistence (one row per chat bubble)
- Local Ed25519 identity display (Identity page), seed encrypted at rest via DPAPI
- SQLite-backed local storage with WAL journal mode and foreign keys
- Direct URL and relay connection paths
- WebSocket protocol handling through `ConnectOnion.Protocol`
- Canonical JSON signing compatible with the JavaScript reference implementation
- Stream event display for thinking/tool/status events with collapsible cards
- Interactive turns (`ask_user`, `approval_needed`, `plan_review`) as persistent inline chat bubbles
- Multimodal attachments: outgoing image/file picking and drag-and-drop, agent-capability
  preflight validation, and incoming `agent_image`/`files_received` rendering with local disk cache
- User and agent message layout aligned with the current desktop UX
- Markdown rendering for chat messages (`WinUiMarkdownRenderer`)
- Copy action for chat messages
- Collapse/expand for long responses
- In-chat find overlay (Ctrl+F) with match highlighting and next/previous navigation
- Agent offline notice surfaced in the chat composer
- Light/dark theme with shell-aware title bar colors and semantic brush system
- Configurable message font size (S/M/L)
- Keyboard shortcuts (sidebar toggle, new chat, zoom, full screen, find) with user-customizable bindings
- Responsive sidebar with agent list and session list navigation
- Back/forward navigation in the title bar
- Suggested prompts for new conversations
- System tray icon with left-click restore, right-click menu (Open Window / Exit)
- Close button (X) dialog with Minimize to tray / Exit / Cancel options
- File menu with keyboard shortcuts (Ctrl+N new chat, Ctrl+Shift+N new window, Ctrl+W close)
- Edit menu (undo/redo/cut/copy/paste/select all) and View menu (sidebar, zoom, full screen, find)
- Protocol conformance test gate
- Windows/.NET CI workflow

## Not Implemented Yet

- Production installer/release pipeline (MSIX packaging is configured but not yet automated)
- `ConnectOnion.Protocol.Tests` is not wired into the `.sln` or CI (run directly with `dotnet test`)
- Live protocol tests in CI
- Full migration tooling for preserving older local app data (a pre-DPAPI identity row is
  regenerated rather than migrated; a database created before the `messages` schema isn't migrated)

## Notes For Development

- Keep new desktop changes in the WinUI projects.
- Treat `frontend/` as legacy reference unless a task explicitly asks for it.
- Use `x64`, `x86`, or `ARM64` for WinUI builds. Avoid relying on `Any CPU` for
  packaged app debugging.
- Keep generated runtime files out of source control, including `bin/`, `obj/`,
  `.vs/`, `.co/`, local SQLite databases, MSIX/appx output, and user-specific
  publish files.

### Local database location

The app stores all data in a single SQLite file `connectonion.db`.  The path
depends on how the app is launched:

| Launch mode | Path |
|---|---|
| Unpackaged (`dotnet run`) | `%APPDATA%\ConnectOnion\connectonion.db` |
| Packaged / MSIX (VS Debug) | `%LOCALAPPDATA%\Packages\779a2035-a8d1-4bc8-91cf-82aecda14cc5_<hash>\LocalState\ConnectOnion\connectonion.db` |

To reset app data during development, delete the file and restart the app —
the database is recreated from the embedded schema on next launch.

## References

- [WinUIClient project README](./ConnectOnion.WinUIClient/README.md)
- [WinUI 3 native migration notes](./WINUI3_NATIVE_MIGRATION.md)
- [ConnectOnion Python repository](https://github.com/openonion/connectonion)
- [ConnectOnion TypeScript repository](https://github.com/openonion/connectonion-ts)
