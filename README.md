# ConnectOnion Desktop

ConnectOnion Desktop is a native Windows desktop client for chatting with
ConnectOnion-compatible agents. The current client is built with WinUI 3, C#,
.NET 10, SQLite, and a C# ConnectOnion protocol library.

The repository is C#-only. The original Electron + React `frontend/` and the
vendored Python `agent/` examples were removed in `197a88d`; if you need the
prior behavior, read them out of git history rather than restoring the trees.

## Current Architecture

```text
User
  -> WinUI 3 window (MainWindow with Mica backdrop and custom title bar)
  -> ShellSidebar: agent list + session list navigation
  -> XAML pages: Home, Chat, AgentDetail, Sessions, Settings (identity + usage live inside Settings)
  -> ViewModels: MVVM state and commands
  -> Services:
      -> SQLite repositories (Data/) for local app state
      -> SchemaMigrator for versioned forward database migrations
      -> ConnectionTester for /health and relay reachability probes
      -> AgentPresenceService for in-memory online status caching
      -> AgentInfoService for /info fetch, caching, and change detection
      -> IdentityStore for the local Ed25519 identity (DPAPI-encrypted seed)
      -> ThemeService for light/dark theme management
      -> AgentSessionManager for app-level turn lifecycle (send, run, persist)
      -> KeyboardShortcutService for customizable keyboard shortcuts
      -> Attachment pipeline: PickerService / DropService / ValidationService
         / Encoder / ImageCacheService for outgoing + incoming multimodal content
      -> Speech pipeline: DictationTranscript for speech-to-text input
      -> Diagnostics: StartupProfiler / StartupTelemetry for performance measurement
      -> Usage heatmap: UsageHeatmapModels / UsageHeatmapView for activity visualization
      -> ConnectOnion.Protocol for WebSocket protocol handling
          -> ClientWebSocket / HttpClient
          -> canonical JSON signing
          -> relay resolution or Direct URL connection
```

WinUI-free application logic and persistence live in
`ConnectOnion.WinUIClient.Core`; architecture tests enforce that it stays free
of `Microsoft.UI` dependencies. Package versions are centrally managed by
`Directory.Packages.props`. See [engineering optimization notes](docs/OPTIMIZATION.md)
for diagnostics, performance, localization, and verification details.

Local app data lives in SQLite, with everything else stored beside it under the
same data root:

```text
%AppData%\ConnectOnion\
  connectonion.db
  conversations\
  cache\images\        Received agent images, named by content hash
  avatars\             User-chosen agent icons (agents.icon_path points here)
  temp\avatars\        A picked icon awaiting save; emptied at startup
  logs\                Structured rolling logs (10 MB cap, 14 retained)
```

That root is `%AppData%\ConnectOnion` for an unpackaged or portable run, the
package's `LocalState\ConnectOnion` for an installed MSIX, and whatever
`CONNECTONION_DATA_ROOT` names when it is set (used to run an isolated instance
for automation). Logs and caches follow the database rather than living at a
fixed path.

Speech/audio resources and page-load database work are cancellation-aware and
are released when their owning WinUI surface unloads.

The database is created automatically on first launch and then upgraded in
place: the schema is versioned via `PRAGMA user_version` and stepped forward by
`Data/SchemaMigrator.cs`, so an existing database is migrated rather than
discarded. There is no legacy JSON → SQLite import path (the Electron client's
local data is not carried over). Deleting the `.db` file to reset dev state is a
convenience, not the upgrade path.

Current SQLite tables:

| Table | Key columns |
| --- | --- |
| `app_meta` | key, value |
| `agents` | id, name, address, direct_url, icon_path, invite_code, info_json, info_updated_at, sort_order |
| `sessions` | id, agent_id, title, remote_session_id, last_processed_event_id, created_at, updated_at, sort_order, mode |
| `preferences` | theme, sidebar_visible, message_font_size, shortcut_overrides_json, microphone_device_id |
| `messages` | conversation_id, id, role, content, agent_name, event_kind/key/eyebrow/title/detail/meta/args/result/status, is_onboarding, created_at |
| `message_attachments` | conversation_id, message_id, id, kind, file_name, mime_type, size_bytes, local_cache_path, remote_uri, status |
| `executions` | id, conversation_id, remote_session_id, prompt, result, status, duration_ms, created_at |
| `trace_events` | id, conversation_id, execution_id, session_id, type, payload_json, ts |
| `identity_keys` | address, private_seed, mnemonic (both DPAPI-protected, base64-encoded) |
| `usage_events` | id, conversation_id, agent_id, agent_name, model, input_tokens, output_tokens, cached_tokens, cache_write_tokens, duration_ms, created_at |

`messages` is one row per rendered chat bubble (not a serialized conversation
envelope), keyed `(conversation_id, id)`. Saves are incremental:
`ConversationRepository.UpsertMessagesAsync` writes only the rows it is handed
(upsert on the primary key), and rewrites attachments only for those rows — a
finished turn no longer reads the conversation back and rewrites every row of
it, so persisting a turn costs the size of the turn rather than the length of
the conversation. `message_attachments` stores attachment metadata only; the
base64 payload is never persisted. The database uses WAL journal mode and
enforces foreign keys.

`agents.icon_path` follows the same rule as attachments: the database stores a
path relative to the data root (`avatars/agent-….png`), never the image bytes.
The file is written before the row that references it and deleted only after
that row is gone, so a failure leaves an unreferenced file rather than an agent
pointing at one that no longer exists. Because the column is hand-editable,
every read resolves through `AppStorage.GetAgentIconAbsolutePath`, which refuses
a path that escapes the managed `avatars\` directory.

## Project Structure

```text
connectonion-desktop/
  Directory.Packages.props            Central NuGet package versions and audit policy
  Directory.Build.props               Shared analyzer and deterministic-build settings

  ConnectOnion.WinUIClient.Core/      Every WinUI-free surface of the client. The testability
                                      seam: test projects reference this, never the app project.
    Data/                             SQLite persistence (this whole layer is Core-only)
      AppDatabase.cs                  Connection open, baseline schema, PRAGMA setup
      SchemaMigrator.cs               Versioned forward migrations (PRAGMA user_version)
      AppStorage.cs                   Data-root selection, directory setup, icon path guards
      AgentRepository.cs              Agent CRUD
      SessionRepository.cs            Session CRUD
      ConversationRepository.cs       Message/attachment persistence, incremental upsert
      ConversationRepository.Mapping.cs  Row<->object SQL plumbing for the above
      PreferencesRepository.cs        Preference persistence
      UsageRepository.cs              Token-usage ledger (no FK, never cascaded)
      IdentityStore.cs                Ed25519 identity persistence, DPAPI-encrypted seed
      AppJsonContext.cs               Source-generated JSON context for local persistence
    Models/                           AgentConfig, ChatMessage, ChatAttachment, SessionSummary,
                                      ToolActivity, PreferencesSnapshot, KeyChord/KeyBinding,
                                      UsageModels, UsageHeatmapModels, AskUserEntries, Notifications/
    Services/
      ConversationCache.cs            LRU cache of the last few idle conversations
      SessionSelection.cs             The one testable "which session?" rule
      KeyboardShortcutCatalog.cs      Single source of truth for every advertised shortcut
      KeyboardShortcutService.cs      Resolves catalog default + user override into a chord
      AgentAddressValidator.cs        Per-keystroke address validation
      AgentEndpointDuplicateDetector.cs  Duplicate detection across address/direct URL
      MimeTypeResolver.cs             Extension -> MIME type lookup
      AppVersionService.cs            Assembly version for the About overlay
      Runtime/
        ChatTurnProjection.cs         The single stream-event -> ChatMessage mapping
        ChatTurnProjection.Events.cs    ...its per-event-type dispatch table
        ToolActivityProjector.cs      tool_call/tool_result -> tool timeline aggregation
        ToolActivityMigration.cs      Upgrades legacy persisted tool bubbles at load
        UsageProjector.cs             Finished run -> token-usage ledger rows
        AgentConnectionRegistry.cs    Per-conversation app-owned WebSockets, idle eviction
      Attachments/                    Validation, encoding, attachment-only prompt fallback
      Notifications/                  Dedup, policy, text shaping (no UI, no OS calls)
      Speech/                         DictationTranscript for speech-to-text
    Diagnostics/                      StartupProfiler for launch-time measurement (WinUI-free)
    ViewModels/                       KeyboardSettings / KeyboardShortcuts (WinUI-free)
    Common/                           ObservableObject base, NameInitial

  ConnectOnion.WinUIClient/           The WinUI 3 app: XAML, code-behind, and anything that
                                      needs a window or the Windows App SDK
    ConnectOnion.WinUIClient.sln      Visual Studio solution (references every other project)
    ConnectOnion.WinUIClient.csproj   net10.0-windows
    App.xaml[.cs]                     Generic Host build, Serilog, composition root
    Program.cs                        Entry point
    MainWindow.xaml[.cs]              Shell window, Mica backdrop, custom title bar
    Shell/                            The rest of the MainWindow partial (see Shell/README.md)
      MainWindow.FileMenu.cs          File menu handlers
      MainWindow.EditMenu.cs          Undo/cut/copy/paste against the last focused text box
      MainWindow.ViewMenu.cs          Sidebar toggle, zoom, full screen, shared find overlay
      MainWindow.HelpMenu.cs          Shortcuts dialog, docs link, About overlay
      MainWindow.Shortcuts.cs         Pushes live chords into menu accelerator text
      MainWindow.Notifications.cs     In-app toast host, notification activation routing
      MainWindow.DragDrop.cs          Window-wide "drop it on the composer" hint
      MainWindow.Tray.cs              Tray icon, close dialog, minimize/restore
      MainWindow.Tray.Interop.cs      The Win32 P/Invoke the tray needs
    Views/
      HomePage                        Agent selection landing page
      ChatPage                        Real-time chat; split by concern:
        ChatPage.Scrolling.cs           Scroll position, stick-to-bottom, load overlay
        ChatPage.MessageActions.cs      Copy / edit / retry and the hover-reveal chrome
        ChatPage.Attachments.cs         Opening and saving message attachments
        ChatPage.Interactive.cs         ask_user / approval / plan-review cards
        ChatPage.Find.cs                In-chat find (IFindHost implementation)
      AgentDetailPage                 Agent edit, /info display, connection test, delete
      SessionsPage                    Conversation management list
      SettingsPage                    Theme, font size, notifications, audio
      IFindHost.cs                    Contract the shared Ctrl+F overlay drives a page through
      IReloadablePage.cs              Contract for pages reused across navigation
      IShutdownDisarmable.cs          Contract for pages that own timers needing shutdown disarm
    ViewModels/
      ChatViewModel.cs                State, properties, conversation load/restore
      ChatViewModel.Run.cs            Send/stop/mode; folds run snapshots into the list
      ChatViewModel.Conversation.cs   New / branch / retry (re-points the run subscription)
      ChatViewModel.StreamEvents.cs   Live stream-event -> ChatMessage projection target
      SessionsViewModel.cs            Session CRUD, active session tracking
      AgentDetailViewModel.cs         Agent form state, /info loading, connection testing
      SettingsViewModel.cs            Preference read/write
      UsageViewModel.cs               Per-model token totals for the Usage panel
      PresenceAwareViewModel.cs       Shared online/offline status base for pages
    Controls/                         Grouped by surface (see Controls/README.md; all keep the
                                      flat ConnectOnion.WinUIClient.Controls namespace)
      Chat/                           ChatComposer (+ .Speech), ToolActivityView, OfflineNoticeBar
      Agents/                         AddAgentForm, AgentAvatar
      Settings/                       SettingsOverlay + content panes, shortcut editing, HotkeyInput,
                                      UsageHeatmapView (activity heatmap)
      Shell/                          ShellSidebar (+ .Events), InAppNotificationHost, AboutOverlay
      Primitives/                     MarkdownTextBlock, HighlightedTextBlock, IconText,
                                      DisclosureAnimation (no view-model or feature-model refs)
    Services/                         Everything here needs a window, the SDK, or DI wiring
      ServiceRegistration.cs          Generic Host registrations (AddAppServices)
      AppServices.cs                  Typed accessor over App.Services for framework-created code-behind
      AgentPresenceService.cs         In-memory online status cache with TTL
      AgentInfoService.cs             /info fetch, cache, change detection
      AgentIconService.cs             Icon pick, decode/crop/resize to PNG, commit, delete
      ConnectionTester.cs             Direct URL and relay reachability probes
      ThemeService.cs                 Light/dark theme management, raw Color lookup
      UriLauncher.cs                  Mockable wrapper over Launcher.LaunchUriAsync
      ClipboardService.cs             Clipboard writes
      LayoutKeys.cs                   Keyboard-layout translation (P/Invokes MapVirtualKeyW)
      KeyboardShortcutStartupService.cs  IHostedService that loads shortcut overrides
      Runtime/
        AgentSessionManager.cs        App-level turn lifecycle: send, run, project, persist
        AgentTurnExecutor.cs          Drives one turn over a connection
        IAgentSessionManager.cs       The contract pages depend on
      Attachments/                    Picker, drop, image cache (the window-bound half)
      Notifications/                  Windows toasts, in-app toasts, activation routing
    Diagnostics/                      StartupTelemetry for wall-clock phase marks
    Models/                           Sidebar row items only (the rest live in Core)
    Rendering/
      WinUiMarkdownRenderer.cs        Markdown -> WinUI inline/block rendering for chat
    Common/                           Template selector, XAML value converters (incl. HeatmapLevelToBrush),
                                      AvatarPalette, LocalizedStrings (resw lookup with a fallback)
    Styles/
      Colors.xaml                     Raw Light/Dark Color resources (brand palette)
      Brushes.xaml                    Semantic brushes + re-pointed Fluent theme keys
    Strings/en-US/                    .resw resources (localization is scaffolded, not finished)
    Properties/PublishProfiles/       win-x86, win-x64, win-arm64 profiles
    Package.appxmanifest              MSIX package manifest

  ConnectOnion.Protocol/              Transport-agnostic, no WinUI dependency
    AgentConnectionService.cs         WebSocket state machine
    AgentIdentity.cs                  Ed25519 identity and signing
    CanonicalJson.cs                  Canonical JSON serializer (byte-identical to the JS signer)
    EndpointResolver.cs               Relay lookup, endpoint probing, /info capability parsing
    WireMessage.cs                    Thin JsonElement wrapper over an incoming frame
    InputMessageBuilder.cs            Builds the INPUT wire frame (text/images/files)
    AttachmentModels.cs               DataUrlCodec + agent_image/files_received parsers
    AgentInteractiveParsers.cs        ask_user frame parsing
    InteractiveModels.cs              ask_user, approval, plan review models
    AgentModes.cs                     Approval-mode constants and display names
    Hex.cs                            Lowercase hex helpers
    Runtime/
      ConversationRunRegistry.cs      App-level owner of every in-flight turn
      ConversationRun.cs              One run's mutable state
      ConversationRunSnapshot.cs      Immutable published view of a run
      ConversationRunStatus.cs        Run status enum
      RunContracts.cs                 IRunSink / IRunPersistence / ITurnExecutor

  ConnectOnion.Protocol.Conformance/
    Program.cs                        C# vs JS signing conformance runner (the CI gate)
    ref-sign.js                       Reference JavaScript signer

  ConnectOnion.Protocol.LiveTest/
    Program.cs                        Optional live WebSocket test against a real agent

  tests/
    ConnectOnion.Protocol.Tests/      xunit tests for ConnectOnion.Protocol
    ConnectOnion.WinUIClient.UnitTests/  Headless tests over Core, plus the ArchUnit layer gate
    ConnectOnion.IntegrationTests/    SQLite schema/repository tests against a real database file
    ConnectOnion.WinUIClient.UITests/ Opt-in FlaUI real-window shell smoke tests

  .github/workflows/ci.yml            Windows/.NET CI workflow

  scripts/Measure-Performance.ps1     Launch-time and memory benchmark script
```

## Requirements

| Requirement | Version | Needed for |
| --- | --- | --- |
| Windows | 10 version 1809 (build 17763) or later | Running the app |
| .NET SDK | 10.0.100 or a newer .NET 10 feature band | Building everything |
| Windows App SDK runtime | 2.2 (matches the `Microsoft.WindowsAppSDK` package reference) | Running the WinUI app, especially unpackaged |
| Visual Studio | **2026** (recommended) | XAML debugging, MSIX packaging, F5 loop |
| Node.js | 22 | Protocol conformance test runner only |

Visual Studio 2026 is the recommended IDE. Visual Studio 2022 (17.14+) can still
open and build the solution, but 2026 is what the WinUI 3 / .NET 10 toolchain is
tested against here, so prefer it for new setups.

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

## Installing The Requirements

All commands below run in a normal PowerShell prompt. `winget` ships with
Windows 11 and current Windows 10; if it is missing, install **App Installer**
from the Microsoft Store first.

### 1. Visual Studio 2026

Install the IDE plus the workload and components WinUI 3 needs:

```powershell
winget install --id Microsoft.VisualStudio.Community --override `
  "--passive --wait --add Microsoft.VisualStudio.Workload.ManagedDesktop --includeRecommended --add Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs"
```

The unversioned `Microsoft.VisualStudio.Community` id is Visual Studio 2026 —
the `...2022.Community` id is the previous release. Use
`Microsoft.VisualStudio.Professional` or `Microsoft.VisualStudio.Enterprise`
instead if that is your licence. If you already have Visual Studio installed, open the
**Visual Studio Installer**, click **Modify**, and make sure these are checked:

- Workload: **.NET Desktop Development**
- Individual component: **Windows App SDK C# Templates**
- Individual component: **MSIX Packaging Tools** (only needed for packaged
  builds / MSIX output)

The workload also installs the Windows 11 SDK and the MSBuild toolchain, so no
separate SDK install is required.

### 2. .NET 10 SDK

Visual Studio 2026 bundles a .NET 10 SDK, but a standalone install is what the
command-line workflow uses:

```powershell
winget install --id Microsoft.DotNet.SDK.10
```

Open a **new** terminal afterwards (so `PATH` is refreshed) and confirm the
pinned SDK resolves from the repository root:

```powershell
dotnet --list-sdks   # expect a 10.0.1xx entry
dotnet --version     # run inside the repo; global.json selects the SDK
```

If `dotnet --version` errors with a `global.json` message, the installed SDK is
older than `10.0.100` — install a newer .NET 10 SDK rather than editing
`global.json`.

### 3. Windows App SDK runtime

Needed to *run* the app, especially in unpackaged mode (`dotnet run`). Install
the runtime matching your machine architecture:

```powershell
winget install --id Microsoft.WindowsAppRuntime.2
```

That package id (no minor version) currently ships **2.2.0**, which is the line
the app references; winget picks the right architecture for your machine. If the
id is not available, download the runtime installer directly instead:

```powershell
# x64
curl.exe -L -o "$env:TEMP\WindowsAppRuntimeInstall-x64.exe" `
  https://aka.ms/windowsappsdk/2.2/latest/windowsappruntimeinstall-x64.exe
& "$env:TEMP\WindowsAppRuntimeInstall-x64.exe"
```

(Swap `x64` for `arm64` or `x86` as needed.) A missing or architecture-mismatched
runtime is the usual cause of the app exiting immediately with
`REGDB_E_CLASSNOTREG`.

### 4. Node.js 22 (conformance test only)

Only required if you run the protocol conformance gate, which cross-checks the
C# signer against `ref-sign.js`:

```powershell
winget install --id OpenJS.NodeJS.22
node --version   # expect v22.x
```

CI pins Node 22, so `OpenJS.NodeJS.22` keeps local runs matching it.
`OpenJS.NodeJS.LTS` (currently Node 24) also works for the conformance signer.

### 5. Verify the whole setup

From the repository root:

```powershell
dotnet restore ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln -p:Platform=x64
dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln --configuration Debug --no-restore -p:Platform=x64
dotnet test tests\ConnectOnion.Protocol.Tests\ConnectOnion.Protocol.Tests.csproj
dotnet run --project ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.csproj -p:Platform=x64 -p:RunUnpackaged=true
```

If the build succeeds, the tests pass, and the window opens, the environment is
complete.

## Visual Studio Workflow

Open with Visual Studio 2026:

```text
ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln
```

Recommended settings:

- Startup project: `ConnectOnion.WinUIClient`
- Configuration: `Debug`
- Platform: `x64`
- Configuration Manager: `Build` and `Deploy` enabled for the WinUI project

Use Visual Studio 2026 F5 for the normal packaged WinUI debug loop. This is the
most reliable path for XAML debugging, package deployment, and Windows App SDK
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

Run the automated test suites (all three run in CI):

```powershell
dotnet test tests\ConnectOnion.Protocol.Tests\ConnectOnion.Protocol.Tests.csproj
dotnet test tests\ConnectOnion.WinUIClient.UnitTests\ConnectOnion.WinUIClient.UnitTests.csproj
dotnet test tests\ConnectOnion.IntegrationTests\ConnectOnion.IntegrationTests.csproj
```

`ConnectOnion.Protocol.Tests` covers the wire protocol, `ConnectOnion.WinUIClient.UnitTests`
covers the WinUI-free app surfaces (chat projection, notifications, caches, view models), and
`ConnectOnion.IntegrationTests` exercises schema migrations and the repositories against a real
SQLite file.

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
- Runs `ConnectOnion.Protocol.Tests`, `ConnectOnion.WinUIClient.UnitTests`, and `ConnectOnion.IntegrationTests`
- Runs `ConnectOnion.Protocol.Conformance`

CI also audits dependencies for known vulnerabilities, collects Cobertura coverage,
compiles the UI smoke tests, and publishes a trimmed unpackaged build. The FlaUI
smoke test itself only runs on a `workflow_dispatch` with `run_ui_smoke` checked,
because it needs a real desktop session.

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
- Home, Chat, Agent Detail, Sessions, and Settings pages (local identity lives inside Settings)
- Agent add, edit, delete, select, and connection testing (Direct URL + relay)
- Sidebar "Add agent" button opens the HomePage add-agent form and scrolls it into view
- Agent /info fetch with 5-minute cache and change detection
- Agent online presence with TTL-based caching and per-agent status indicators
- App-level run runtime: turns are owned by `AgentSessionManager`, not the page — a turn keeps
  running (and completes, persists, and notifies) after its chat page is closed, and idle
  conversation sockets are evicted after a completed turn
- Per-agent sessions with message/attachment persistence (one row per chat bubble, incremental
  per-turn upserts rather than rewriting the conversation)
- Turn completion / approval-required notifications (Windows toasts plus in-app toasts, deduped,
  suppressed when the relevant chat is already on screen)
- Per-turn tool execution timeline (collapsible, persisted across restart)
- Token-usage ledger and Usage panel (Settings), independent of conversation/agent deletion
- Usage activity heatmap with daily aggregation and twelve-month calendar view
- Local Ed25519 identity display (Settings), seed encrypted at rest via DPAPI
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
- Speech-to-text dictation input in the chat composer
- System tray icon with left-click restore, right-click menu (Open Window / Exit)
- Close button (X) dialog with Minimize to tray / Exit / Cancel options
- File menu with keyboard shortcuts (Ctrl+N new chat, Ctrl+Shift+N new window, Ctrl+W close)
- Edit menu (undo/redo/cut/copy/paste/select all) and View menu (sidebar, zoom, full screen, find)
- Help menu (keyboard shortcuts dialog, ConnectOnion docs, About overlay)
- Protocol conformance test gate
- Windows/.NET CI workflow
- Startup telemetry and shutdown teardown instrumentation for performance measurement
- Launch-time and memory benchmark script (`scripts/Measure-Performance.ps1`)

## Not Implemented Yet

- First production release (the tag-driven self-contained portable ZIP pipeline is implemented;
  clean-machine validation is still required, while signed MSIX publishing is paused)
- Broad UI-level tests: the headless surfaces are covered and a FlaUI shell smoke test proves
  the harness works, but navigation, drag-and-drop, and the find overlay are still untested.
  `AutomationId`s exist on the critical controls (guarded by `AutomationContractTests`) —
  anything beyond those needs ids added before it can be automated
- Live protocol tests in CI
- Full migration tooling for preserving older local app data (a pre-DPAPI identity row is
  regenerated rather than migrated; a database created before the `messages` schema isn't migrated)

## Notes For Development

- Keep new desktop changes in the WinUI projects. Logic that should be tested belongs in
  `ConnectOnion.WinUIClient.Core` — the test projects can reference that, but not the app.
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
| Packaged / MSIX (installed or VS Debug) | `%LOCALAPPDATA%\Packages\ConnectOnion.Desktop_<publisher hash>\LocalState\ConnectOnion\connectonion.db` |

To reset app data during development, delete the file and restart the app —
the database is recreated from the embedded schema on next launch.

The schema is versioned (`PRAGMA user_version`) and stepped forward by
`Data/SchemaMigrator.cs`, so an existing database is upgraded rather than
discarded; deleting the file is a development convenience, not the upgrade path.

## Documentation

All project documentation lives in [`docs/`](./docs/):

| Document | What it is |
|---|---|
| [Project brief](./docs/PROJECT_BRIEF.md) | The original requirements, plus a requirement → implementation traceability table and known limitations |
| [Test plan](./docs/TEST_PLAN.md) | Layered test strategy, test cases, E2E flows, and the testing backlog |
| [Session & message structure](./docs/SESSION_MESSAGE_STRUCTURE.md) | The ConnectOnion wire protocol: `CONNECT`/`INPUT`/`OUTPUT`, streaming events, sessions, reconnect |
| [WinUI 3 migration notes](./docs/WINUI3_NATIVE_MIGRATION.md) | Historical record of the Electron → WinUI migration, and how the result diverged from the plan |
| [Commit convention](./docs/GIT_COMMIT_CONVENTION.md) | Conventional Commits types and scopes used in this repo |
| [Optimization notes](./docs/OPTIMIZATION.md) | Implemented architecture, performance, reliability, diagnostics, and localization improvements |
| [Performance baseline](./docs/PERFORMANCE.md) | Launch-time and memory benchmark: method, budgets, current baseline |
| [Concurrency notes](./docs/CONCURRENCY.md) | Thread model, synchronization primitives, and the post-`Closed` dispatcher race |

[`CLAUDE.md`](./CLAUDE.md) is the authoritative description of the current architecture.

## References

- [WinUIClient project README](./ConnectOnion.WinUIClient/README.md)
- [ConnectOnion Python repository](https://github.com/openonion/connectonion)
- [ConnectOnion TypeScript repository](https://github.com/openonion/connectonion-ts)
