# ConnectOnion Desktop

ConnectOnion Desktop is a native Windows desktop client for chatting with
ConnectOnion-compatible agents. The current client is built with WinUI 3, C#,
.NET 10, SQLite, and a C# ConnectOnion protocol library.

The old `frontend/` Electron + React implementation is kept in the repository as
legacy reference code. New desktop work should happen in the WinUI projects.

## Current Architecture

```text
User
  -> WinUI 3 window
  -> XAML pages: Chat, Agents, Sessions, Settings
  -> ViewModels: MVVM state and commands
  -> Services:
      -> SQLite repositories for local app state
      -> ConnectionTester for /health and relay checks
      -> IdentityStore for the local Ed25519 identity
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

```text
app_meta
agents
sessions
preferences
conversations
identity_keys
```

## Project Structure

```text
connectonion-desktop/
  ConnectOnion.WinUIClient/
    ConnectOnion.WinUIClient.sln      Visual Studio solution
    ConnectOnion.WinUIClient.csproj   WinUI 3 desktop app, net10.0-windows
    App.xaml[.cs]                     App startup
    MainWindow.xaml[.cs]              Shell window and navigation
    Views/                            Chat, Agents, Sessions, Settings pages
    ViewModels/                       MVVM state and commands
    Models/                           UI and persistence models
    Services/
      AppDatabase.cs                  SQLite initialization and schema
      AgentRepository.cs              Agent persistence
      SessionRepository.cs            Session persistence
      ConversationRepository.cs       Conversation persistence
      PreferencesRepository.cs        Preference persistence
      IdentityStore.cs                Local Ed25519 identity persistence
      ConnectionTester.cs             Direct URL and relay connection tests
      AppJsonContext.cs               System.Text.Json source-generation context
    Properties/PublishProfiles/       win-x86, win-x64, win-arm64 profiles
    Package.appxmanifest              MSIX package manifest

  ConnectOnion.Protocol/
    AgentConnectionService.cs         WebSocket state machine
    AgentIdentity.cs                  Ed25519 identity and signing
    CanonicalJson.cs                  Canonical JSON serializer
    EndpointResolver.cs               Relay lookup and endpoint probing
    WireMessage.cs                    Protocol message definitions
    InteractiveModels.cs              ask_user, approval, plan review models
    Hex.cs                            Lowercase hex helpers

  ConnectOnion.Protocol.Conformance/
    Program.cs                        C# vs JS signing conformance runner
    ref-sign.js                       Reference JavaScript signer

  ConnectOnion.Protocol.LiveTest/
    Program.cs                        Optional live WebSocket integration test

  agent/                              Python agent implementations
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

Add agents from the WinUI sidebar or Agents page.

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

## Implemented

- Native WinUI 3 desktop shell
- Chat, Agents, Sessions, and Settings pages
- Agent add, edit, delete, select, and connection testing
- Per-agent sessions and conversation persistence
- SQLite-backed local storage
- Local Ed25519 identity persistence
- Direct URL and relay connection paths
- WebSocket protocol handling through `ConnectOnion.Protocol`
- Canonical JSON signing compatible with the JavaScript reference implementation
- Stream event display for thinking/tool/status events
- User and agent message layout aligned with the current desktop UX
- Copy action for chat messages
- Light/dark theme and basic preferences
- Protocol conformance test gate
- Windows/.NET CI workflow

## Not Implemented Yet

- Production installer/release pipeline
- Secure OS credential storage
- File attachments and drag-and-drop
- System tray behavior
- App menu commands
- Live protocol tests in CI
- Full migration tooling for preserving older local app data

## Notes For Development

- Keep new desktop changes in the WinUI projects.
- Treat `frontend/` as legacy reference unless a task explicitly asks for it.
- Use `x64`, `x86`, or `ARM64` for WinUI builds. Avoid relying on `Any CPU` for
  packaged app debugging.
- Keep generated runtime files out of source control, including `bin/`, `obj/`,
  `.vs/`, `.co/`, local SQLite databases, MSIX/appx output, and user-specific
  publish files.

## References

- [WinUI 3 native migration notes](./WINUI3_NATIVE_MIGRATION.md)
- [Requirement-to-solution mapping](./connectonion_requirement_solution_mapping.md)
- [Email agent README](./agent/email-agent/README.md)
- [ConnectOnion Python repository](https://github.com/openonion/connectonion)
- [ConnectOnion TypeScript repository](https://github.com/openonion/connectonion-ts)
