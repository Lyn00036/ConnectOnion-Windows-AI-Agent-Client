# ConnectOnion Desktop — Test Plan & Test Cases

> Version 1.1 · Branch `main` · Implementation status refreshed 2026-07-13 against working tree based on commit `79872fe`
> Every class, page, method, table, and constant cited below exists in this repository. Status markers describe the current working tree; planned files are explicitly labelled as planned.

---

## Table of Contents

1. [Document Goals](#1-document-goals)
2. [Project Analysis](#2-project-analysis-grounded-in-real-code)
3. [Test Scope](#3-test-scope)
4. [Out of Scope](#4-out-of-scope)
5. [Test Strategy](#5-test-strategy)
6. [Test Layers](#6-test-layers)
7. [Test Environment](#7-test-environment)
8. [Test Data](#8-test-data)
9. [Mock and Fake Strategy](#9-mock-and-fake-strategy)
10. [Per-Module Test Checklist](#10-per-module-test-checklist)
11. [Detailed Test Cases](#11-detailed-test-cases)
12. [End-to-End Flows](#12-end-to-end-flows)
13. [UI Automation Standards](#13-ui-automation-standards)
14. [Accessibility Testing](#14-accessibility-testing)
15. [Performance and Stability](#15-performance-and-stability)
16. [Regression Strategy](#16-regression-strategy)
17. [Defect Severity Levels](#17-defect-severity-levels)
18. [Test Pass Criteria](#18-test-pass-criteria)
19. [Release Gate Criteria](#19-release-gate-criteria)
20. [Test Directory Structure](#20-test-directory-structure)
21. [Implementation Priority](#21-implementation-priority)
22. [Appendix A — Design Gaps Surfaced by This Plan](#appendix-a--design-gaps-surfaced-by-this-plan)

---

> Optimization implementation refresh: 2026-07-16. The Core project,
> ArchUnitNET boundaries, Kestrel fake server, and opt-in FlaUI smoke test are
> implemented. See [OPTIMIZATION.md](./OPTIMIZATION.md) for the current state.

## 1. Document Goals

- Establish a **layered test architecture** for ConnectOnion Desktop — solving *testability* at the architectural level, not just piling up test cases.
- Produce a test case table usable **directly as a development backlog** (Test ID, priority, code location, automatable yes/no).
- Name the concrete gaps in the current test infrastructure and give a **phased rollout order**.
- Define release gates so that "can we ship?" becomes a decidable question.

---

## 2. Project Analysis (grounded in real code)

### 2.1 Main modules

Production projects sit at the repo root, test projects under `tests/`. Everything except the live smoke test is in the `.sln`:

| Project | In `.sln` | In CI | Responsibility |
|---|---|---|---|
| `ConnectOnion.WinUIClient` | ✅ | ✅ build | The WinUI 3 app: XAML, code-behind, window-bound services (`net10.0-windows10.0.19041.0`) |
| `ConnectOnion.WinUIClient.Core` | ✅ | ✅ via tests | Every WinUI-free surface of the client (models, repositories, notifications, projections, view models) → the headless test target |
| `ConnectOnion.Protocol` | ✅ | ✅ build | Transport-agnostic protocol library (`net10.0`), zero WinUI dependency → headless-testable |
| `ConnectOnion.Protocol.Conformance` | ✅ | ✅ **CI gate** | Cross-checks Ed25519 / canonical JSON against the Node.js `ref-sign.js` |
| `ConnectOnion.Protocol.LiveTest` | ❌ | ❌ | Manual smoke run against a real deployed agent |
| `tests/ConnectOnion.Protocol.Tests` | ✅ | ✅ | xunit unit tests for the wire protocol |
| `tests/ConnectOnion.WinUIClient.UnitTests` | ✅ | ✅ | Headless xunit tests over `Core`, plus the ArchUnitNET layer-boundary gate |
| `tests/ConnectOnion.IntegrationTests` | ✅ | ✅ | xunit tests against a real SQLite file (schema, migrations, repositories, identity) |
| `tests/ConnectOnion.WinUIClient.UITests` | ✅ | ⚠️ compile-only | FlaUI (UIA3) real-window shell smoke test; *runs* only on a `workflow_dispatch` opt-in |

The test projects can't reference the app project (`net10.0-windows` + Windows App SDK won't load in a headless test host), so they take a `ProjectReference` on `ConnectOnion.WinUIClient.Core`, which holds every WinUI-free surface of the client. **Moving a production file into `Core` is how it comes under test.** (This replaces the older `<Compile Include>` link lists — those are gone.)

### 2.2 Key pages and windows

- **Window**: `MainWindow` — the single top-level window with a custom title bar, split across partials:
  `MainWindow.xaml.cs` / `.FileMenu.cs` / `.EditMenu.cs` / `.ViewMenu.cs` / `.HelpMenu.cs` / `.DragDrop.cs` / `.Tray.cs` / `.Tray.Interop.cs` / `.Notifications.cs`
- **Pages** (inside `ContentFrame`): `HomePage`, `ChatPage` (+ `ChatPage.Find.cs`), `AgentDetailPage`, `SessionsPage`, `SettingsPage`
- **In-window modal overlays** (**not separate Windows, not ContentDialogs**):
  `SettingsOverlay`, `KeyboardShortcutsDialog`, `AboutOverlay` — all three share one shape: dimmed backdrop, centered card, Esc / backdrop / close-button dismissal, focus returned to the opener. Hosted as persistent singletons by `MainWindow`; `IsModalOverlayOpen` gates every global accelerator.
- **Custom controls**: `ChatComposer` (+`.Speech`), `ShellSidebar` (+`.Events`), `AddAgentForm`, `AgentAvatar`, `ToolActivityView`, `MarkdownTextBlock`, `HighlightedTextBlock`, `InAppNotificationHost`, `OfflineNoticeBar`, `IconText`, `SettingRow`, `IdentitySettingsContent`

### 2.3 Main ViewModels

Hand-rolled MVVM: `Common/ObservableObject.cs` + `Common/RelayCommand.cs` (no CommunityToolkit.Mvvm, no DI container).

| ViewModel | Key members (real signatures) |
|---|---|
| `ChatViewModel` (+`.StreamEvents`) | `Messages`, `LoadAsync()`, `SendAsync(text, attachments)`, `NewConversationAsync()`, `ReconnectAsync()`, `Retry()`, `Cleanup()`, `CanSend`, `CanReconnect`, `CanRetry`, `IsProcessing`, `IsConnecting`, `SubmitAskUserAnswerAsync`, `SubmitApprovalAsync`, `SubmitPlanReviewAsync` |
| `SessionsViewModel` | Session list, ordering, deletion |
| `SettingsViewModel` | Theme, font size, sidebar, microphone, notification settings |
| `AgentDetailViewModel` | Agent detail, `/info` capabilities |
| `KeyboardShortcutsViewModel` | `Groups`, `SearchText`, `IsEmpty`, `Reset()` |
| `PresenceAwareViewModel` | Online-presence base class |

### 2.4 Main Services

- **Runtime (core)** `Services/Runtime/`: `AgentSessionManager` (app-level owner of connections and turns; implements `IRunPersistence`), `AgentConnectionRegistry` (per-conversation WebSocket + idle eviction), `ChatTurnProjection` (**the single** stream-event → `ChatMessage` mapping), `ToolActivityProjector`, `ToolActivityMigration`, `AgentTurnExecutor`
- **Notifications** `Services/Notifications/`: `NotificationCoordinator` (the one decision point), `NotificationPolicy` (`Decide()` → Suppress / InApp / System), `DedupCache`, `WindowPresenceService`, `WindowsNotificationService`, `InAppNotificationService`, `ConversationNavigationService`, `NotificationActivationRouter`, `NotificationSettingsStore`, `ThreadingScheduler`
- **Attachments** `Services/Attachments/`: `AttachmentPickerService`, `AttachmentDropService`, `AttachmentValidationService`, `AttachmentEncoder`, `AttachmentImageCacheService`, `AttachmentIntake`
- **Others**: `AppServices` (static service locator / composition root), `AgentInfoService`, `AgentPresenceService`, `ConnectionTester`, `ThemeService`, `ClipboardService`, `MimeTypeResolver`, `ConversationCache` (LRU of 4 idle conversations), `AppVersionService`, `KeyboardShortcutCatalog`

### 2.5 Persistence

SQLite (`Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.provider.winsqlite3`), WAL journal, `foreign_keys = ON`.
Path: unpackaged → `%AppData%\ConnectOnion\connectonion.db`; packaged → MSIX LocalState.

**Tables**: `app_meta`, `agents`, `sessions`, `preferences`, `messages` (one row per rendered bubble, *not* a JSON blob), `message_attachments` (**metadata + local cache path only — never the base64 payload**), `executions`, `trace_events`, `identity_keys` (`private_seed` is a DPAPI-protected, base64-encoded blob).

**Migration status:** `SchemaMigrator` versions the database with `PRAGMA user_version`, applies ordered forward migrations transactionally, and rejects databases created by a newer app version. Schema evolution remains high-risk, so every migration must add an old-snapshot integration test (see §10.4 and `DB-MIGRATION-*`).

Write path: `ConversationRepository.UpsertMessagesAsync` is the **only** writer — incremental `INSERT ... ON CONFLICT(conversation_id, id) DO UPDATE`, with each written row's attachments replaced wholesale by a `message_id`-scoped delete + re-insert.

### 2.6 Networking

`ConnectOnion.Protocol.AgentConnectionService` (a C# port of the TS SDK's `remote-agent.ts`):

- Handshake `CONNECT` → `CONNECTED`; send `INPUT`; resolve on `OUTPUT`
- **Real timeout constants**: `ConnectTimeoutMs = 30_000`, `SilenceTimeoutMs = 60_000` (the 60s silence watchdog is the *only* bounded wait — `ask_user` may legitimately pend on a human indefinitely)
- Events: `StreamEvent`, `ConnectionLost`, `AskUserRequested`, `ApprovalRequested`, `PlanReviewRequested`, `OnboardRequired`
- Response API: `RespondAskUserAsync`, `RespondApprovalAsync(approved, scope)`, `RespondPlanReviewAsync`
- `EndpointResolver`: relay lookup, direct-endpoint probing, `/info` capability parsing
- Identity: `AgentIdentity` + `CanonicalJson` + `Hex` (Ed25519, verified byte-for-byte by the Conformance project)

### 2.7 Application lifecycle

- `Program.cs` — **custom entry point** (`DISABLE_XAML_GENERATED_MAIN`). `AppInstance.FindOrRegisterForKey("ConnectOnion.Main")`; a non-primary instance calls `RedirectActivationToAsync` and **exits before WinUI starts**, so a second `Application` is never constructed.
- `App.xaml.cs` — `OnLaunched` → `AppStorage.EnsureDirectories()` → `AppServices.InitializeNotifications()` → construct the one `MainWindow` → `HandleColdStartActivation()` → `FlushPendingActivation()`
- `App.ActivateMainWindow()` / `_pendingActivation` (`Interlocked`) — **cold-start race**: a redirected second launch can arrive *before* the main window exists; the request is buffered and replayed once the window is ready.
- `Shell/MainWindow.Tray.cs` — minimize-to-tray, `BringToForeground`, `RestoreFromTray`
- `AgentSessionManager.ShutdownAsync()` — drains in-flight runs within 8s, then releases all sockets

### 2.8 Existing tests

**Current automated baseline (2026-07-13): 247 passing tests** — 66 protocol, 139 headless WinUI-client unit tests, and 42 SQLite/Windows integration tests. `ConnectOnion.Protocol.Conformance` is an additional executable CI gate and is not included in the xunit count.

| Location | Content | Framework | In CI |
|---|---|---|---|
| `tests/ConnectOnion.Protocol.Tests/InputMessageBuilderTests.cs` | INPUT wire shape (text / images / files / mixed) | xunit 2.9.3 | ✅ |
| `tests/ConnectOnion.Protocol.Tests/DataUrlCodecTests.cs` | data-URL encode/decode + malformed input | xunit | ✅ |
| `tests/ConnectOnion.Protocol.Tests/AttachmentWireEventsTests.cs` | `agent_image` / `files_received` parsing | xunit | ✅ |
| `tests/ConnectOnion.Protocol.Tests/ConversationRunRegistryTests.cs` | Run registry state machine | xunit | ✅ |
| `tests/ConnectOnion.Protocol.Tests/AgentConnectionServiceTests.cs` | Handshake, input/output, ping/pong, timeout, watchdog pause, disconnect and disposal against `FakeAgentServer` | xunit | ✅ |
| `tests/ConnectOnion.WinUIClient.UnitTests/Runtime/` | `ChatTurnProjection`, `ToolActivityProjector`, legacy tool-message migration | xunit | ✅ |
| `tests/ConnectOnion.WinUIClient.UnitTests/Services/Notifications/` | `NotificationPolicy.Decide`, `NotificationCoordinator` (turn dedup, approval dedup, disconnect grace period), `DedupCache`, preview text | xunit | ✅ |
| `tests/ConnectOnion.WinUIClient.UnitTests/Services/` | Attachment validation/encoding, `ConversationCache`, keyboard shortcuts, MIME resolution | xunit | ✅ |
| `tests/ConnectOnion.WinUIClient.UnitTests/Models/`, `ViewModels/`, `Common/` | Attachment state/formatting, usage models, `ChatMessage`, preferences, notification models, shortcuts, grapheme-safe initials | xunit | ✅ |
| `tests/ConnectOnion.WinUIClient.UnitTests/Accessibility/` | Critical `AutomationId` XAML contract and Help-menu source contract | xunit | ✅ |
| `tests/ConnectOnion.IntegrationTests/Database/` | Schema + migrations, agent/session/conversation/usage/preferences repositories, `IdentityStore` reset detection, app version | xunit | ✅ |
| `tests/ConnectOnion.IntegrationTests/Runtime/` | `AgentConnectionRegistry` concurrency, conversation isolation, removal, LRU idle eviction and cleanup | xunit | ✅ |
| `ConnectOnion.Protocol.Conformance/Program.cs` | Signing / canonical JSON vs. Node reference | console asserts | ✅ **the signing gate** |

> **Note on frameworks**: the brief asks for MSTest, but the repo already standardizes on **xunit**. This plan uses **xunit** for all headless testing and MSTest *only* for the WinUI Unit Test App (whose template mandates it). Rationale in §6.

### 2.9 Missing tests

Closed since this plan was written: all headless test projects run in CI; database repositories and migrations have real-file integration coverage; `AgentConnectionService` now runs against `FakeAgentServer`; and 24 critical XAML controls have stable `AutomationId`s guarded by a static contract test. What remains open:

- Converters and `ChatMessageTemplateSelector` still need a WinUI-capable test host.
- `AgentSessionManager`, reconnect behavior, shutdown draining, and activation buffering need additional seams or integration coverage. `AgentConnectionRegistry` concurrency and idle eviction are covered.
- **No WinUI Unit Test App or FlaUI project exists yet**, so navigation, drag-and-drop, focus behavior, rendered Markdown, tray behavior, and the 15 end-to-end flows remain manual.
- Automation IDs cover the critical first slice, not the complete E2E locator set; remaining IDs are tracked in §13.1.

### 2.10 High-risk modules and flows

Ranked by *probability × blast radius*:

| # | Module | Risk |
|---|---|---|
| 🔴 1 | `AppDatabase` schema evolution | A migration framework now exists, but forward-only schema changes can still cause irreversible data loss without old-snapshot and rollback-on-failure coverage. |
| 🔴 2 | `AgentSessionManager` + `ConversationRunRegistry` | Cross-page ownership of connections and turns. In-flight runs during page close / conversation switch / app exit are a concurrency minefield. |
| 🔴 3 | `ConversationRepository.UpsertMessagesAsync` | The single write path, incremental upsert plus attachment delete+insert. A bug here permanently corrupts user data. |
| 🔴 4 | `ChatTurnProjection` | The single event→bubble mapping, **driven by both the live and the headless path**. Divergence between the two produces double-rendered bubbles. |
| 🟠 5 | `AgentConnectionService` timeouts / reconnect / dispose | 30s connect timeout, 60s silence watchdog, and unbounded `ask_user` interact in non-obvious ways. |
| 🟠 6 | `NotificationCoordinator` + `WindowPresenceService` | Decides whether to interrupt the user. False positives are user-visible spam. |
| 🟠 7 | Single-instance + cold-start activation race | The `Interlocked` buffer/replay only misbehaves under specific timing. |
| 🟡 8 | `WinUiMarkdownRenderer` | Already crashed in production over the `InlineUIContainer` constraint — direct evidence that this area has no test protection. |
| 🟡 9 | `ChatMessageTemplateSelector` | Resolved once per container; if a message kind ever becomes mutable, the row keeps the wrong template forever. |
| 🟡 10 | `IdentityStore` DPAPI | On decrypt failure it **regenerates the identity** rather than migrating — a silent identity loss. |

**High-risk user flows**: ① switching conversations mid-stream and returning ② reconnect after a drop ③ history restore after restart ④ background turn completion + notification ⑤ answering and persisting interactive cards (approval / ask_user).

---

## 3. Test Scope

- All of `ConnectOnion.Protocol` (wire format, connection state machine, identity signing, run registry)
- `ConnectOnion.WinUIClient`: ViewModels, Services, Data, Common (converters, selectors)
- WinUI controls, DataTemplates, overlays, focus and keyboard behavior
- SQLite persistence and restore
- WebSocket connection and its failure paths
- Application lifecycle (single instance, cold-start activation, tray, exit)
- Notification decisions and activation routing
- Accessibility semantics
- Performance and resource leaks

---

## 4. Out of Scope

- Browser compatibility, SEO, cookies, DOM selectors, cross-browser CSS, web responsive breakpoints — **all irrelevant to a desktop app**
- Correctness of the remote agent server itself (we test only how the *client* reacts, using a fake server)
- Windows' own notification delivery reliability (we test our registration, payloads, and routing)
- LLM answer quality (not decidable, not a test target)
- MSIX packaging and installation (not yet wired into CI — see CLAUDE.md "Not implemented yet")

---

## 5. Test Strategy

1. **Infrastructure before cases.** A test that isn't in CI protects nothing. Step zero — wiring every test project into the `.sln` and the workflow — is **done**; keep it that way by adding new test projects to both in the same commit that creates them.
2. **Push logic out of the UI.** The protocol layer already has zero WinUI dependency and is headless-testable. The WinUI layer should copy that: anything worth testing gets extracted into a pure class. `NotificationPolicy.Decide`, `ChatTurnProjection`, and `AppVersionService` are already good examples.
3. **Invert the pyramid by cost**: xunit headless (fast, stable, many) → WinUI Unit Test App (slow, needs a UI thread, few) → UI automation (slowest, most brittle, exactly 15 E2E flows).
4. **Failure paths before happy paths.** Somebody has manually clicked "send a message". Nobody manually tests reconnect-after-drop, background-run-survives-page-close, or restart-restores-history.
5. **Regressions follow defects.** Every fixed P0/P1 defect gets a regression case tagged with its issue number (§16).

---

## 6. Test Layers

| Layer | Framework | Environment | Target count | Runtime |
|---|---|---|---|---|
| **A. Unit (headless)** | **xunit** (matches the repo) | `net10.0`, no UI thread | ~180 | < 10s |
| **B. WinUI control tests** | **MSTest** (mandated by the WinUI Unit Test App template) | UI thread + XAML runtime | ~40 | < 90s |
| **C. Service / integration** | xunit + fake WebSocket server | headless | ~50 | < 60s |
| **D. Database** | xunit + temp `.db` file | headless | ~35 | < 30s |
| **E. UI automation / E2E** | **FlaUI** (recommended) or WinAppDriver/Appium | real process | 15 flows | < 10min |
| **F. Manual** | checklist | human | ~30 | — |

> **Why xunit at layer A but MSTest at layer B**: the WinUI 3 "Unit Test App" template supports only MSTest and must run as a packaged app to get a XAML runtime. Mixing is a platform constraint, not a design flaw. **The correct response is to push as much logic as possible down to layer A so you rarely pay layer B's cost.**

---

## 7. Test Environment

| Item | Value |
|---|---|
| OS | Windows 11 22H2+ (CI: `windows-latest`) |
| SDK | .NET 10 (`global.json` pins `10.0.100`, `rollForward: latestFeature`) |
| Platform | **Must be x64 / x86 / ARM64** — `Any CPU` does not work for the WinUI project |
| DB | Temp `.db` per test. **Never** point at `%AppData%\ConnectOnion\connectonion.db` |
| Fake agent | Local `HttpListener` + `WebSocket` (see §9) |
| DPI | 100% / 150% / 200% (UI and A11Y layers) |
| Theme | Light and Dark, both (control and A11Y layers) |

`.github/workflows/ci.yml` builds the `.sln` and then runs, in order:

```yaml
- run: dotnet test tests/ConnectOnion.Protocol.Tests/ConnectOnion.Protocol.Tests.csproj -c Release
- run: dotnet test tests/ConnectOnion.WinUIClient.UnitTests/ConnectOnion.WinUIClient.UnitTests.csproj -c Release
- run: dotnet test tests/ConnectOnion.IntegrationTests/ConnectOnion.IntegrationTests.csproj -c Release
- run: dotnet run --project ConnectOnion.Protocol.Conformance/... -c Release --no-build
```

Note the test steps pass no `-p:Platform`. The solution build maps the conformance project to `Any CPU`, so pushing `x64` through to a standalone `dotnet test`/`dotnet run` makes it look for output under `bin\x64\...` that the solution never produced.

---

## 8. Test Data

| Dataset | Contents | Used by |
|---|---|---|
| `agents.valid.json` | 3 agents: 0x address only, direct URL only, both | Agent management |
| `agents.invalid.json` | Empty name, empty address, malformed URL, duplicate address | Form validation |
| `conversation.100.json` | 100 messages (mixed user / agent / tool_activity) | List performance |
| `conversation.1000.json` | 1000 messages | Virtualization / scrolling |
| `markdown.torture.md` | Headings, bold/italic, strikethrough, inline code, fenced code, nested lists, quotes, tables, rules, escapes, **unclosed code fence**, very long lines, bare URLs | Markdown rendering |
| `stream.happy.jsonl` | thinking → tool_call → tool_result → assistant → OUTPUT | Streaming happy path |
| `stream.malformed.jsonl` | Out-of-order, duplicate `event_key`, empty payloads, unknown kinds | Streaming robustness |
| `stream.interactive.jsonl` | ask_user / approval_needed / plan_review | Interactive cards |
| `images/*` | 1KB, 5MB, over-limit, non-image with image extension | Attachment validation |
| `db/v0.db` | **Snapshot of today's schema** — the "old database" baseline for the day someone adds a column | Migration |

---

## 9. Mock and Fake Strategy

| Dependency | Strategy | Rationale |
|---|---|---|
| Remote agent WebSocket | **Fake server** (local `HttpListener` + `WebSocket`, scriptable responses) | Exercises the real protocol path; catches wire-format regressions a mocked interface would hide |
| `HttpClient` (`/info`, health probes) | `HttpMessageHandler` stub | No network needed |
| SQLite | **Real SQLite on a temp file** (do *not* mock) | The SQL *is* the thing under test; an in-memory fake cannot validate WAL/FK behavior |
| `WindowsNotificationService` | **Fake** implementing `INotificationAbstractions` | OS notifications are not assertable |
| `WindowPresenceService` | Fake (programmable focus / visibility / current conversation) | These are `NotificationPolicy`'s decision inputs |
| `DispatcherQueue` / `ThreadingScheduler` | Fake (execute synchronously) | Makes async UI orchestration headless-testable |
| `IChatWindow` | Fake (records `ShowConversationAsync` calls) | Asserts notification-click routing |
| DPAPI (`IdentityStore`) | Not mocked — real, against a temp DB | `CurrentUser` scope works on CI |
| `Launcher.LaunchUriAsync` | **Cannot be mocked (static WinRT)** → **requires refactor**: extract `IUriLauncher` | See UT-HELP-002 risk note |

> **Important**: `AppServices` is a **static service locator** — `AgentSessionManager` and friends are `new`'d during static initialization, so tests cannot inject fakes through it.
> **Good news**: `AgentSessionManager` already exposes `public AgentSessionManager(HttpClient, ConversationRepository)`. Tests should construct instances **directly** and bypass `AppServices` entirely. The repositories are the same. No large refactor required.

---

## 10. Per-Module Test Checklist

| § | Module | Coverage |
|---|---|---|
| 10.1 | Startup & lifecycle | First launch, existing data, DB init failure, second instance, cold-start activation replay, tray restore, exit with in-flight runs |
| 10.2 | Agent management | CRUD, empty state, validation (blank address, untested connection), deleting the selected agent, deleting a connected agent, avatar initial |
| 10.3 | Agent connection | Handshake, 30s timeout, 60s silence watchdog, user disconnect, abnormal drop, reconnect, duplicate connect, agent switch mid-connect, dispose |
| 10.4 | Database | Idempotent schema, incremental upsert, attachment delete+insert, message ordering, concurrent writes, FK cascade, **schema evolution (no migration → critical)**, corrupt file |
| 10.5 | Chat & streaming | Send validation, duplicate-send guard, event projection, interrupt/cancel/timeout, survives page switch, correct persistence, interactive card answers |
| 10.6 | Markdown | Full syntax, unclosed fences, search highlight (**known crash**), baseline alignment, both themes |
| 10.7 | Notifications | `NotificationPolicy.Decide` all branches, dedup, foreground suppression, click routing, cold-start activation |
| 10.8 | Window / navigation / keyboard / focus / a11y / performance | See §11 |

---

## 11. Detailed Test Cases

> Fields: ID · Title · Layer · Priority · Preconditions/Data · Steps · Expected · Automatable · Framework · Code location · Risk

### 11.1 Startup and lifecycle (LC)

| ID | Title | Layer | Pri | Preconditions / Data | Steps | Expected | Auto | Framework | Code location |
|---|---|---|---|---|---|---|---|---|---|
| **LC-START-001** | First launch creates DB + directories | D | **P0** | Delete `%AppData%\ConnectOnion` | Launch | Directories and `connectonion.db` created; all 10 tables present (9 baseline + `usage_events` from migration v2); WAL on; `foreign_keys=ON` | ✅ | xunit | `AppStorage.EnsureDirectories`, `AppDatabase.CreateSchemaAsync` |
| **LC-START-002** | Launch with existing data loses nothing | D | **P0** | Seed DB with 2 agents / 3 sessions | Launch | Schema creation is idempotent; all data readable | ✅ | xunit | `AppDatabase.EnsureInitializedAsync` |
| **LC-START-003** | DB init failure does not crash the app | C | **P0** | Make `.db` read-only / directory unwritable | Launch | A comprehensible error is surfaced; no silent crash | ✅ | xunit | `AppDatabase.OpenAsync` — **Risk: there is no try/catch today; this will likely crash. The expected result documents what *should* happen.** |
| **LC-INST-001** | Second instance redirects and exits | E | **P0** | App already running | Launch the exe again | Second process exits; no second window; existing window comes forward | ✅ | FlaUI + process assertions | `Program.Main`, `AppInstance.FindOrRegisterForKey` |
| **LC-INST-002** | Cold-start activation is buffered and replayed | A | **P1** | — | Call `App.ActivateMainWindow()` *before* the window exists, then create it and `FlushPendingActivation()` | Activation is honored exactly once, not dropped | ⚠️ partial | xunit (after extracting the buffer) | `App._pendingActivation` / `FlushPendingActivation` — **Risk: coupled to static `App`; extract `PendingActivationBuffer` to make it testable (INFRA-5)** |
| **LC-TRAY-001** | Tray click restores the window | E | P1 | Minimized to tray | Click tray icon | Window restores to foreground | ⚠️ | FlaUI | `Shell/MainWindow.Tray.cs` `RestoreFromTray` |
| **LC-EXIT-001** | Graceful shutdown drains in-flight runs | C | **P0** | A run is streaming | Call `ShutdownAsync()` | Completes within 8s; all sockets released; no unhandled exception | ✅ | xunit + fake server | `AgentSessionManager.ShutdownAsync` |
| **LC-EXIT-002** | Shutdown still releases when a run hangs | C | P1 | Fake server never replies | `ShutdownAsync()` | Returns after the 8s bound; sockets disposed anyway | ✅ | xunit | same |

### 11.2 Agent management (AGENT)

| ID | Title | Layer | Pri | Steps | Expected | Auto | Framework | Code location |
|---|---|---|---|---|---|---|---|---|
| **UT-AGENT-001** | Reject submit when address and direct URL are both empty | A | **P0** | Submit empty form | Shows "Enter a 0x address or Direct URL."; nothing written | ⚠️ (logic lives in code-behind) | MSTest (B), or xunit after extraction | `AddAgentForm.xaml.cs:96` |
| **UT-AGENT-002** | Reject add before a successful connection test | A | **P0** | Fill address, skip Test | Shows "Test connection successfully before adding this agent." | ⚠️ | same | `AddAgentForm.xaml.cs:105` |
| **UT-AGENT-003** | Blank name falls back to a default | A | P1 | Empty name, valid address | Name falls back to the address, else "Agent" | ✅ | xunit | `AddAgentForm.xaml.cs:176-177` |
| **DB-AGENT-001** | Agent create / read / delete | D | **P0** | CRUD | Round-trips correctly | ✅ | xunit | `AgentRepository` |
| **DB-AGENT-002** | Deleting an agent cascades to its sessions | D | **P0** | Delete an agent with conversations | `sessions` / `messages` stay consistent; no orphan rows (FK enforced) | ✅ | xunit | `AgentRepository` + FK |
| **UT-AGENT-004** | Delete an agent that is currently connected | C | **P0** | Agent has an active run | Run is cancelled, socket released, no dangling references | ✅ | xunit + fake | `AgentSessionManager.ReleaseConversationAsync`, `ConversationCache` invalidation |
| **UI-AGENT-001** | Empty state when no agents exist | B | P1 | Empty DB | HomePage shows the empty state and an add entry point | ⚠️ | MSTest (WinUI) | `HomePage.xaml` |
| **UT-AGENT-005** | Avatar initial generation | A | P2 | Names: `"connectonion"`, `""`, `"🙂x"` | Yields `C`, `?`, and a whole grapheme respectively | ✅ | xunit | `ChatViewModel.SelectedAgentInitial:136` — **Risk: `Name[..1]` splits surrogate pairs / emoji** |

### 11.3 Agent connection (CONN)

| ID | Title | Layer | Pri | Steps | Expected | Auto | Framework | Code location |
|---|---|---|---|---|---|---|---|---|
| **UT-CONN-001** | CONNECT → CONNECTED handshake | C | **P0** | Fake server responds normally | Connection ready; `IsConnecting` clears | ✅ | xunit + fake ws | `AgentConnectionService` |
| **UT-CONN-002** | 30s connect timeout | C | **P0** | Server accepts TCP but never sends CONNECTED | Times out at 30s; never hangs forever | ✅ (inject a controllable clock) | xunit | `ConnectTimeoutMs = 30_000` |
| **UT-CONN-003** | 60s silence watchdog fires | C | **P0** | Server goes silent after handshake | `ConnectionLost` raised at 60s | ✅ | xunit | `SilenceTimeoutMs = 60_000` |
| **UT-CONN-004** | Watchdog does **not** fire during `ask_user` | C | **P0** | Server sends ask_user then goes silent 5 min | Connection stays up (a human may take a while) | ✅ | xunit | `AgentConnectionService` — 🔴 **the behavior most likely to be "optimized" away by accident** |
| **UT-CONN-005** | Handshake failure (HTTP 4xx / malformed frame) | C | **P0** | Server returns garbage | Comprehensible exception; no leaked socket | ✅ | xunit | same |
| **UT-CONN-006** | Service unreachable | C | **P0** | Point at a closed port | Fails fast; UI can retry | ✅ | xunit | `ConnectionTester`, `EndpointResolver` |
| **UT-CONN-007** | User-initiated disconnect | C | P1 | Dispose | `ConnectionLost` is **not** raised (deliberate vs. abnormal must differ) | ✅ | xunit | `AgentConnectionService.DisposeAsync:543` |
| **UT-CONN-008** | Dispose is idempotent | C | P1 | `DisposeAsync()` ×3 | No exception | ✅ | xunit | same |
| **UT-CONN-009** | Rapid repeated Connect creates one socket | C | **P0** | Call `GetOrCreate` concurrently 20× | Exactly one connection instance and one factory invocation | ✅ implemented | xunit | `AgentConnectionRegistryTests.GetOrCreate_ConcurrentCallsForSameConversation_CreateOneConnection` |
| **UT-CONN-010** | Per-conversation connection isolation | C | **P0** | Create two conversation connections; remove one | Independent instances; removing one leaves the other registered | ✅ implemented | xunit | `AgentConnectionRegistryTests` |
| **UT-CONN-011** | Idle socket eviction | C | P1 | Create more than five idle connections with a controlled clock | Least-recently-used idle sockets are released; busy sockets are spared | ✅ implemented | xunit | `AgentConnectionRegistryTests.TrimIdleAsync_*` |
| **UT-CONN-012** | Switch agent while connecting | C | P1 | Navigate away mid-connect | Old connection cleaned up; no leak | ✅ | xunit | `ChatViewModel.Cleanup:63` |
| **E2E-CONN-001** | Manual reconnect after a drop | E | **P0** | See E2E-005 | — | ⚠️ | FlaUI | `ChatViewModel.ReconnectAsync:472` |

### 11.4 Database and persistence (DB)

| ID | Title | Layer | Pri | Steps | Expected | Auto | Framework | Code location |
|---|---|---|---|---|---|---|---|---|
| **DB-SCHEMA-001** | Schema creation is idempotent | D | **P0** | Init ×3 | No error; schema unchanged | ✅ | xunit | `CreateSchemaAsync` |
| **DB-MIGRATION-001** | **An old DB (v0 snapshot) opens under the current build** | D | **P0** | Initialize current code against the legacy schema fixture | No crash, no data loss; `user_version` advances | ✅ | xunit | `SchemaMigrationTests` exercises the real `SchemaMigrator` against a temporary SQLite file. |
| **DB-MIGRATION-002** | A DB missing a column fails loudly | D | P1 | Hand-craft a DB with a column removed | Explicit error, not silent data loss | ✅ | xunit | same |
| **DB-MSG-001** | Incremental upsert touches only the given rows | D | **P0** | 100 existing rows, upsert 3 | Only 3 rows written; others' `created_at` unchanged | ✅ | xunit | `ConversationRepository.UpsertMessagesAsync` |
| **DB-MSG-002** | `created_at` survives an update | D | **P0** | Upsert an existing id | `created_at` not overwritten (it is absent from the DO UPDATE list) | ✅ | xunit | same |
| **DB-MSG-003** | Message ordering is stable on `(conversation_id, id)` | D | **P0** | Upsert out of order | Read-back order is correct | ✅ | xunit | same |
| **DB-MSG-004** | Attachments replaced wholesale per `message_id` | D | **P0** | Agent bubble gains an image mid-turn | Old attachment rows deleted, new inserted, no duplicates | ✅ | xunit | same |
| **DB-MSG-005** | **Base64 payloads are never persisted** | D | **P0** | Receive `agent_image` | `message_attachments` holds only a local path + metadata | ✅ | xunit | `AttachmentImageCacheService` — **security + database-size risk** |
| **DB-MSG-006** | `GetNextMessageIdAsync` = MAX(id)+1 | D | P1 | Empty conversation / 100 rows | Returns 1 / 101 | ✅ | xunit | `ConversationRepository` |
| **DB-MSG-007** | `LoadLastAgentMessageAsync` returns the last agent row | D | P1 | Mixed roles | Correct row | ✅ | xunit | same |
| **DB-CONC-001** | Concurrent writes do not deadlock (WAL) | D | **P0** | 10 concurrent upserts | All succeed; no `database is locked` | ✅ | xunit | `AppDatabase` (WAL + shared cache) |
| **DB-FK-001** | Deleting a conversation leaves no orphan rows | D | **P0** | Delete a conversation | Attachments, messages, traces and executions are all gone; no orphans | ✅ | xunit | **There is no `ON DELETE CASCADE`.** Children are deleted explicitly, in order, before the `sessions` row (`ConversationRepository.DeleteConversationAsync` → `DeleteExecutionsAndTracesAsync` → `SessionRepository.SaveAsync`). Assert the *ordering contract*: doing it in the wrong order raises an FK violation that the repository `try/catch` swallows into a log line |
| **DB-USAGE-001** | Deleting a conversation does **not** erase its usage | D | **P0** | Record usage, then delete the conversation | `usage_events` rows survive; per-model totals are unchanged | ✅ | xunit | `usage_events` has no FK and is in no cascade — 🔴 **the ledger invariant: totals must never shrink because a user tidied their sidebar** |
| **DB-USAGE-002** | Usage insert is idempotent | D | **P0** | Persist the same run twice (retry / replay) | No double counting (`ON CONFLICT(id)` — the id is the server's event id) | ✅ | xunit | `UsageRepository.InsertAsync` |
| **DB-USAGE-003** | A failed / cancelled run still records its usage | C | **P0** | Run fails mid-turn after 2 LLM calls | Both calls appear in the ledger | ✅ | xunit | `AgentSessionManager.PersistFailedAsync` → `PersistUsageAsync` — a ledger that only counts successes does not add up |
| **UT-USAGE-001** | `UsageProjector` extracts one row per `llm_result` | A | **P0** | Turn using two models | Two rows, correct model attribution | ✅ | xunit | `UsageProjector.Extract` |
| **UT-USAGE-002** | Malformed / model-less / zero-token events are skipped | A | P1 | Bad frames | No rows, no exception | ✅ | xunit | same |
| **UT-USAGE-003** | Clear is the only deletion path | A | **P0** | — | No other code path deletes `usage_events` | ✅ code review + grep | `UsageRepository.ClearAsync` |
| **DB-CORRUPT-001** | Corrupt database file | D | P1 | Write garbage bytes | Comprehensible error; **must not silently wipe the DB** | ✅ | xunit | `AppDatabase.OpenAsync` |
| **DB-ID-001** | Identity seed is DPAPI-encrypted at rest | D | **P0** | Generate identity | `private_seed` is not plaintext; round-trips | ✅ | xunit | `IdentityStore` |
| **DB-ID-002** | Behavior when the seed cannot be decrypted | D | P1 | Tamper with the seed | **Today: a fresh identity is generated** (no migration) | ✅ | xunit | `IdentityStore` — 🔴 **A silent identity swap loses existing agent authorizations. Needs a product decision: warn the user?** |
| **DB-PREF-001** | Preferences round-trip (single row, `CHECK id = 1`) | D | P2 | Write twice | Still exactly one row | ✅ | xunit | `PreferencesRepository` |

### 11.5 Chat and sending (CHAT)

| ID | Title | Layer | Pri | Data | Expected | Auto | Framework | Code location |
|---|---|---|---|---|---|---|---|---|
| **UT-CHAT-001** | Empty message is not sent | A | **P0** | `""` | `SendMessageAsync` not called; no bubble | ✅ | xunit | `ChatViewModel.SendAsync:292` |
| **UT-CHAT-002** | Whitespace-only message is not sent | A | **P0** | `"   \t\n"` | Same | ✅ | xunit | same |
| **UT-CHAT-003** | `CanSend` gate | A | **P0** | All combinations of `HasAgent` / `IsOnline` / `IsProcessing` / `IsConnecting` | Full truth table (4 inputs → 16 rows) | ✅ | xunit | `ChatViewModel.CanSend:112` |
| **UT-CHAT-004** | Send is rejected while processing | A | **P0** | `IsProcessing = true` | Second send ignored | ✅ | xunit | same |
| **UT-CHAT-005** | Rapid repeated send guard | C | **P0** | 10 clicks in 100ms | Exactly one run | ✅ | xunit | `AgentSessionManager.SendMessageAsync:79` |
| **UT-CHAT-006** | Send while disconnected | A | **P0** | Offline | Rejected with a message; no exception | ✅ | xunit | `CanSend` |
| **UT-CHAT-007** | User message appears instantly and persists | C | **P0** | Normal send | Bubble is immediate; user row written before the reply | ✅ | xunit | `AgentSessionManager` (persists the user message on send) |
| **UT-CHAT-008** | Unicode / Chinese / emoji / multiline / very long | A | P1 | See test data | Preserved verbatim; not truncated or mangled | ✅ | xunit | `InputMessageBuilder` |
| **UT-CHAT-009** | `CanRetry` / `ShowRetryBar` logic | A | P1 | After a failure | Retry bar appears and is mutually exclusive with the offline notice | ✅ | xunit | `ChatViewModel:119,133` |
| **UI-CHAT-001** | Composer clears and keeps focus after send | B | P1 | — | Focus remains in the input | ⚠️ | MSTest (WinUI) | `ChatComposer.xaml.cs` |
| **UI-CHAT-002** | Disclosure bubbles animate open and closed | B | P1 | Thought process, interactive cards, tool activity and tool steps | 140 ms fade/vertical transition; header is guarded against repeat clicks; OS animation preference is respected | ⚠️ manual | WinUI | `DisclosureAnimation` |

### 11.6 Streaming and projection (STREAM)

| ID | Title | Layer | Pri | Expected | Auto | Framework | Code location |
|---|---|---|---|---|---|---|---|
| **UT-STREAM-001** | thinking / tool / assistant events project to the right bubbles | A | **P0** | `EventKind` maps 1:1 to a template; Thinking is collapsed and loading until assistant output, then reaches a terminal state on success/failure/cancel | ✅ | xunit | `ChatTurnProjection.Apply`, `ChatMessageTests` |
| **UT-STREAM-002** | **Live and headless paths produce identical bubbles** | A | **P0** | Same event sequence through an `ObservableCollection` target and a plain `List` target yields **identical** output | ✅ | xunit | `ChatTurnProjection` — 🔴 **the core invariant that prevents double-rendering when a page opens mid-turn** |
| **UT-STREAM-003** | Final reply is de-duplicated | A | **P0** | `AppendFinalReply` adds nothing when it equals the last assistant bubble | ✅ | xunit | `ChatTurnProjection.AppendFinalReply` |
| **UT-STREAM-004** | Repeated `event_key` merges instead of appending | A | **P0** | running → done updates the same row | ✅ | xunit | `ChatTurnProjection` |
| **UT-STREAM-005** | Unknown event kinds are safely ignored | A | P1 | No crash, no blank bubble | ✅ | xunit | same |
| **UT-STREAM-006** | Out-of-order / empty-payload events | A | P1 | No exception | ✅ | xunit | same |
| **UT-STREAM-007** | Cancelled run ends in Cancelled state | C | **P0** | `CompleteToolActivity(Cancelled)`; persisted status correct | ✅ | xunit | `AgentSessionManager.PersistFailedAsync:260` |
| **UT-STREAM-008** | Failed run keeps generated content and appends an error bubble | C | **P0** | `[connection error] …` bubble present; earlier bubbles retained | ✅ | xunit | `PersistFailedAsync:278-287` |
| **UT-STREAM-009** | **Run survives page close and still persists** | C | **P0** | With no page subscribed, the run completes and history is correct | ✅ | xunit | `AgentSessionManager` + `ConversationRunRegistry` — 🔴 core architectural promise |
| **UT-STREAM-010** | Page opened mid-turn: load history, replay live events, no double render | C | **P0** | Bubble count is correct | ✅ | xunit | `MarkHistoryPersisted` / `IsRunHistoryPersisted:155` |
| **UT-STREAM-011** | Interactive card answers persist (approve / reject) | C | **P0** | `EventMeta` = "Approved once" / "Rejected"; status correct | ✅ | xunit | `RecordInteractiveAnswer:73` + `ResolveInteractiveCards:357` |
| **UT-STREAM-012** | Unanswered interactive cards are sealed as "No selection" | C | **P0** | Reloaded history shows "No selection", not dead controls | ✅ | xunit | `NoSelectionMeta` (`AgentSessionManager:50`) |
| **UT-STREAM-013** | Interactive card default collapse state | A | P1 | Resolved → collapsed; awaiting → expanded | ✅ | xunit | `ChatMessage.IsInteractiveExpanded` |
| **UT-STREAM-014** | 1000 events in one turn, nothing dropped | C | P1 | All projected, in order | ✅ | xunit | `ConversationRun` |
| **UT-STREAM-015** | Graceful stop uses the latest wire protocol | B | **P0** | Existing socket receives exactly `{ "type": "INTERRUPT" }`; ordinary OUTPUT still completes the turn | ✅ | xunit | `AgentConnectionServiceTests.SendInterruptAsync_*` |
| **UT-STREAM-016** | Stop during approval uses the FIFO-safe hard rejection | B | **P0** | Exactly `approved=false`, `mode=reject_hard`, `feedback=用户中断`; no `type`/`scope` fields | ✅ | xunit | `AgentConnectionServiceTests.RejectApprovalForStopAsync_*` |
| **UT-STREAM-017** | Interrupted OUTPUT is marked without an extra activity bubble | A | **P0** | Final agent message has `EventMeta=Stopped`; failed interrupt send leaves it unmarked | ✅ | xunit | `ChatTurnProjectionTests.AppendCompletedTurn_AfterInterrupt*` |
| **UI-STREAM-001** | Stop enters an idempotent disabled intermediate state | B | **P0** | Button is visible only after INPUT, disabled while `IsStopping`, and disappears on OUTPUT/ERROR/disconnect | ⚠️ | MSTest (WinUI) | `ChatComposer.CanStop`, `ChatViewModel.ApplyRunSnapshot` |

### 11.7 Conversation management (SESSION)

| ID | Title | Layer | Pri | Expected | Auto | Code location |
|---|---|---|---|---|---|---|
| **DB-SESSION-001** | Create / rename / delete a conversation | D | **P0** | Persists correctly | ✅ | `SessionRepository` |
| **DB-SESSION-002** | List ordered by `updated_at DESC` | D | P1 | Index `ix_sessions_agent_updated` is used | ✅ | same |
| **UT-SESSION-003** | Delete a conversation that is mid-reply | C | **P0** | Run cancelled, cache invalidated, no residual socket | ✅ | `AgentSessionManager.ReleaseConversationAsync:180` |
| **UT-SESSION-004** | `ConversationCache` caches only idle conversations | A | **P0** | A conversation with an active run **never** reads from cache (it must load authoritative history) | ✅ | `ConversationCache` — 🔴 violating this shows stale history |
| **UT-SESSION-005** | LRU cap of 4; invalidated on delete | A | P1 | The 5th evicts the oldest; deleting a session/agent clears it | ✅ | `ConversationCache` |
| **UT-SESSION-006** | Conversations are isolated per agent | D | P1 | Not visible across agents | ✅ | `SessionRepository` |
| **E2E-SESSION-001** | History restored after restart | E | **P0** | See E2E-006 | ⚠️ | — |

### 11.8 Markdown rendering (MD)

| ID | Title | Layer | Pri | Expected | Auto | Code location |
|---|---|---|---|---|---|---|
| **UI-MD-001** | Full syntax renders (headings, bold/italic, strikethrough, lists, quotes, tables, rules) | B | P1 | Correct element types | ⚠️ | MSTest (WinUI) · `WinUiMarkdownRenderer` |
| **UI-MD-002** | Unclosed code fence does not crash | B | **P0** | Degrades gracefully | ⚠️ | `MarkdownTextBlock.RenderMarkdown:123` (fallback paragraph exists) |
| **UI-MD-003** | **Search highlight inside inline code does not crash** | B | **P0** | No `ArgumentException` | ⚠️ | 🔴 **Regression case (this actually happened)**: `InlineUIContainer` is legal only inside a `RichTextBlock` paragraph · `WinUiMarkdownRenderer.WriteCodeInline` |
| **UI-MD-004** | Search highlight inside a fenced code block does not crash | B | **P0** | Same | ⚠️ | `WriteCodeBlockLines` |
| **UI-MD-005** | Search highlight inside a link / bare URL does not crash | B | **P0** | Falls back to the bold+underline path | ⚠️ | `WriteLink` + `_uiContainerBlockedDepth` |
| **UI-MD-006** | `` [`code`](url) `` does not crash | B | P1 | Inline code degrades to a monospace `Span` | ⚠️ | `WriteCodeInline` (`!CanHostUIContainer` branch) |
| **UI-MD-007** | Highlight match count equals the number of highlights | A | **P0** | The fallback path must still increment `matchIndex`, or Ctrl+F's "n of m" desyncs | ✅ | `HighlightedTextBlock.AddHighlightedRuns` |
| **UI-MD-008** | Code blocks and highlights sit on the text baseline | F | P2 | No upward float | ❌ manual/visual | `ApplyBaselineNudge` — **Risk: 0.22 is an empirical ratio, not font metrics** |
| **UI-MD-009** | Correct colors in both themes | B | P1 | Brushes read fresh from `Application.Current.Resources`; no stale colors after a theme flip | ⚠️ | `ResolveBrush` |
| **UI-MD-010** | Table presentation and alignment | B | P1 | Cells with default/repeated parser column indices advance sequentially without overlap; header has distinct surface/weight; `:---`, `:---:`, `---:` align left/center/right; long cells wrap at a bounded width | ❌ manual/visual | `WinUiMarkdownRenderer.WriteTable` |
| **UI-MD-011** | Wide table scrolling does not break message-list scrolling | B | **P0** | Table owns horizontal overflow only; mouse wheel and touch continue to scroll the outer virtualized message list vertically | ❌ manual/visual | `WriteTable` horizontal-only `ScrollViewer` |
| **UI-MD-012** | Fenced code language header | B | P2 | A language info string renders as a compact themed header; blocks without a language keep the simpler layout | ❌ manual/visual | `WriteCodeBlockLines` |
| **PERF-MD-001** | Large markdown (10k lines) render time | E | P2 | < 500ms | ✅ | performance test |

### 11.9 Tool calls (TOOL)

| ID | Title | Layer | Pri | Expected | Auto | Code location |
|---|---|---|---|---|---|---|
| **UT-TOOL-001** | start / running / success / failure / cancel state machine | A | **P0** | `ToolActivityStatus` transitions are correct | ✅ | `ToolActivityProjector` |
| **UT-TOOL-002** | Consecutive tool calls collapse into one timeline | A | P1 | One `tool_activity` bubble with multiple steps | ✅ | same |
| **UT-TOOL-003** | Failed migration rows fall back to the Empty template | A | P1 | No crash; renders a blank row | ✅ | `ToolActivityMigration` + `ChatMessageTemplateSelector.Empty` |
| **UT-TOOL-004** | `EventKind` → template mapping | A | **P0** | user / agent / activity / tool_activity / ask_user / approval / plan_review / other → Empty | ✅ | `ChatMessageTemplateSelector.SelectTemplateCore` |
| **UT-TOOL-005** | **Template is resolved exactly once per container** | A | **P0** | `Role` / `EventKind` / `ToolActivity` must not change after a message enters the list | ✅ | `ChatMessageTemplateSelector` (its class comment is the contract) — 🔴 violating it locks in the wrong template forever |

### 11.10 Overlays: Settings / About / Keyboard Shortcuts (OVL)

| ID | Title | Layer | Pri | Expected | Auto | Code location |
|---|---|---|---|---|---|---|
| **UI-OVL-001** | All three overlays close on Esc | B | **P0** | `OverlayRoot_KeyDown` handles Escape | ⚠️ | `SettingsOverlay` / `KeyboardShortcutsDialog` / `AboutOverlay` |
| **UI-OVL-002** | Backdrop click closes; card click does not | B | P1 | `Backdrop_Tapped` vs. `ModalContainer_Tapped` (`e.Handled = true`) | ⚠️ | same |
| **UI-OVL-003** | Re-invoking never opens a second instance | B | **P0** | When `IsOpen`, `Show()` only re-focuses | ⚠️ | all three `Show()` |
| **UI-OVL-004** | Focus returns to the opener (`HelpMenuBarItem`) on close | B | **P0** | Focus lands back on the menu item | ⚠️ | `_focusReturnTarget` |
| **UI-OVL-005** | Global accelerators are inert while an overlay is open | B | **P0** | `IsModalOverlayOpen` short-circuits the File/Edit/View/Help KeyDown handlers | ⚠️ | `MainWindow.xaml.cs:206` |
| **UI-OVL-006** | Opening an overlay does not disturb background runs | C | **P0** | Streaming continues; connections stay up | ✅ | architectural assertion (overlays are a pure UI layer) |
| **UI-ABOUT-001** | Shows app name, description, icon, copyright | B | P1 | Copy is correct and says "ConnectOnion" | ⚠️ | `AboutOverlay.xaml` |
| **UT-ABOUT-002** | **Version is not hard-coded** | A | **P0** | Packaged → `Package.Current.Id.Version`; unpackaged → `AssemblyInformationalVersion` with any `+sha` stripped; fallback `1.0.0` | ✅ | xunit · `AppVersionService.ResolveDisplayVersion` |
| **UT-ABOUT-003** | Copyright year is taken from the clock | A | P2 | `© {DateTime.Now.Year} ConnectOnion` | ✅ | `AppVersionService.CopyrightText` |
| **UI-ABOUT-004** | High DPI (150% / 200%): text is not clipped | F | P2 | Layout adapts | ❌ manual | `AboutOverlay.xaml` (ScrollViewer backstop) |
| **UT-HELP-001** | Help menu contains Keyboard shortcuts / ConnectOnion Docs / About | B | P1 | All three present, in order | ⚠️ | `MainWindow.xaml` `HelpMenuBarItem` |
| **UT-HELP-002** | Docs opens the correct URL | A | **P0** | `https://docs.connectonion.com/` | ⚠️ | `Shell/MainWindow.HelpMenu.cs` `DocsUrl` — **Risk: `Launcher.LaunchUriAsync` is static WinRT and cannot be mocked. Extract `IUriLauncher` (INFRA-4) to automate this.** |
| **UT-HELP-003** | A failed Docs launch surfaces an error toast (never silent) | A | P1 | `NotificationType.Error` toast | ⚠️ | same (`launched == false` branch) |
| **UT-HELP-004** | Docs and About are separate commands | A | P2 | Two independent handlers; no shared event branching on text | ✅ code review | `Shell/MainWindow.HelpMenu.cs` |
| **UT-KBD-001** | The shortcut catalog matches real handlers | A | **P0** | Every entry in `KeyboardShortcutCatalog` has a corresponding KeyDown handler in `MainWindow.*Menu.cs` | ✅ | xunit (reflection / string comparison) — the file's own comment demands this |
| **UT-KBD-002** | Ctrl+Shift+/ opens the shortcuts window | B | P1 | VK_OEM_2 (191) + Ctrl + Shift | ⚠️ | `Shell/MainWindow.HelpMenu.cs` `VkOemSlash` — **Risk: the OEM key code differs on non-US layouts** |
| **UI-KBD-003** | Shortcut search filters | A | P1 | Filters by keyword; empty state when no results | ✅ | `KeyboardShortcutsViewModel.IsEmpty` |
| **UI-SET-001** | Settings persist across restart | D | **P0** | Theme / font size / sidebar written to `preferences` | ✅ | `PreferencesRepository` + `SettingsViewModel` |
| **UI-SET-002** | Theme switch applies immediately with no stale colors | B | P1 | Converters and the renderer read brushes fresh each time | ⚠️ | `ThemeService`, `Brushes.xaml` |

### 11.11 Notifications (NOTIF)

| ID | Title | Layer | Pri | Expected | Auto | Code location |
|---|---|---|---|---|---|---|
| **UT-NOTIF-001** | Globally disabled → Suppress | A | **P0** | `Suppress("notifications disabled")` | ✅ | `NotificationPolicy.Decide:19` |
| **UT-NOTIF-002** | Type disabled → Suppress | A | **P0** | `Suppress("type disabled")` | ✅ | `:22` |
| **UT-NOTIF-003** | Already viewing that conversation → Suppress | A | **P0** | `Suppress("viewing target conversation")` | ✅ | `:33` |
| **UT-NOTIF-004** | Already viewing the approval → Suppress | A | **P0** | `Suppress("viewing approval")` | ✅ | `:29` |
| **UT-NOTIF-005** | Foreground but on another view → in-app toast | A | **P0** | `NotificationChannel.InApp` | ✅ | `:41` |
| **UT-NOTIF-006** | Background → system notification | A | **P0** | `NotificationChannel.System` | ✅ | `:44` |
| **UT-NOTIF-007** | Dedup cache suppresses repeats | A | P1 | The same run notifies once | ✅ | `DedupCache` |
| **UT-NOTIF-008** | Click activates the window and opens the right conversation | C | **P0** | `IChatWindow.ShowConversationAsync(agentId, conversationId)` invoked | ✅ | `ConversationNavigationService` + `Shell/MainWindow.Notifications.cs` |
| **UT-NOTIF-009** | Target conversation was deleted → falls back to HomePage | C | P1 | No crash | ✅ | `MainWindow.ShowConversationAsync:47` |
| **UT-NOTIF-010** | Cold-start notification click | C | **P0** | `HandleColdStart(arguments)` buffers until the window is ready | ✅ | `App.HandleColdStartActivation` + `NotificationActivationRouter` |
| **UT-NOTIF-011** | Notification registration failure does not block startup | C | P1 | `NotificationLog.Warn` records it; the app continues | ✅ | `App.HandleColdStartActivation` try/catch |
| **UT-NOTIF-012** | Invalid notification arguments | A | P1 | Ignored, no crash | ✅ | `NotificationActivationRouter` |
| **F-NOTIF-013** | Focus Assist / notifications denied at OS level | F | P2 | App does not crash; the in-app channel still works | ❌ manual | — |

### 11.12 Window and navigation (NAV)

| ID | Title | Layer | Pri | Expected | Auto | Code location |
|---|---|---|---|---|---|---|
| **UI-NAV-001** | Back/Forward enablement after navigation | B | P1 | `UpdateNavigationButtons` | ⚠️ | `MainWindow.xaml.cs:209` |
| **UI-NAV-002** | Navigation closes the Find overlay | B | P2 | `CloseFindOverlay()` | ⚠️ | `MainWindow.xaml.cs:43` |
| **UI-NAV-003** | `forceReload` pops the stale BackStack entry (leak guard) | A | **P0** | The old page instance becomes collectable | ⚠️ | `MainWindow.NavigateTo:232` |
| **UI-NAV-004** | Navigation does not cancel an in-flight run | C | **P0** | The run continues | ✅ | architectural assertion |
| **UI-NAV-005** | Rapid repeated navigation does not crash | E | P1 | 10 fast page switches | ⚠️ | FlaUI |
| **UI-NAV-006** | Custom title bar drag region works | F | P2 | The window can be dragged | ❌ manual | `TitleBarDragRegion` |
| **UI-NAV-007** | Multi-monitor / DPI change | F | P2 | Layout stays correct | ❌ manual | — |
| **UI-FIND-001** | Ctrl+F opens Find; `IFindHost` pages respond | B | P1 | Match count correct; next/prev works | ⚠️ | `Shell/MainWindow.ViewMenu.cs` + `ChatPage.Find.cs` |

### 11.13 Keyboard and focus (FOCUS)

| ID | Title | Layer | Pri | Expected | Auto |
|---|---|---|---|---|---|
| **A11Y-FOCUS-001** | Initial focus on overlay open | B | **P0** | Settings → first control; Shortcuts → SearchBox; About → OkButton | ⚠️ MSTest |
| **A11Y-FOCUS-002** | Focus returns to the opener on close | B | **P0** | See UI-OVL-004 | ⚠️ |
| **A11Y-FOCUS-003** | Tab / Shift+Tab order is sane and skips hidden controls | B | P1 | Collapsed regions are not tabbable | ⚠️ |
| **A11Y-FOCUS-004** | Disabled controls do not take focus | B | P1 | — | ⚠️ |
| **A11Y-FOCUS-005** | Focus visuals are visible (including restyled buttons) | F | P1 | `FocusVisualPrimaryBrush` applies | ❌ manual |
| **E2E-015** | **Full send-message flow using only the keyboard** | E | **P0** | See §12 | ⚠️ FlaUI |

### 11.14 Accessibility (A11Y)

| ID | Title | Layer | Pri | Expected | Auto |
|---|---|---|---|---|---|
| **A11Y-ID-001** | **Key controls carry an `AutomationId`** | B | **P0** | Per the table in §13.1 | ✅ partial | 24 critical IDs exist and are guarded by `AutomationContractTests`; complete the remaining E2E locators before adding FlaUI |
| **A11Y-NAME-001** | Icon-only buttons carry `AutomationProperties.Name` | B | **P0** | 48 exist; all icon buttons must be covered | ✅ static scan |
| **A11Y-NAME-002** | Interactive card headers announce their state | B | P1 | `InteractiveAccessibilityName` includes eyebrow, title, outcome, expanded/collapsed | ⚠️ |
| **A11Y-NAME-003** | Keycaps are not double-announced | B | P2 | Keycaps are `AccessibilityView=Raw`; the group name carries the full combo | ⚠️ | `KeyboardShortcutsDialog.xaml` |
| **A11Y-COLOR-001** | Color is not the only carrier of meaning | F | P1 | Approve/reject also show text ("Approved" / "Rejected") | ❌ manual |
| **A11Y-CONTRAST-001** | Text contrast ≥ WCAG AA | F | P1 | Both themes | ❌ Accessibility Insights |
| **A11Y-HC-001** | High-contrast mode is usable | F | P2 | — | ❌ manual |
| **A11Y-SCALE-001** | 200% text scaling does not clip | F | P1 | — | ❌ manual |
| **A11Y-LIVE-001** | State changes are announced | B | P2 | Connection status, error messages | ⚠️ |

### 11.15 Error handling (ERR)

| ID | Title | Layer | Pri | Expected | Auto |
|---|---|---|---|---|---|
| **UT-ERR-001** | Network errors are shown and recoverable | C | **P0** | Error bubble + retry bar; UI stays interactive | ✅ |
| **UT-ERR-002** | WebSocket exceptions are not swallowed | C | **P0** | `ConnectionLost` fires | ✅ |
| **UT-ERR-003** | Database errors are not silent | D | **P0** | Explicit exception | ✅ |
| **UT-ERR-004** | Markdown render errors degrade | B | **P0** | Fallback paragraph (`RenderMarkdown:123`) | ⚠️ |
| **UT-ERR-005** | External-link failure is surfaced | A | P1 | See UT-HELP-003 | ⚠️ |
| **UT-ERR-006** | Errors leak no sensitive data | A | **P0** | No private seed, invite code, or full token in any message | ✅ assertions + code review — 🔴 security |
| **UT-ERR-007** | One bad image does not fail the whole persist | C | P1 | `Task.WhenAll(PendingImages)` is wrapped in try/catch | ✅ | `AgentSessionManager:228` |

---

## 12. End-to-End Flows

> Conventions: all E2E use **FlaUI** and locate elements by `AutomationId`. **No `Thread.Sleep`** — condition waits only (§13.3).
> On failure, always collect: app logs, a copy of `connectonion.db`, the fake server's interaction log, a screenshot at failure, and a UIA tree dump.

| ID | Flow | Preconditions | Steps | Expected | Automation difficulty | Stability measures |
|---|---|---|---|---|---|---|
| **E2E-001** | First launch and add an agent | Empty `%AppData%\ConnectOnion` | Launch → `AddAgentButton` → fill `AgentAddressInput` → Test → Submit | Agent appears in `AgentList`; one row in `agents` | Connection test depends on the network | Point at the fake server; wait for the success status text before submitting |
| **E2E-002** | Connect and send a message | An agent exists | Select agent → new conversation → type → `SendMessageButton` | User bubble appears immediately; reply arrives | Connect time varies | Wait for `ConnectionStatus` = online; wait for `MessageList` count +2 |
| **E2E-003** | Receive a streamed reply | Same | Fake server emits thinking → tool → assistant → OUTPUT | Bubbles appear in order; final content persists | Event timing | Wait for the terminal state (`StopResponseButton` disappears) |
| **E2E-004** | Switch conversation mid-stream and return | A stream is in flight | Switch away → wait 3s → switch back | **No double rendering**; content complete and correctly ordered | Race between DB load and live replay | Assert bubble count == DB row count — 🔴 **the highest-value E2E in this suite** |
| **E2E-005** | Reconnect after a drop | Connected | Fake server drops → click reconnect | Offline notice → reconnect succeeds → sending works again | Drop timing | Wait on `ConnectionStatus` text transitions |
| **E2E-006** | History restored after restart | 2 conversations with messages | Close → relaunch → open a conversation | Content, order, and roles identical; interactive cards show their resolved outcome | Process restart | After relaunch, wait for `MessageList` to appear |
| **E2E-007** | Notification after a background reply | Window minimized | Send → minimize → wait for completion | System notification raised (asserted through the fake channel) | OS notifications are not directly assertable | Assert on the fake `INotificationAbstractions` |
| **E2E-008** | Notification click opens the right conversation | Same | Trigger activation | Window comes forward; navigates to that conversation | Cold and warm start are different paths | Test both; assert `ShowConversationAsync` arguments |
| **E2E-009** | Open and close Settings | — | Ctrl+, → change theme → Esc | Setting applies and persists; focus returns | — | Wait for `SettingsOverlay` visible/hidden |
| **E2E-010** | Open About | — | Help → About ConnectOnion | Shows name, version, copyright; Esc / OK / close all behave identically | — | Wait for `AboutOverlay` |
| **E2E-011** | Open Keyboard Shortcuts | — | Ctrl+Shift+/ | Window opens; search works; Esc closes | Keyboard layout | Cover the menu-click path too |
| **E2E-012** | Open the docs via ConnectOnion Docs | — | Help → ConnectOnion Docs | Default browser opens `docs.connectonion.com` | **The external browser cannot be asserted** | Inject an `IUriLauncher` fake and assert the URL (needs INFRA-4) |
| **E2E-013** | Second instance activates the existing window | App running | Launch the exe again | No second window; existing one comes forward; the second process exits | Process timing | Poll the process count and window handle |
| **E2E-014** | Long conversation scrolling and virtualization | `conversation.1000.json` | Open and scroll to the bottom | Smooth scrolling; memory does not grow linearly with scroll | Performance assertions are flaky | Sample repeatedly, take the median |
| **E2E-015** | **Complete a send using only the keyboard** | Connected | Tab / Enter / accelerators only | The message sends; focus is visible throughout | Focus order | Assert the `AutomationId` of `FocusedElement` at each step |

---

## 13. UI Automation Standards

### 13.1 `AutomationId` implementation status

The first **INFRA-1** slice is complete: 24 IDs exist and `Accessibility/AutomationContractTests.cs` prevents accidental removal. The remaining rows are required before the corresponding FlaUI flows can be implemented.

| AutomationId | Location | Status |
|---|---|---|
| `MainWindow` | `MainWindow.xaml` root | ✅ |
| `AgentList` | `ShellSidebar` agent `ItemsRepeater` | ✅ |
| `AddAgentButton` | `ShellSidebar.xaml` | ✅ |
| `AgentAddressInput` / `AgentDirectUrlInput` | `AddAgentForm.xaml` | ✅ |
| `TestConnectionButton` / `SubmitAgentButton` | `AddAgentForm.xaml` | ✅ |
| `MessageList` | `ChatPage.xaml` message list | ✅ |
| `MessageInput` / `SendMessageButton` / `StopResponseButton` | `ChatComposer.xaml` | ✅ |
| `ConnectionStatus` | `ChatPage.xaml` connection-phase label below the composer (Connected / Reconnecting / Offline) | ✅ |
| `SettingsButton` | `ShellSidebar.xaml` | ✅ |
| `HelpMenuButton` | `MainWindow.xaml` `HelpMenuBarItem` | ✅ |
| `AboutMenuItem` / `ConnectOnionDocsMenuItem` / `KeyboardShortcutsMenuItem` | `MainWindow.xaml` Help menu | ✅ |
| `SettingsOverlay` / `AboutOverlay` / `KeyboardShortcutsOverlay` | the three overlay roots | ✅ |
| `AboutOkButton` / `ShortcutsSearchBox` | inside their overlays | ✅ |
| `ApproveOnceButton` / `RejectButton` | `ChatPage.xaml` interactive cards | ✅ |
| `ConnectAgentButton` / `DisconnectAgentButton` / `ReconnectButton` | connection actions in `ChatPage.xaml` | ⏳ pending |
| `NewConversationButton` / `ConversationList` | conversation navigation in `ShellSidebar.xaml` | ⏳ pending |
| `InteractiveCardHeader` | interactive-card expander/header | ⏳ pending |

`AgentNameInput` was removed from the locator contract because the current add-agent form has no agent-name field.

### 13.2 Hard prohibitions

UI automation must **never** depend on:

- Absolute screen coordinates
- A fixed position in the visual tree ("the 2nd Button inside the 3rd Grid")
- Control indices
- Text content as the sole locator (copy changes; it may be localized)
- Fixed `Thread.Sleep`
- The current display resolution or DPI

### 13.3 Async waiting

Every wait is a **condition wait** with a timeout and a polling interval:

```csharp
// Correct
await Wait.UntilAsync(
    () => window.FindFirstDescendant(cf => cf.ByAutomationId("MessageList"))
               ?.FindAllChildren().Length == expectedCount,
    timeout: TimeSpan.FromSeconds(15),
    interval: TimeSpan.FromMilliseconds(100));

// Forbidden
Thread.Sleep(3000);
```

Permitted conditions: element appears · element becomes clickable · status text changes · message count changes · connection state changes · loading indicator disappears · `StopResponseButton` disappears (i.e. the turn ended).

---

## 14. Accessibility Testing

| Check | Method | Tool |
|---|---|---|
| AutomationId coverage | Static XAML scan + UIA tree dump | Custom script / Accessibility Insights |
| Icon-button naming | Assert every `Button` with icon-only content has `AutomationProperties.Name` | Static scan (**can be a CI gate**) |
| Reading order | Manual | Narrator |
| Keyboard reachability | E2E-015 | FlaUI |
| Contrast | Both themes | Accessibility Insights for Windows |
| 200% text scaling | Manual | Windows Settings |
| High contrast | Manual | Windows Settings |
| Color not sole carrier | Code review (approval cards already carry a text outcome) | Human |

**Recommended CI gate**: a XAML static scan that fails the build when a new icon-only button lacks `AutomationProperties.Name`. Cheap to build, high payoff.

---

## 15. Performance and Stability

| ID | Metric | Target | Method |
|---|---|---|---|
| **PERF-START-001** | Cold start to interactive | < 2.0s | Stopwatch |
| **PERF-CHAT-001** | Conversation switch (cache hit) | < 100ms | `ConversationCache` hit path |
| **PERF-CHAT-002** | Conversation switch (DB read, 1000 msgs) | < 500ms | — |
| **PERF-MSG-001** | Scrolling 1000 messages | No dropped frames (≥ 55fps) | Depends on `ChatMessageTemplateSelector`'s per-kind templates + virtualization |
| **PERF-MSG-002** | Thumbnail converter cache | Recycled containers do not re-decode | `LocalPathToImageSourceConverter` (caches `BitmapImage` by path) |
| **PERF-DB-001** | Per-turn write cost | O(turn), not O(conversation) | Assert the upsert touches exactly N rows |
| **PERF-STREAM-001** | 1000 events per turn | Nothing dropped; UI does not freeze | — |
| **STAB-LEAK-001** | Repeated page enter/exit cycles | Tail Private Bytes slope ≤ 1.5 MB/cycle and tail span ≤ 24 MB; handles/threads plateau | ✅ `MemoryLeakTests` via `scripts/Test-MemoryLeaks.ps1` |
| **STAB-LEAK-002** | Repeated overlay open/close cycles | Same plateau gate; no event-subscription leak | ✅ Settings scenario in `MemoryLeakTests` |
| **STAB-LEAK-003** | WebSocket release | No residual socket after a conversation is deleted | `AgentConnectionRegistry` count assertion |
| **STAB-LEAK-004** | `CancellationTokenSource` release | No leak | — |
| **STAB-NET-001** | 20 disconnect/reconnect cycles | No handle leak; always recovers | Fake server flapping |
| **STAB-LONG-001** | 2-hour soak | Memory stays flat | Long-run test |

`STAB-LEAK-001/002` deliberately do not require Task Manager memory to return to the launch
baseline. CLR/XAML/native allocators retain reusable high-water pages. The regression signal is
an unbounded tail, independently corroborated by handles and threads; see
[`MEMORY_LEAK_INVESTIGATION.md`](./MEMORY_LEAK_INVESTIGATION.md).

---

## 16. Regression Strategy

1. **Every fixed P0/P1 defect gets a regression case**, ID-suffixed with the issue number — e.g. `UI-MD-003 (REG-#42)`.
2. **Known real regression points in this repo** (these have already happened; lock them down permanently):
   - `UI-MD-003/004/005` — `InlineUIContainer` is legal only inside a `RichTextBlock` paragraph (this crashed)
   - `UT-STREAM-002` — live/headless projection parity (prevents double rendering)
   - `UT-SESSION-004` — conversations with an active run must bypass the cache
   - `UT-CONN-004` — the silence watchdog must not fire during `ask_user`
   - `UT-TOOL-005` — the template selector resolves once per container
   - XAML traps already documented in CLAUDE.md: `x:Bind` DataTemplate types must not use `required` / `init`; a `ResourceDictionary` cannot be shared across elements
3. **Regression suite = all P0 + everything tagged REG**, run on every PR.
4. Any wire-format change must update both `ConnectOnion.Protocol.Tests` and the Conformance reference.

---

## 17. Defect Severity Levels

| Level | Definition | Example | SLA |
|---|---|---|---|
| **S1 Blocker** | Data loss/corruption, crash, cannot send messages, cannot launch | Persist overwrites user history; app crashes on start | Fix immediately; blocks release |
| **S2 Critical** | Core flow impaired, workaround exists | Reconnect requires an app restart | Must fix before release |
| **S3 Major** | Non-core functionality broken | Shortcut search shows no empty state | Scheduled |
| **S4 Minor** | Cosmetic / polish | Code block sits 2px off the baseline | Opportunistic |

---

## 18. Test Pass Criteria

All of the following must hold before release:

- [ ] **All P0 tests pass** — no exceptions, no waivers
- [ ] **No S1 or S2 defects** open
- [ ] Core user flows pass: E2E-001 … E2E-006
- [ ] **All persistence tests pass** (every `DB-*`, especially `DB-MSG-001/002/004`)
- [ ] **Connection recovery passes** (`UT-CONN-002/003/004/005` + E2E-005)
- [ ] Critical UI automation paths pass (E2E-002/003/004/006/015)
- [ ] Accessibility blockers cleared (`AutomationId` coverage; 100% of icon buttons named)
- [ ] No evident resource leaks (`STAB-LEAK-001…004` green)
- [ ] Every fixed severe defect has a regression case **that runs in CI**
- [ ] `ConnectOnion.Protocol.Conformance` passes (the existing CI gate)

---

## 19. Release Gate Criteria

In addition to §18:

- [x] Every test project is wired into the `.sln` **and** CI
- [x] All current test projects run in CI on every push and pull request (66 protocol + 139 client unit + 42 integration)
- [ ] Coverage: `ConnectOnion.Protocol` ≥ 80%; `Services/Runtime` + `Data` ≥ 70%
- [ ] Performance baselines met: `PERF-START-001`, `PERF-MSG-001`
- [ ] A manual pass over Light/Dark × 100%/150%/200% DPI
- [ ] No P0 case is skipped or ignored

---

## 20. Test Directory Structure

Production `.csproj` files sit at the repo root; **every implemented automated test project lives under `tests/`**. The tree below is the target layout: the three existing xunit projects are implemented, while `WinUITests`, `UITests`, and `PerfTests` are planned phase-3 projects rather than current files.

```
connectonion-desktop/
├── ConnectOnion.WinUIClient/           # existing
├── ConnectOnion.Protocol/              # existing
├── ConnectOnion.Protocol.Conformance/  # existing (signing gate)
├── ConnectOnion.Protocol.LiveTest/     # existing (manual smoke, not in CI)
│
└── tests/
    ├── ConnectOnion.Protocol.Tests/          # xunit · in .sln + CI
    │   ├── InputMessageBuilderTests.cs
    │   ├── DataUrlCodecTests.cs
    │   ├── AttachmentWireEventsTests.cs
    │   ├── ConversationRunRegistryTests.cs
    │   └── AgentConnectionServiceTests.cs    # new: timeouts / watchdog / dispose
    │
    ├── ConnectOnion.WinUIClient.UnitTests/   # xunit · net10.0 · no UI thread
    │   ├── ViewModels/
    │   │   ├── ChatViewModelTests.cs                 # CanSend truth table, empty send, retry
    │   │   └── KeyboardShortcutsViewModelTests.cs
    │   ├── Services/
    │   │   ├── AppVersionServiceTests.cs
    │   │   ├── ConversationCacheTests.cs             # LRU + no-cache-while-running
    │   │   ├── KeyboardShortcutCatalogTests.cs       # catalog vs. real handlers
    │   │   └── Notifications/
    │   │       ├── NotificationPolicyTests.cs        # every Decide branch
    │   │       ├── NotificationCoordinatorTests.cs   # turn/approval dedup, disconnect grace
    │   │       ├── NotificationTextTests.cs
    │   │       └── DedupCacheTests.cs
    │   ├── Runtime/
    │   │   ├── ChatTurnProjectionTests.cs            # live/headless parity
    │   │   ├── ToolActivityProjectorTests.cs
    │   │   └── ToolActivityMigrationTests.cs
    │   ├── Models/
    │   │   └── ChatMessageTests.cs                   # interactive + thought-process collapse/loading state
    │   ├── Converters/
    │   │   ├── BoolToVisibilityConverterTests.cs
    │   │   ├── EventStatusToBrushConverterTests.cs
    │   │   └── ToolStatusToBrushConverterTests.cs
    │   └── Selectors/
    │       └── ChatMessageTemplateSelectorTests.cs
    │
    ├── ConnectOnion.WinUIClient.WinUITests/   # MSTest · WinUI Unit Test App (UI thread)
    │   ├── Controls/
    │   │   ├── MarkdownTextBlockTests.cs             # highlight × code/link regressions
    │   │   ├── HighlightedTextBlockTests.cs
    │   │   ├── ToolActivityViewTests.cs
    │   │   └── AgentAvatarTests.cs
    │   ├── Overlays/
    │   │   ├── AboutOverlayTests.cs
    │   │   ├── SettingsOverlayTests.cs
    │   │   └── KeyboardShortcutsDialogTests.cs
    │   ├── Templates/
    │   │   └── ChatTemplateSelectionTests.cs
    │   └── Focus/
    │       └── OverlayFocusTests.cs
    │
    ├── ConnectOnion.IntegrationTests/         # xunit + fake server + real SQLite
    │   ├── Database/
    │   │   ├── AppDatabaseSchemaTests.cs
    │   │   ├── ConversationRepositoryTests.cs        # incremental upsert
    │   │   ├── SessionRepositoryTests.cs
    │   │   ├── AgentRepositoryTests.cs
    │   │   ├── IdentityStoreTests.cs
    │   │   └── MigrationTests.cs                     # old-DB snapshot
    │   ├── Connections/
    │   │   ├── AgentSessionManagerTests.cs           # run survives page close
    │   │   ├── AgentConnectionRegistryTests.cs
    │   │   └── ReconnectTests.cs
    │   ├── Notifications/
    │   │   └── NotificationCoordinatorTests.cs
    │   ├── Lifecycle/
    │   │   └── ShutdownTests.cs
    │   └── Fakes/
    │       ├── FakeAgentServer.cs                    # HttpListener + WebSocket
    │       ├── FakeNotificationChannel.cs
    │       ├── FakeWindowPresence.cs
    │       └── FakeChatWindow.cs
    │
    ├── ConnectOnion.UITests/                  # FlaUI · the 15 E2E flows
    │   ├── Fixtures/
    │   │   ├── AppFixture.cs                         # launch/close process, isolated %AppData%
    │   │   └── FakeServerFixture.cs
    │   ├── PageObjects/                              # wrap AutomationIds; never expose tree shape
    │   │   ├── MainWindowPage.cs
    │   │   ├── ChatPageObject.cs
    │   │   ├── SidebarPageObject.cs
    │   │   └── OverlayPageObjects.cs
    │   ├── Workflows/
    │   │   ├── OnboardingE2E.cs                      # E2E-001
    │   │   ├── ChatE2E.cs                            # E2E-002/003/004/015
    │   │   ├── ConnectionE2E.cs                      # E2E-005
    │   │   ├── PersistenceE2E.cs                     # E2E-006
    │   │   ├── NotificationE2E.cs                    # E2E-007/008
    │   │   ├── OverlayE2E.cs                         # E2E-009/010/011/012
    │   │   ├── LifecycleE2E.cs                       # E2E-013
    │   │   └── PerformanceE2E.cs                     # E2E-014
    │   └── TestData/
    │       ├── agents.valid.json
    │       ├── conversation.1000.json
    │       ├── markdown.torture.md
    │       └── db/v0.db
    │
    └── ConnectOnion.PerfTests/                # BenchmarkDotNet (optional, phase 3)
        └── ProjectionBenchmarks.cs
```

### Naming conventions

**Test methods**: `MethodUnderTest_Scenario_ExpectedOutcome` — matching the existing `DataUrlCodecTests.TryDecode_MalformedInput_ReturnsFalseWithoutThrowing`. **Follow the repo's convention; do not invent a second one.**

**Test classes**: `{ClassUnderTest}Tests`
**E2E methods**: `E2E00X_ShortDescription`
**Regression cases**: suffix the method name with `_Regression_Issue42`

---

## 21. Implementation Priority

### 21.0 Prerequisite infrastructure (**do this first or everything else is wasted motion**)

| ID | Task | Why |
|---|---|---|
| ~~**INFRA-0**~~ | ~~Add `ConnectOnion.Protocol.Tests` to the `.sln` and CI~~ | ✅ **Done** — it now lives at `tests/ConnectOnion.Protocol.Tests` and runs in CI alongside the two new suites |
| **INFRA-1** | Add the `AutomationId`s from §13.1 | 🟡 **Partial** — 24 critical IDs are present and contract-tested; connection/conversation/card locators remain |
| ~~**INFRA-2**~~ | ~~Build `FakeAgentServer` (HttpListener + WebSocket)~~ | ✅ **Done** — protocol connection tests cover handshake, timeout, watchdog, responses and disposal |
| ~~**INFRA-3**~~ | ~~Build a temp-DB fixture (isolated from `%AppData%`)~~ | ✅ **Done** — `tests/ConnectOnion.IntegrationTests/Database/TempDatabaseFixture.cs` |
| ~~**INFRA-4**~~ | ~~Extract `IUriLauncher` (wrapping `Launcher.LaunchUriAsync`)~~ | ✅ **Done** — `Services/UriLauncher.cs`, injectable via `AppServices.UriLauncher` |
| **INFRA-5** | Extract `PendingActivationBuffer` from static `App` | Otherwise the cold-start activation race cannot be tested |

### 21.1 P0 tests required before release

**48 P0 cases**, distributed as: LC (5) · AGENT (5) · CONN (9) · DB (12) · CHAT (6) · STREAM (9) · SESSION (2) · MD (4) · TOOL (3) · OVL (5) · NOTIF (7) · A11Y (3) · ERR (4) · E2E (6).

### 21.2 Phase 1 — headless unit tests (1–2 weeks, highest ROI)

Pure functions, zero UI dependency, near-instant. **This phase alone covers roughly 60% of the high-risk logic.**

### 21.3 Phase 2 — integration tests (2–3 weeks)

Database + fake server + `AgentSessionManager`. **This phase locks down data safety and connection recovery.**

### 21.4 Phase 3 — UI automation (3–4 weeks)

FlaUI + the 15 E2E flows. Blocked on completing INFRA-1 and creating a launch/isolation fixture. These tests must not be run as part of ordinary headless development unless application launch is explicitly requested.

### 21.5 Keep manual for now

Multi-monitor, DPI switching, high contrast, Narrator reading order, Focus Assist, tray interaction, title-bar dragging, and visual baseline alignment (the empirical `0.22` in `ApplyBaselineNudge`).

### 21.6 Most flake-prone areas

1. **E2E-004 (switch mid-stream)** — a race; assert final consistency, never intermediate states
2. **Notification E2E** — OS behavior is uncontrollable → must go through the fake channel
3. **Performance assertions** — CI machines are noisy → sample repeatedly, take the median, leave 50% headroom
4. **Connection timeout tests** — actually waiting 30s/60s is unacceptable → **inject a controllable clock**
5. **Tray / window activation** — depends on OS foreground policy → push down to unit tests wherever possible

### 21.7 Biggest infrastructure gap today

See §21.0. CI, the fake server, temp databases, connection registry tests, and the first critical AutomationId slice are complete. The biggest gap is now the **WinUI/FlaUI test host and process-isolation fixture**. Headless coverage can continue independently for session lifecycle and activation buffering.

### 21.8 The first 20 tests to write

Ranked by risk ÷ cost — all headless, all writable in a day:

| # | Test ID | Why this one |
|---|---|---|
| ~~1~~ | ~~INFRA-0 (wire existing tests into CI)~~ | ✅ Done — the precondition for every test below |
| ~~2~~ | ~~`UT-STREAM-002`~~ | ✅ Done — `ChatTurnProjectionTests` |
| ~~3~~ | ~~`DB-MSG-001`~~ | ✅ Done — `ConversationRepositoryTests` |
| ~~4~~ | ~~`DB-MSG-002`~~ | ✅ Done — `ConversationRepositoryTests` |
| ~~5~~ | ~~`DB-MSG-004`~~ | ✅ Done — `ConversationRepositoryTests` |
| ~~6~~ | ~~`UT-NOTIF-001…006`~~ | ✅ Done — `NotificationPolicyTests` + `NotificationCoordinatorTests` |
| 7 | `UT-CHAT-003` | `CanSend` truth table |
| 8 | `UT-TOOL-004` | `EventKind` → template mapping |
| ~~9~~ | ~~`UT-CONN-004`~~ | ✅ Done — `AgentConnectionServiceTests` verifies human-response waits pause the watchdog |
| ~~10~~ | ~~`UT-STREAM-003`~~ | ✅ Done — `ChatTurnProjectionTests.AppendFinalReply_*` |
| 11 | `UT-STREAM-011/012` | Interactive answers persist; unanswered sealed as "No selection" |
| 12 | `UT-SESSION-004` | Active-run conversations bypass the cache |
| ~~13~~ | ~~`UT-ABOUT-002`~~ | ✅ Done — `AppVersionServiceTests` verifies assembly metadata and fallback behavior |
| ~~14~~ | ~~`UT-KBD-001`~~ | ✅ Done — shortcut catalog and source-contract tests cover catalog/handler agreement |
| 15 | `UI-MD-007` | Highlight match counting (the fallback path must count too) |
| ~~16~~ | ~~`DB-SCHEMA-001`~~ | ✅ Done — `AppDatabaseSchemaTests` verifies idempotent initialization |
| ~~17~~ | ~~`DB-ID-001`~~ | ✅ Done — `IdentityStoreTests` verifies DPAPI-protected persistence |
| 18 | `UT-CHAT-001/002` | Empty and whitespace-only messages |
| ~~19~~ | ~~`UT-CONN-009`~~ | ✅ Done — concurrent `GetOrCreate` calls invoke the connection factory exactly once |
| ~~20~~ | ~~`DB-MIGRATION-001`~~ | ✅ Done — migration tests open and upgrade legacy schema snapshots |

---

## Appendix A — Design Gaps Surfaced by This Plan (all resolved)

These were never testing problems — they are real design gaps that writing the test plan **exposed**, and all seven have since been fixed. The appendix is kept for two reasons: each fix is the reason a specific test case is writable at all (delete this and those cases lose their provenance), and it records *why* a change was made in a form that does not fit in a code comment.

**The durable architectural facts now live in [`CLAUDE.md`](../CLAUDE.md)** — schema versioning, the `IdentityStore` reset contract, `IUriLauncher`. Read that for how the code works today; read this for how it got there.

| # | Gap | Resolution |
|---|---|---|
| 1 | 🔴 **No DB migration framework** — the documented recovery was "delete the database" | **Fixed.** `Data/SchemaMigrator.cs` versions the DB via SQLite's `PRAGMA user_version` and applies ordered forward migrations in a transaction. Existing databases (version 0) are stamped as the version-1 baseline without running anything, since that is exactly the shape pre-versioning builds produced. A database written by a *newer* build now fails loudly instead of being silently mangled. `AppDatabase.EnsureInitializedAsync` calls it right after the baseline schema. **Enables `DB-MIGRATION-001/002`.** |
| 2 | 🔴 **`IdentityStore` silently regenerated the identity on decrypt failure** | **Fixed.** `Load()` now distinguishes *nothing stored* (normal first run → generate quietly) from *stored but unreadable* (identity is being thrown away → report). The latter raises `IdentityStore.IdentityReset` and latches `WasReset` / `ResetReason`; `MainWindow` surfaces it as an error toast telling the user their address changed and agents must re-authorize. **Enables `DB-ID-002`.** |
| 3 | 🟠 **`Launcher.LaunchUriAsync` is static and unmockable** | **Fixed.** `Services/UriLauncher.cs` introduces `IUriLauncher` + `SystemUriLauncher`, exposed as the settable `AppServices.UriLauncher`. `Shell/MainWindow.HelpMenu.cs` now goes through it. A test substitutes a fake and asserts the URI. **Enables `UT-HELP-002` and `E2E-012`; INFRA-4 is done.** |
| 4 | 🟠 **`AppServices` is a static locator** | **No change needed.** `AgentSessionManager` already exposes `public AgentSessionManager(HttpClient, ConversationRepository)`, and the repositories are constructible. Tests construct instances directly and bypass `AppServices` entirely. |
| 5 | 🟡 **Ctrl+Shift+/ hard-coded VK 191** | **Fixed.** `ProducesSlash(VirtualKey)` asks the *current keyboard layout* what character a key types (`MapVirtualKeyW` with `MAPVK_VK_TO_CHAR`), so the shortcut works on layouts where `/` is not on VK_OEM_2. Numpad divide is accepted too; VK 191 remains only as a fallback. **Removes the `UT-KBD-002` layout risk.** |
| 6 | 🟡 **`Name[..1]` splits emoji / surrogate pairs** | **Fixed.** The bug existed in *four* places (`ChatViewModel`, `HomePage`, `AgentDetailViewModel`, `ShellAgentItem`), which is why it now lives in one: `Common/NameInitial.cs` takes the first *grapheme* via `StringInfo.GetTextElementEnumerator`. **Enables `UT-AGENT-005`.** |
| 7 | 🟡 **`ApplyBaselineNudge`'s `0.22` was a guessed em-ratio** | **Fixed.** `HighlightedTextBlock.DescentFor(fontFamily, fontSize)` measures a probe `TextBlock` and derives the real descent from `DesiredSize.Height - BaselineOffset`, cached per (family, size); the ratio survives only as a fallback when measurement is impossible. Each call site now passes the font that actually governs its alignment — the code face for inline code, the document face for a code block. Search highlights inside code also inherit the monospace face now, which they previously did not. |

**Net effect on this plan**: `INFRA-4` (extract `IUriLauncher`) is complete, and `DB-MIGRATION-001`, `DB-ID-002`, `UT-HELP-002`, `UT-AGENT-005`, and `E2E-012` moved from "blocked on a refactor" to "writable today". `INFRA-5` (extract `PendingActivationBuffer`) remains open.

---

*End of document. 48 P0 cases · 15 E2E flows · 6 prerequisite infrastructure tasks.*
