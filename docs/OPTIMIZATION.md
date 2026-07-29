# Engineering optimization notes

Last updated: 2026-07-16

## Implemented

- **Dependency governance:** package versions live in `Directory.Packages.props`; restore enables transitive pinning and NuGet auditing. `Directory.Build.props` enables deterministic builds and the current recommended .NET analyzers.
- **Layer boundary:** `ConnectOnion.WinUIClient.Core` owns WinUI-free models, SQLite repositories, notification policy, projections, and other testable services. The WinUI executable references Core, and ArchUnitNET tests prevent Core from taking a `Microsoft.UI` dependency or Data from depending on ViewModels.
- **Lifecycle and diagnostics:** the generic host is started before the window, stopped once on window/tray exit, and drains the run manager with bounded timeouts. Serilog writes structured rolling logs to `AppStorage.LogsDir` — `<data root>\logs`, so they follow the database instead of a fixed path (10 MB per file, 14 retained files). The teardown is instrumented end to end — exit funnel, page disarm outcome, minimize-to-tray, then each shutdown phase's duration — so a clean exit is distinguishable from a crash and from a minimize after the fact; see `docs/CONCURRENCY.md`.
- **Shutdown races:** pages disarm timers in `Unloaded`, which is not guaranteed to fire on window close, so `Views.IShutdownDisarmable` lets `MainWindow.DetachWindowServices` disarm the live page synchronously before the async host shutdown the dispatcher keeps pumping through.
- **Cancellation:** page unloading cancels conversation restore; cancellation tokens flow through view-model loading and SQLite reads/writes where supported.
- **Rendering:** `MarkdownTextBlock` caches its Markdig syntax tree. Search-highlight and theme rerenders no longer parse the same message again.
- **Resource lifecycle:** `ChatComposer` implements deterministic cleanup for speech recognition, AudioGraph nodes, Win2D stroke resources, timers, cancellation sources, and event subscriptions. Audio-meter startup observes cancellation after each asynchronous acquisition so page unload cannot leave a microphone graph behind.
- **Publish assets and exit:** unpackaged Release publish explicitly carries the `Assets` tree, and window icons resolve from an absolute application-base path. WebSocket shutdown has a 1.5-second hard budget, connections close concurrently, and structured logs record shutdown phase timings.
- **Shutdown ordering:** window activation, presence, and session callbacks detach before the Generic Host service provider is disposed. Closed callbacks are idempotent and use cached service references, preventing late WinUI events from resolving a disposed container.
- **Localization:** WinUI `.resw` resources and `x:Uid` are established under `Strings/en-US`; settings, shell menus, search/drop hints, chat approvals, composer suggestions/modes, offline state, the agent-icon menu items and Add Agent icon picker, and primary entry/empty states are migrated. Keep fallback XAML text when adding resources so development builds remain diagnosable. Strings assembled at runtime cannot carry an `x:Uid`, so they go through `Common/LocalizedStrings.Get(key, fallback)`, which takes the English text as a parameter for the same reason.
- **Analyzer hygiene:** a clean unpackaged x64 application build completes with zero compiler/analyzer warnings. CA1848 is enforced, so *every* logging call site uses a cached `LoggerMessage` delegate, and persisted values use explicit culture semantics.
- **Logging reaches the file three ways and no fourth:** constructor-injected `ILogger<T>` for container-created types, a `Configure(ILoggerFactory)` facade for the two `static` entry points in `Core` (`NotificationLog`, `IdentityStore`), and `AppServices.Logging` for framework-created code-behind. `Debug.WriteLine` is `[Conditional("DEBUG")]` and therefore never a failure path's only record — it appears only alongside a real logger call, or for development tracing (per-run-phase lines) that would be noise in a shipped log.
- **Testing:** the protocol fake server uses an in-process Kestrel WebSocket endpoint on an OS-assigned port, eliminating `HttpListener` ACL and port-reservation races. FlaUI provides an opt-in real-window shell smoke test.
- **CI:** restore audits vulnerable packages, test runs emit Cobertura coverage, Release publishes use ReadyToRun with trimming disabled pending trim-safe JSON coverage, and `workflow_dispatch` can opt into the interactive shell smoke test.

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

For launch time and steady-state memory, run `scripts/Measure-Performance.ps1` — see
`docs/PERFORMANCE.md` for the method, the budgets, and the current baseline.

For deeper performance investigations, capture an ETW trace with Windows Performance Recorder while restoring a long conversation and scrolling/searching it. Compare UI-thread CPU, allocations, SQLite I/O, and working set against a fixed conversation fixture; do not accept timing results from a Debug build.

## Follow-up backlog

- Migrate remaining secondary settings/help/identity labels and runtime-generated strings into `.resw`, then add a second locale as a localization validation build.
- Add focused WinUI-host tests for converters, template selection, Markdown visual output, focus, drag/drop, tray, and notification activation.
- Add reconnect, shutdown-drain, and cold-activation race integration coverage.
- Establish measured budgets for 1,000-message restore, search latency, and scrolling frame time. Cold start and steady-state memory are done — `scripts/Measure-Performance.ps1` measures them and `docs/PERFORMANCE.md` records the budgets and the current baseline.
