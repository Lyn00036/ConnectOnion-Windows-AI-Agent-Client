# Trimming

**Production and CI publish untrimmed, on purpose.** This document is the gate that decides when
that changes. Nothing here is a recommendation to enable trimming — it is the record of what has
been proved, what has not, and how to re-derive both.

Related: `ConnectOnion.WinUIClient.csproj` (where the setting lives), `scripts/Invoke-TrimAudit.ps1`
(the evidence), `tests/ConnectOnion.TrimSmoke/` (the runtime proof),
`tests/ConnectOnion.WinUIClient.UnitTests/Architecture/TrimmingGateTests.cs` (the guard).

## Why this is a gate and not a flag

`PublishTrimmed=true` does two separable things, and only the first is the one people expect.

1. **The linker removes unreferenced IL.** If it removes something reached only by reflection, you
   get a `MissingMethodException` or a silently empty object at runtime. This is the familiar risk,
   and `IL2026`/`IL3050` warnings are the linker telling you where it might happen.

2. **The SDK sets `JsonSerializerIsReflectionEnabledByDefault=false`.** This is the one that bites.
   Every reflection-based `JsonSerializer.Serialize`/`Deserialize` call then throws
   `InvalidOperationException: Reflection-based serialization has been disabled for this
   application` — **on the first call, unconditionally, whether or not the linker touched
   anything.** Note the exception type: `IL2026`'s wording ("can break functionality when
   trimming") suggests a `NotSupportedException` and a maybe. It is neither.

The 2026-07-25 release audit met (2) as a Tool Activity timeline that restored empty in Release and
worked perfectly in Debug. That is the shape of every bug in this class: invisible in development,
invisible in unit tests, invisible in the build log once the warnings are triaged away, and visible
only to a user reopening an old conversation.

**So a clean warning inventory does not mean trimming is safe.** It means the linker has no more
questions. The runtime still does.

## Current status

| Acceptance criterion | Status |
|---|---|
| No unexplained app-owned `IL2026`/`IL3050` in the experimental trimmed publish | **Met** — 0 app-owned sites (was 16) |
| Protocol and interactive serialization paths have direct unit or integration coverage | **Met** — see "Coverage" below |
| Tool Activity and every interactive card survive persist/restart in the trimmed publish | **Partially met** — proved for `Core` + `Protocol` in a trimmed binary across a real process restart; **not** proved in the trimmed *WinUI app* binary |
| Fake Agent and real-window smoke tests pass against the trimmed executable | **Not met** — the weekly FlaUI job exercises the untrimmed production-shaped publish only; `CONNECTONION_UI_TEST_EXE` can be pointed at an experimental trimmed publish for this criterion |
| Signed MSIX functional matrix passes with trimming enabled experimentally | **Not met** — the current release pipeline publishes only the self-contained portable ZIP (`docs/RELEASE.md`); no signed package has been produced or installed, so there is nothing to run the matrix against |
| No key operation regresses >10% against the same-machine untrimmed baseline | **Not met** — `scripts/Measure-Performance.ps1 -Exe <trimmed>` is the mechanism; no run yet |
| Package-size savings and compatibility risks documented | **Met** — this document |
| Production and CI remain explicitly untrimmed until all of the above pass | **Held** — enforced by `TrimmingGateTests` |

## What was actually wrong, and what changed

The audit's "clean publish" result did not reproduce on `main`: an experimental trimmed publish
emitted **16 app-owned `IL2026` sites**, every one of them a reflection-based `JsonSerializer` call
on a path that runs in normal use.

| Site | What it serialized | Now |
|---|---|---|
| `AgentConnectionService.cs:984` | **every outgoing WebSocket frame** | `WireJson.Serialize` |
| `AgentConnectionService.cs:809` | `session_sync` compaction | `WireJson.Serialize` |
| `InteractiveResponseBuilder.cs:31` | the `ask_user` field-form answer | `WireJson.SerializeStringMap` |
| `ToolActivityProjector.cs:379` | redacted tool arguments, on every persisted step | direct `Utf8JsonWriter` recursion |
| `AgentTurnExecutor.cs` ×7 | re-serialized interactive frames; `JsonElement` parsing | `WireJson.Serialize`, `JsonDocument.Parse` |
| `AgentSessionManager.cs` ×4 | reconnect/reconnected progress events | `WireJson.Serialize` |
| `AgentInfoService.cs:153` | the relay-composed `/info` blob | `EndpointResolver.SerializeAgentInfo` |
| `AppStorage.cs:80,101` | a generic file round-trip with **no callers** | deleted |

Two of these are worth calling out beyond the trim fix:

- **The outgoing-frame serializer covers the entire protocol.** `SendJsonAsync` is the single exit
  for `CONNECT`, `INPUT`, `INTERRUPT`, `mode_change`, `SESSION_STATUS`, `PONG`, `ONBOARD_SUBMIT`
  and all three interactive answers. Under trimming the app would have failed to send anything at
  all — it would not have connected, so nothing downstream could have been tested.

- **`AgentInfoService` had a latent bug the trim fix exposed.** It built the relay's `/info` blob
  from an anonymous type with no naming policy, so it emitted `{"Name":…,"Description":…}` for a
  skill and `{"Text":…,"Files":{"MaxFileSizeMb":…}}` for capabilities — while
  `NormalizeSkills`/`ParseAcceptedInputs` read `name`, `description`, `text` and
  `max_file_size_mb`. Every relay-composed blob read back with no skills and no declared
  capabilities, in *untrimmed* builds too. The writer now lives in `EndpointResolver` beside those
  parsers so the halves cannot drift apart again, and `AgentInfoSerializationTests` pins the round
  trip.

### The two sanctioned mechanisms

There is no third way, and that is the point:

- **Ours** — a blob we define and read back out of our own database: a source-generated context
  (`AppJsonContext`, `ConversationJsonContext`).
- **Theirs** — a shape the host defines, which is deliberately untyped on our side: `WireJson`
  (insertion-ordered, default escaping, byte-identical to what the reflection serializer produced)
  or a hand-written `Utf8JsonWriter`. `CanonicalJson` is the pre-existing example.

`WireJson.WriteValue` throws on an unrecognised value type rather than guessing. A builder that
starts putting something new on the wire fails in `WireJsonTests`, which is the moment to notice.

## Coverage

- `WireJsonTests` — 30+ cases, most asserting **byte equality against `JsonSerializer.Serialize` as
  an oracle**. The requirement is not "emits reasonable JSON", it is "emits the bytes the agent
  already receives", so the implementation being replaced is the right thing to diff against.
  Reflection still works in a test host, which is what makes the oracle available here and
  unavailable in the shipping build — that asymmetry is why the smoke harness below also exists.
- `AgentInfoSerializationTests` — `/info` round trip through its own parsers.
- `SanitizeJsonEncodingTests` — the redaction recursion, including nesting, arrays and the
  non-JSON fallback.
- `tests/ConnectOnion.TrimSmoke` — the runtime proof; see below.

## The smoke harness

`tests/ConnectOnion.TrimSmoke` is a plain console app, not an xunit project: a trimmed
self-contained publish has no VSTest host to load into, and the whole question is what happens
inside that binary.

It sets `JsonSerializerIsReflectionEnabledByDefault=false` **unconditionally**, so an ordinary
`dotnet run` already reproduces the runtime failure mode — the fast loop catches a reintroduced
reflection call without waiting for a publish. Publishing it trimmed adds the linker's half.

Its first check is a **negative control**: it calls the reflection serializer and requires it to
throw. Every other check passes trivially if the fallback is on, so without this a green run would
mean nothing.

`persist` and `verify` are separate phases so the audit script can run them as **two processes**
against one data root. Restarting for real is the only way to distinguish a row that survived from
an in-memory object that was handed back — which is exactly what the original bug turned on.

```powershell
# Fast loop (untrimmed, reflection fallback already off)
dotnet run --project tests\ConnectOnion.TrimSmoke\ConnectOnion.TrimSmoke.csproj -- all

# The real thing, plus both app publishes and the warning inventory
pwsh scripts\Invoke-TrimAudit.ps1

# Just the harness, trimmed
pwsh scripts\Invoke-TrimAudit.ps1 -SmokeOnly
```

**What it covers:** outgoing frame serialization for every frame shape, inbound `ask_user` /
`ONBOARD_REQUIRED` / `agent_image` / `files_received` parsing, the `/info` round trip, the
interactive response builders, tool-argument redaction, BIP39 derivation and DPAPI identity
storage, and — across a restart — the Tool Activity timeline with its steps, arguments, results
and derived invocations, the readable interactive cards with their resolved answers, attachment
metadata, and the preferences/shortcut-override map.

**What it does not cover, and this is the gap in criterion 3:** it links `Core` and `Protocol`
only. The WinUI app binary additionally trims XAML type information, `CommunityToolkit`,
`FluentIcons`, `H.NotifyIcon`, `Markdig`, `QRCoder`, `Win2D`, Serilog and the Windows App SDK.
Nothing here says anything about those.

## Warning inventory

Classification rule, as implemented in `Invoke-TrimAudit.ps1`:

- **App-owned** — the line begins with a repo source path and position:
  `D:\…\AgentConnectionService.cs(984,20): warning IL2026: …`. These block the gate.
- **Third-party** — the line begins with `ILLink :` and names generated code with no source
  position. These do not block the gate.

The trailing `[…\Some.csproj]` tag appears on *both* and cannot be used to tell them apart; the
leading token is what distinguishes them.

**Remaining third-party warnings: `IL2081` ×35, `IL2067` ×1** — all from CsWinRT's generated ABI
marshalling (`ABI.Windows.Foundation.*`, `ABI.System.Collections.Generic.KeyValuePair`). They are
generic-parameter annotation mismatches in code this repo does not own and cannot annotate. They
are attributed to whichever project publishes, which makes them look app-owned until you read the
message. Not suppressed — an `IL2081` that starts naming *our* types would be a real finding, and
blanket suppression would hide it.

## Size

Measured 2026-07-27, SDK 10.0.301, `main` at `c2ddeb5`:

| Configuration | Size |
|---|---|
| Untrimmed + ReadyToRun (**what ships today**) | 247.59 MB |
| Trimmed, no ReadyToRun | 89.35 MB |
| Difference | 158.24 MB (63.9%) |

Re-derive with `Invoke-TrimAudit.ps1`, which writes `artifacts/trim-audit/trim-audit.json`.

**These two are not directly comparable, and the difference is not the trimming saving.**
ReadyToRun precompiles IL to native code and roughly doubles assembly size; the shipping build has
it on and the trimmed measurement has it off, because R2R makes the publish much slower and
changes nothing about what this gate is testing. A like-for-like trimmed **+ R2R** number has not
been measured, and it is the only one that would honestly answer "what does trimming save us".
Quote that 158 MB / 63.9% as an upper bound at most.

Size is also not the reason to want this. It is not a metered download, it is a desktop
application. The real arguments are startup working set and disk footprint, and neither has been
measured against the untrimmed baseline yet — that is the >10% criterion, still open.

## Compatibility risks if trimming is enabled

- **Reflection-based JSON anywhere new.** The mechanism is now closed off in the code that exists,
  but nothing stops the next `JsonSerializer.Serialize(someObject)`. The smoke harness catches it
  only on the paths it exercises.
- **XAML type resolution.** `x:Bind` is compiled and safe; `Binding`, `DataTemplateSelector`
  targets, `{ThemeResource}` lookups by string, and `XamlTypeInfo.g.cs` reflection metadata are
  the classic trimming casualties in WinUI, and none of them are exercised by the harness.
- **Third-party packages.** None of the eight UI packages declare `IsTrimmable`, so the linker
  keeps them whole today; that is why the trimmed size is 90 MB and not far smaller. If a future
  version starts declaring it, the risk profile changes without any edit here.
- **`Microsoft.Data.Sqlite` + `SQLitePCLRaw.provider.winsqlite3`** resolve a native provider
  through a static initializer that trimming has historically pruned. The harness covers this,
  which is the one third-party risk it does retire.
- **Serilog sinks** are resolved by configuration, and configuration-driven type resolution is
  reflection by another name.

## What to do before flipping the switch

1. Run `pwsh scripts\Invoke-TrimAudit.ps1` — it must report 0 app-owned warning sites and a green
   persist/verify pair.
2. Publish the app trimmed, then run the real-window smoke test against it:
   `$env:CONNECTONION_UI_TEST_EXE = '<trimmed publish>\ConnectOnion.WinUIClient.exe'` followed by
   `dotnet test tests\ConnectOnion.WinUIClient.UITests\…`.
3. Exercise the trimmed app by hand against a live agent: a tool-using turn, an approval, an
   `ask_user` form, a plan review, a mode change, an image attachment, a reconnect, then **restart
   and reopen the conversation** — the restart is the step that would have caught the original bug.
4. `pwsh scripts\Measure-Performance.ps1 -Exe <trimmed>` against a same-machine untrimmed baseline;
   no key operation may regress more than 10%.
5. The signed MSIX matrix — temporarily restore the dormant MSIX build/signing path described in
   `docs/RELEASE.md`, build a trimmed package, and run `scripts/Test-ReleaseUpgrade.ps1` against
   it. The active portable release passes `PublishTrimmed=false` explicitly and
   `PackageIdentityTests` asserts it does, so this step means deliberately overriding both, not
   quietly flipping them.
6. Only then edit `PublishTrimmed` in `ConnectOnion.WinUIClient.csproj` and CI, and update
   `TrimmingGateTests` in the same commit — the guard is meant to make this a decision, so
   changing it is part of taking the decision, not an obstacle to route around.
