# Engineering optimization notes

Last updated: 2026-07-16

## Implemented

- **Dependency governance:** package versions live in `Directory.Packages.props`; restore enables transitive pinning and NuGet auditing. `Directory.Build.props` enables deterministic builds and the current recommended .NET analyzers.
- **Layer boundary:** `ConnectOnion.WinUIClient.Core` owns WinUI-free models, SQLite repositories, notification policy, projections, and other testable services. The WinUI executable references Core, and ArchUnitNET tests prevent Core from taking a `Microsoft.UI` dependency or Data from depending on ViewModels.
- **Lifecycle and diagnostics:** the generic host is started before the window, stopped once on window/tray exit, and drains the run manager with bounded timeouts. Serilog writes structured rolling logs to `%AppData%\ConnectOnion\logs` (10 MB per file, 14 retained files).
- **Cancellation:** page unloading cancels conversation restore; cancellation tokens flow through view-model loading and SQLite reads/writes where supported.
- **Rendering:** `MarkdownTextBlock` caches its Markdig syntax tree. Search-highlight and theme rerenders no longer parse the same message again.
- **Resource lifecycle:** `ChatComposer` implements deterministic cleanup for speech recognition, AudioGraph nodes, Win2D stroke resources, timers, cancellation sources, and event subscriptions. Audio-meter startup observes cancellation after each asynchronous acquisition so page unload cannot leave a microphone graph behind.
- **Publish assets and exit:** unpackaged Release publish explicitly carries the `Assets` tree, and window icons resolve from an absolute application-base path. WebSocket shutdown has a 1.5-second hard budget, connections close concurrently, and structured logs record shutdown phase timings.
- **Shutdown ordering:** window activation, presence, and session callbacks detach before the Generic Host service provider is disposed. Closed callbacks are idempotent and use cached service references, preventing late WinUI events from resolving a disposed container.
- **Localization:** WinUI `.resw` resources and `x:Uid` are established under `Strings/en-US`; settings, shell menus, search/drop hints, chat approvals, composer suggestions/modes, offline state, and primary entry/empty states are migrated. Keep fallback XAML text when adding resources so development builds remain diagnosable.
- **Analyzer hygiene:** a clean unpackaged x64 application build completes with zero compiler/analyzer warnings. Hot-path logging uses cached `LoggerMessage` delegates and persisted values use explicit culture semantics.
- **Testing:** the protocol fake server uses an in-process Kestrel WebSocket endpoint on an OS-assigned port, eliminating `HttpListener` ACL and port-reservation races. FlaUI provides an opt-in real-window shell smoke test.
- **CI:** restore audits vulnerable packages, test runs emit Cobertura coverage, Release publishes are trimmed/ReadyToRun, and `workflow_dispatch` can opt into the interactive shell smoke test.

## Operational checks

```powershell
dotnet restore ConnectOnion.WinUIClient/ConnectOnion.WinUIClient.sln -p:Platform=x64
dotnet build ConnectOnion.WinUIClient/ConnectOnion.WinUIClient.sln -c Release --no-restore -p:Platform=x64
dotnet test tests/ConnectOnion.Protocol.Tests/ConnectOnion.Protocol.Tests.csproj
dotnet test tests/ConnectOnion.WinUIClient.UnitTests/ConnectOnion.WinUIClient.UnitTests.csproj
dotnet test tests/ConnectOnion.IntegrationTests/ConnectOnion.IntegrationTests.csproj
```

For a local real-window smoke test, build unpackaged and point the test at the executable:

```powershell
$env:CONNECTONION_UI_TEST_EXE = (Resolve-Path 'ConnectOnion.WinUIClient/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/ConnectOnion.WinUIClient.exe')
dotnet test tests/ConnectOnion.WinUIClient.UITests/ConnectOnion.WinUIClient.UITests.csproj
```

For performance investigations, capture an ETW trace with Windows Performance Recorder while restoring a long conversation and scrolling/searching it. Compare UI-thread CPU, allocations, SQLite I/O, and working set against a fixed conversation fixture; do not accept timing results from a Debug build.

## Follow-up backlog

- Migrate remaining secondary settings/help/identity labels and runtime-generated strings into `.resw`, then add a second locale as a localization validation build.
- Add focused WinUI-host tests for converters, template selection, Markdown visual output, focus, drag/drop, tray, and notification activation.
- Add reconnect, shutdown-drain, and cold-activation race integration coverage.
- Establish measured budgets for cold start, 1,000-message restore, search latency, scrolling frame time, and steady-state memory before making further micro-optimizations.
