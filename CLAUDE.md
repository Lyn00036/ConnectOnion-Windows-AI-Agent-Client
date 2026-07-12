# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ConnectOnion Desktop is a native Windows client (WinUI 3, C#, .NET 10) for chatting with ConnectOnion-compatible agents over WebSocket. `frontend/` is the **legacy** Electron + React + TypeScript implementation, kept only as reference for prior UX/business behavior — do not build new features there unless explicitly asked; all active work happens in the WinUI projects at the repo root.

## Solution Layout

Five sibling `.csproj` projects at the repo root. Only three are wired into the `.sln` — keep this in mind when running `dotnet build`/`dotnet test` against the solution vs. a project directly:

| Project | In `.sln`? | Purpose |
|---|---|---|
| `ConnectOnion.WinUIClient/` | yes | The WinUI 3 app (`net10.0-windows10.0.19041.0`) |
| `ConnectOnion.Protocol/` | yes | Transport-agnostic protocol library (`net10.0`, no WinUI dependency) |
| `ConnectOnion.Protocol.Conformance/` | yes | Cross-checks C# Ed25519/canonical-JSON signing against a Node.js reference signer (`ref-sign.js`) — this is the CI gate |
| `ConnectOnion.Protocol.LiveTest/` | no | Console script for a real end-to-end run against a deployed agent; run directly with `dotnet run` |
| `ConnectOnion.Protocol.Tests/` | no | xunit unit tests for `ConnectOnion.Protocol` (wire schema, encoding, event parsing); run directly with `dotnet test` — not yet added to the `.sln` or to CI |

The `.sln` lives at `ConnectOnion.WinUIClient/ConnectOnion.WinUIClient.sln` even though it references sibling projects by relative path (`..\ConnectOnion.Protocol\...`).

SDK pin: `global.json` sets `"version": "10.0.100"` with `rollForward: latestFeature` (any newer .NET 10 feature-band SDK works).

## Commands

Build/test the two projects that aren't in the `.sln` directly by `.csproj` path; everything else goes through the solution.

```powershell
# Restore + build the solution (WinUIClient, Protocol, Conformance)
dotnet restore ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln -p:Platform=x64
dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln --configuration Debug --no-restore -p:Platform=x64

# Run the app unpackaged (no MSIX deploy step needed)
dotnet run --project ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.csproj -p:Platform=x64 -p:RunUnpackaged=true

# Protocol unit tests (xunit) — the actual test-writing target for wire-protocol work
dotnet test ConnectOnion.Protocol.Tests\ConnectOnion.Protocol.Tests.csproj
dotnet test ConnectOnion.Protocol.Tests\ConnectOnion.Protocol.Tests.csproj --filter "FullyQualifiedName~InputMessageBuilderTests"
dotnet test ConnectOnion.Protocol.Tests\ConnectOnion.Protocol.Tests.csproj --filter "FullyQualifiedName~DataUrlCodecTests.TryDecode_MalformedInput_ReturnsFalseWithoutThrowing"

# Protocol conformance gate (matches CI exactly)
dotnet run --project ConnectOnion.Protocol.Conformance\ConnectOnion.Protocol.Conformance.csproj --configuration Release

# Live protocol smoke test against a real deployed agent (not in CI)
dotnet run --project ConnectOnion.Protocol.LiveTest\ConnectOnion.Protocol.LiveTest.csproj -- <directUrl> <address> "<prompt>"
```

`Platform` must be `x64`, `x86`, or `ARM64` for the WinUI project — `Any CPU` doesn't work for packaged debugging. Visual Studio F5 (Debug/x64, Deploy checked for `ConnectOnion.WinUIClient` in Configuration Manager) is the most reliable path for XAML debugging; `dotnet run -p:RunUnpackaged=true` is the fast CLI loop.

CI (`.github/workflows/ci.yml`, `windows-latest`) builds the `.sln` in Release/x64 with packaging disabled, then runs the Conformance gate. It does **not** currently run `ConnectOnion.Protocol.Tests` or `ConnectOnion.Protocol.LiveTest`, and does not touch `frontend/`.

There is no lint/format command configured for this repo.

## Local data

SQLite at `%AppData%\ConnectOnion\connectonion.db` when run unpackaged (`dotnet run`), or under `%LOCALAPPDATA%\Packages\<msix-identity>\LocalState\ConnectOnion\` when run packaged/deployed from Visual Studio. Schema is created idempotently (`CREATE TABLE IF NOT EXISTS` in `AppDatabase.CreateSchemaAsync`, no migration framework) — to reset state during development, just delete the `.db` file and relaunch. WAL journal mode, foreign keys on.

Tables: `app_meta`, `agents`, `sessions`, `preferences`, `messages` (one row per chat bubble, not a JSON blob — see the WinUI client architecture notes below), `message_attachments` (attachment **metadata only**: kind/filename/mime/size/cache-path — never the base64 payload), `executions`, `trace_events`, `identity_keys` (`private_seed` is a DPAPI-protected (`CurrentUser` scope) blob, base64-encoded — never plaintext; a row written before DPAPI encryption was added won't decrypt, so `IdentityStore` just regenerates a fresh identity rather than migrating it — delete the `.db` file if you have a pre-DPAPI dev database).

Image cache for received `agent_image` attachments lives under `%AppData%\ConnectOnion\cache\images\`, filenames are the content's SHA-256 hash (never trust a remote-provided filename).

## Architecture

### Protocol layer (`ConnectOnion.Protocol`)

Deliberately has zero WinUI/platform dependency so it's unit-testable headless. This is where wire-format logic belongs — don't duplicate JSON parsing in the WinUI layer.

- **`AgentConnectionService`** — the WebSocket state machine (C# port of the TS SDK's `remote-agent.ts`): opens/authenticates via `CONNECT`→`CONNECTED`, sends `INPUT`, resolves on `OUTPUT`. Interactive turns (`ask_user`, `approval_needed`, `plan_review`) and onboarding are surfaced as dedicated C# events, not lumped into the generic stream; everything else (`thinking`, `tool_call`, `tool_result`, `llm_call`, `llm_result`, `assistant`, `agent_image`, `intent`, `eval`, `compact`, `tool_blocked`, `files_received`) is forwarded as a generic `StreamEvent` carrying the raw JSON for the ViewModel to interpret. A 60s silence watchdog is the only bounded wait — `ask_user` legitimately pends on a human indefinitely.
- **`WireMessage`** — thin `JsonElement` wrapper (`GetString`/`GetBool`/`TryGet`), not raw `JsonDocument.Parse` scattered around. Outgoing messages are plain `Dictionary<string, object?>` serialized with the default `JsonSerializer` (no source-gen context, no attribute-based DTOs) — follow this convention for new message types rather than introducing typed request/response classes.
- **`InputMessageBuilder`** — pure function building the `INPUT` frame dict; kept separate from `AgentConnectionService` specifically so the exact wire shape (text-only / images / files / mixed) is unit-testable without a live socket.
- **`AttachmentModels`** — `DataUrlCodec` (encode/decode `data:` URLs, and the size-limit math replicated from the Python host's `validate_files`: the server measures the **encoded string length**, not raw bytes, so client-side preflight must too) and `AttachmentWireEvents` (typed parsers for the real `agent_image`/`files_received` wire events — see "Multimodal attachments" below for the full protocol contract).
- **`EndpointResolver`** — relay lookup, direct-endpoint probing, `/info` fetch and parsing (`AgentAcceptedInputs`/`AgentFileInputs` capability model).
- **`AgentIdentity` / `CanonicalJson` / `Hex`** — Ed25519 identity and canonical-JSON signing, verified byte-for-byte against `ref-sign.js` by the Conformance project.

### WinUI client (`ConnectOnion.WinUIClient`)

Hand-rolled MVVM (no CommunityToolkit.Mvvm) — `Common/ObservableObject.cs` and `Common/RelayCommand.cs` are the base primitives; follow their existing pattern for new models/view models rather than introducing a different notification style. `Services/AppServices.cs` is a manual static service-locator singleton (no DI container) exposing the SQLite repositories, shared `HttpClient`, and presence cache.

- **Pages own their ViewModel** as a `public XyzViewModel Vm { get; } = new();` property; XAML uses `x:Bind` against `Vm.*`, code-behind wires routed events (`Click`, `PointerEntered`, etc.) that call `Vm` methods. `ChatViewModel` is split across two partial-class files: `ChatViewModel.cs` (session/connection lifecycle, `SendAsync`) and `ChatViewModel.StreamEvents.cs` (stream-event → `ChatMessage` mapping, interactive-turn bubbles, per-turn token/duration aggregation).
- **Chat message persistence**: `messages` is one row per rendered bubble (not a serialized envelope) — `ConversationRepository` deletes-then-reinserts the full message list per save inside a transaction, matched by `(conversation_id, id)`. `ChatMessage` carries generic `Event*` fields (`EventKind`, `EventTitle`, `EventDetail`, `EventArgs`, `EventMeta`, `Status`) reused across very different bubble types (`activity`, `tool`, `ask_user`, `approval`, `plan_review`) — check `EventKind` before assuming what a field means for a given bubble.
- **Interactive turns** (`ask_user`/`approval_needed`/`plan_review`) render as persistent inline chat bubbles with live controls (submit button, approve/reject, etc.), not `ContentDialog` popups — this matches the reference web client's chat-item-feed model and keeps the decision visible in history. Their live *control* state (options/fields/free text) is intentionally **not** persisted to SQLite — once the WebSocket session is gone, answering is meaningless. The *resolved result* is persisted, though: the reply-less event stream can't carry the user's answer, so when the page answers it calls `AgentSessionManager.RecordInteractiveAnswer`, and at turn-persist time `ResolveInteractiveCards` stamps the chosen result onto the re-projected card's `EventMeta`/`Status`. Any interactive card a persisted (terminal) run left unanswered is sealed as `"No selection"` — so reloaded history shows what was chosen (or that nothing was) instead of dead, unanswerable controls.
- **Multimodal attachments**: outgoing images/files are picked via `AttachmentPickerService` (`FileOpenPicker`, HWND resolved through the static `App.MainWindow` since `ChatComposer` is a `UserControl` with no HWND of its own), preflight-validated via `AttachmentValidationService` against the agent's real `/info` capability (client-side checks are a UX nicety — the server remains the final authority), and encoded off the UI thread via `AttachmentEncoder` right after validation passes (never re-encoded, never read into memory before validation). Incoming `agent_image` events are decoded/downloaded and cached to disk via `AttachmentImageCacheService` *before* ever touching `ChatMessage` or SQLite — the message list and DB only ever hold a local cache path plus metadata, never a base64 payload. Wire contract specifics (exactly what `images`/`files` look like on `INPUT`, what `agent_image`/`files_received` look like coming back, and what is/isn't actually enforced server-side) live in the `ConnectOnion.Protocol` doc comments above, not duplicated here — read `AttachmentModels.cs` and `InputMessageBuilder.cs` before changing this path.
- **Drag-and-drop attachments**: `Controls/ChatComposer` is the only real drop target — `AttachmentDropService` (the drop counterpart to `AttachmentPickerService`) classifies dropped `StorageFile`s by extension (no file-type filter like the picker gets) into `PendingAttachment` drafts, which still flow through the same `AttachmentValidationService`/`AttachmentEncoder` pipeline; dropped folders are skipped silently rather than recursively expanded. `MainWindow.DragDrop.cs` only paints a window-wide "drop it on the composer" hint (`RootGrid.AllowDrop` + handlers registered with `handledEventsToo: true` so the hint still updates after the composer's own drag handlers mark the event `Handled`) — it is not itself a drop target.
- **Global find overlay** (Ctrl+F, `MainWindow.ViewMenu.cs`): pages that support in-content search implement `Views.IFindHost` (`OpenFind`/`CloseFind`/`SetFindQuery`/`SelectNextFindMatch`/`SelectPreviousFindMatch`, plus a `FindStateChanged` event); `MainWindow` attaches to whatever `ContentFrame.Content` currently is and drives one shared find UI (`FindOverlayPanel`/`FindTextBox`) against it — add `IFindHost` to a new page rather than building a page-local find UI.
- **Sidebar** (`Controls/ShellSidebar.xaml[.cs]`): `ItemsRepeater`-based agent/session tree. Row hover and selection are unified through one `RowStateToBackgroundConverter`-driven Border per row (bound to a computed `ShowRowBackground = IsSelected || IsHovered`) rather than each sub-button showing its own independent hover — sub-button chrome is neutralized via a `Resources` dictionary declared **inline on each row's container element**, not shared through a `StaticResource` (WinUI requires a `ResourceDictionary` assigned to `FrameworkElement.Resources` to be uniquely owned per element; a shared instance throws `XamlParseException` the moment a second row tries to reuse it).
- **Color system** (`Styles/Colors.xaml` + `Styles/Brushes.xaml`, merged into `App.xaml` after `XamlControlsResources`): the single source of truth for every color in the app — a Deep Purple brand palette expanded into semantic Light/Dark tokens (`BrandPrimaryBrush`, `SurfaceElevatedBrush`, `TextSecondaryBrush`, `SidebarItemSelectedBrush`, `SearchMatchBackgroundBrush`, `UserMessageBackgroundBrush`, ...). `Colors.xaml` holds raw `Color` resources per theme; `Brushes.xaml` wraps them as `SolidColorBrush`es pages actually bind to via `{ThemeResource ...}`, **and** re-points WinUI's own themed keys (`TextFillColorSecondaryBrush`, `CardBackgroundFillColorDefaultBrush`, `AccentFillColorDefaultBrush`, `ButtonBackgroundPointerOver`/`Pressed`, `FocusVisualPrimaryBrush`, ...) at the same tokens so native Fluent chrome (Expander, default Button hover, hyperlinks) stays visually consistent without replacing any `ControlTemplate` — `ToggleSwitch`/`CheckBox`/`RadioButton` are deliberately left alone. Code-behind that needs a raw `Windows.UI.Color` (Win2D canvas draws in `ChatComposer`, `AppWindowTitleBar` in `MainWindow.xaml.cs`) goes through `ThemeService.GetColor(key, isDark)` rather than duplicating hex literals; anything reading a `Brush` by string key (converters, `HighlightedTextBlock`, `WinUiMarkdownRenderer`) reads fresh from `Application.Current.Resources[key]` each time rather than caching, so a live theme flip never leaves stale colors on screen. A handful of pre-existing resource keys (`ShellBackgroundBrush`, `ComposerBackgroundBrush`, `StatusSuccessTextBrush`, ...) are kept as redirects onto the new tokens for source compatibility — prefer the new semantic names in new code.

### WinUI/XAML gotchas already paid for in this codebase

Worth knowing before you re-discover them via a build or runtime failure:

- A type referenced from an `x:Bind` `DataTemplate` needs plain `{ get; set; }` properties — `required` or `init` accessors break the compiler-generated `XamlTypeInfo.g.cs` reflection metadata with a confusing `CS9035`/`CS8852`.
- `FluentIcons.WinUI`'s `IconSize` enum only has specific values (`Size10/12/16/20/24/28/32/48`, no `Size14` etc.) — check the actual enum rather than guessing a round number.
- `Microsoft.UI.Xaml.EventTrigger` has no `RoutedEvent` property (that's WPF-only); it always fires on `Loaded` implicitly. A XAML-declared `Loaded`-triggered `Storyboard` needs `<EventTrigger>` with no `RoutedEvent` attribute, or just start the `Storyboard` from a code-behind `Loaded` handler — the latter is what this repo uses for the sidebar's online-status breathing animation.
- `Style` setters can't target `Resources` (not a `DependencyProperty`); scope resource overrides by declaring them inline on the actual element instance instead.

## Git conventions

Conventional Commits, enforced by convention rather than tooling: `<type>(<scope>): <summary>` (types: `feat`/`fix`/`docs`/`test`/`refactor`/`style`/`chore`/`build`/`ci`; recommended scopes include `chat`, `agent`, `sessions`, `storage`, `settings`, `files`, `ui`, `ci`, `build` — see `git_commit_convention.md` for the full list). PRs use the same format for their title.

## Not implemented yet

- Production installer/release automation (MSIX packaging is configured but not wired into CI)
- Migration tooling for older local app data formats
