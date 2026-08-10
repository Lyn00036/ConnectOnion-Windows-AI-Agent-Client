# Design Justifications (COMP9900 — evidence draft)

> **Purpose:** Raw material for the final report “Design Justifications” section.  
> **Audience:** Team → GPT polish → submission.  
> **Rules used:** Only changes with **High** confidence and documented before/after evidence. No invented client feedback or sprint minutes.

**Project brief (Aaron Xie):** Windows desktop client for ConnectOnion agents; must not be “only a packaged web interface”; needs connection, chat, local history, saved agent configs, secure credentials, reliability, and testing (see `docs/PROJECT_BRIEF.md` and client PDF).

**Prior codebase (Electron era):** Documented in `HANDOFF_CONTEXT_PREVIOUS.md` (sibling repo `capstone-project-26t2-9900-t09c-almond`). Stack was Electron + React + TypeScript + ConnectOnion TS SDK; agents/sessions/chat initially in **localStorage**, later partially in **SQLite** via IPC; chat state stored as SDK **JSON envelopes** keyed in settings.

**Current codebase:** Native **WinUI 3 / C# / .NET 10** (`ConnectOnion.WinUIClient`, `ConnectOnion.WinUIClient.Core`, `ConnectOnion.Protocol`). Legacy `frontend/` and vendored Python `agent/` trees were removed in commit `197a88d`.

---

### Design Change 1: Desktop technology stack — Electron/React to native WinUI 3

**Initial design:**
- Windows client built as **Electron + React + Vite**, using the **ConnectOnion TypeScript SDK** (`connect` / `useAgentForHuman`) for WebSocket chat.
- Renderer talked to agents directly; persistence went through **preload IPC → Electron main process → SQLite**, with much configuration still in **localStorage** early on.
- Documented in `HANDOFF_CONTEXT_PREVIOUS.md` §2–3; PDF allowed Electron but required a **desktop-oriented** app, not a wrapped web page.

**What changed:**
- As features grew (streaming, attachments, identity, notifications, packaging, automated tests), the team needed **first-class Windows APIs** (DPAPI, tray, notifications, WinUI navigation) and a **testable protocol layer** without dragging the full Windows App SDK into unit tests.
- Maintaining a Node/Electron shell plus a separate TS SDK port made CI, signing, and headless testing harder than a single C# solution.

**Final design:**
- Replaced the Electron/React tree with a **WinUI 3** app and a separate **`ConnectOnion.Protocol`** library (C# port of the wire protocol, Ed25519 signing, WebSocket state machine).
- Persistence, view models, and runtime logic live in **`ConnectOnion.WinUIClient.Core`** so they can be tested without WinUI.
- Legacy Electron `frontend/` was removed after the native client reached parity (`197a88d`).

**Why it is better:**
- Matches the brief’s “not just packaged web UI”: native title bar, shell sidebar, settings overlays, and system tray are WinUI-first.  
- Protocol and storage are covered by **~1,600 headless xUnit tests** plus a JS↔C# conformance gate—something the old renderer-centric stack did not support.

**Evidence:**
- Git: `81d38ca` (introduce WinUI 3 / Protocol / SQLite client), `197a88d` (remove legacy Electron frontend).
- Docs: `docs/PROJECT_BRIEF.md` (traceability table), `README.md`, `AGENTS.md` (architecture).
- Prior design: `HANDOFF_CONTEXT_PREVIOUS.md` §1–3, §10.6–10.8.
- Client PDF: scope item “appropriate technology stack”; explicit line that the project must **not** be only a packaged web interface.

**Confidence:** High

---

### Design Change 2: Local conversation storage — JSON envelopes to normalized SQLite messages

**Initial design:**
- In the Electron client, chat history for a session was stored as a **single JSON envelope** per conversation (SDK-shaped `{ state, version }`), keyed like `co:agent:{address}:session:{id}` in **localStorage**, later bulk-copied into a SQLite `settings` row (`HANDOFF_CONTEXT_PREVIOUS.md` §10.7).
- Saving often meant **rewriting the whole conversation blob** when anything changed.

**What changed:**
- Long transcripts, tool timelines, attachments metadata, and search made **whole-envelope rewrites** expensive and hard to query.
- The native client needed **incremental persistence** (per turn), **global transcript search**, and **schema migrations** without losing older user data.

**Final design:**
- SQLite stores **one row per rendered chat bubble** in `messages` (not one JSON blob per conversation), with attachments in `message_attachments`.
- `ConversationRepository.UpsertMessagesAsync` performs **incremental** `INSERT … ON CONFLICT DO UPDATE` for only the rows touched in a turn.
- Legacy Electron-era `envelope_json` rows are **imported once** on upgrade via `SchemaMigrator.ImportLegacyConversationsAsync` (`ConnectOnion.WinUIClient.Core/Data/SchemaMigrator.cs`).
- Global search uses FTS5 (`message_search`, schema v8), not leading-wildcard scans.

**Why it is better:**
- Meets PDF requirements **#4** (local history / multiple sessions) and **#8** (testable persistence) with bounded write cost per turn instead of O(conversation) saves.  
- Supports reopening long chats, background completion, and search without loading entire JSON envelopes into memory.

**Evidence:**
- Git: `1184bd4` (`perf: replace full-conversation save with incremental per-turn upserts`).
- Code: `ConnectOnion.WinUIClient.Core/Data/ConversationRepository.cs`, `SchemaMigrator.cs` (legacy `envelope_json` import).
- Docs: `AGENTS.md` (“Chat message persistence”), `docs/TEST_PLAN.md` (schema v12 / messages model).
- Prior design: `HANDOFF_CONTEXT_PREVIOUS.md` §10.7 (conversation envelope in settings table).

**Confidence:** High

---

### Design Change 3: Interactive agent turns — modal dialogs to inline chat cards

**Initial design:**
- Early WinUI builds handled `ask_user`, `approval_needed`, and `plan_review` as **`ContentDialog` popups** over the chat page.
- Modals disappeared after dismissal and **left little trace in scrollable history**, which diverged from the reference **oo-chat** “chat item feed” model noted in the prior handoff.

**What changed:**
- Approvals, login forms, and plan reviews must stay **visible in the transcript** after the user acts (audit trail, reload safety, PDF **#2** desktop chat UX).
- Modal focus and WinUI theme inheritance also caused **fragile UX** (documented fixes such as `6431ba9` for ContentDialog theming in git history).

**Final design:**
- Interactive turns render as **persistent inline bubbles** in the message list, with live controls (submit, approve/reject, etc.) bound on `ChatMessage` / dedicated card controls under `ConnectOnion.WinUIClient/Controls/Chat/`.
- Resolved answers are stamped at persist time via `AgentSessionManager.RecordInteractiveAnswer` and `ResolveInteractiveCards` so history shows what was chosen after reload.
- Shell modals (Settings, About, shortcuts) remain **full-window overlays**; only **agent interactive turns** moved into the feed (`docs/TEST_PLAN.md` distinguishes these from `ContentDialog`).

**Why it is better:**
- Users see **what the agent asked and how they answered** in context—critical for approvals and onboarding.  
- Aligns with the web reference client’s feed model while still using native WinUI controls, satisfying the brief’s “not just a packaged web UI” without hiding decisions in transient popups.

**Evidence:**
- Git: `3407f46` (commit message explicitly: “replace the ask_user/approval/plan_review **ContentDialog popups** with persistent inline bubbles… matching the reference web client's chat-item-feed model”).
- Code: `Views/ChatPage.xaml`, interactive card controls; `ConnectOnion.WinUIClient.Core/Services/Runtime/AgentSessionManager.cs` (interactive answer buffer).
- Docs: `AGENTS.md` (“Interactive turns”), `docs/TEST_PLAN.md` (in-window overlays vs chat cards).
- Prior context: `HANDOFF_CONTEXT_PREVIOUS.md` §2.3 (oo-chat as UI reference).

**Confidence:** High

---

### Design Change 4: Chat connection lifecycle — page-owned sockets to app-level session runtime

**Initial design:**
- In the first native WinUI builds, **`ChatViewModel` owned the agent connection** for the visible chat page. Leaving the page risked **dropping the WebSocket** or losing in-flight stream state.
- The Electron predecessor had a similar coupling: conversation hooks (`useAgentForHuman` / `useDirectAgentForHuman`) lived **inside the active chat view**, not as an app-wide service (`HANDOFF_CONTEXT_PREVIOUS.md` §3.3).

**What changed:**
- PDF **#3** (streaming/progress) and **#7** (reliability) require turns to **continue while the user navigates**, minimizes to tray, or opens another conversation.
- Tests needed a **headless seam** between UI pages and the WebSocket/run state machine.

**Final design:**
- Introduced **`AgentSessionManager`** + **`ConversationRunRegistry`** + **`AgentConnectionRegistry`** in Core: the app owns connections and turns; **`ChatPage` only subscribes** to run snapshots and replays events.
- User messages are persisted on send; stream events are buffered and projected to bubbles **before** the run is marked complete, so mid-turn navigation loads SQLite history plus live replay without double rendering.
- Refactored behind `IAgentSessionManager` for integration tests (`c1befec`).

**Why it is better:**
- Meets reliability expectations: a reply can finish and notify the user **with no chat page open**.  
- Clear separation improves **maintainability and test coverage** (runtime tests in `tests/ConnectOnion.IntegrationTests/Runtime/`).

**Evidence:**
- Git: `9723c82` (commit message: “replacing **per-page connection ownership in ChatViewModel**”; allows streaming across page switches and tray minimize), `c1befec` (move runtime behind test seam).
- Code: `ConnectOnion.WinUIClient.Core/Services/Runtime/AgentSessionManager.cs`, `Views/ChatPage.xaml.cs` (subscribe-only).
- Docs: `AGENTS.md` (“The run runtime owns connections and turns, not pages”), `docs/CONCURRENCY.md`.
- Prior design: `HANDOFF_CONTEXT_PREVIOUS.md` §3.2–3.3 (chat hooks scoped to React conversation components).

**Confidence:** High

---

## Items considered but not included (lower confidence or no clear “before”)

| Topic | Why omitted |
|--------|-------------|
| Stop button → `INTERRUPT` wire frame (`922711a`) | Clear final design; original “runtime-input stop” is only fully described in one commit message—not in HANDOFF or PDF. |
| `agents.invite_code` removed from SQLite (`de1e42a`, schema v12) | Strong security story, but column was **never written in production** (always NULL)—weak “original design” narrative. |
| Presence probing pull-based vs timer | Documented in `AGENTS.md` as settled design; **insufficient evidence** of an earlier timer-based approach in this repo. |
| Sidebar flat rows / selection refresh (PR #37) | Recent UI polish; **no archived “before” spec** beyond git diff. |

---

## Mapping to PDF core requirements (for polish pass)

| PDF # | Topic | Primary design change above |
|-------|--------|-----------------------------|
| 1 | Agent connection | Change 4 (persistent connections); also Protocol layer in Change 1 |
| 2 | Desktop chat | Change 3 (inline interactive cards) |
| 3 | Streaming / progress | Change 4 (background runs + projection) |
| 4 | Session / history | Change 2 (SQLite message rows) |
| 5 | Agent config / secure storage | Change 1 (native stack + `IdentityStore` DPAPI—detail in `docs/PROJECT_BRIEF.md`, not a separate justification here) |
| 7 | Reliability / reconnect | Change 4 |
| 8 | Testing | Change 1 (headless Core + Protocol tests) |

---

*Generated from repository state and `bb90f03` on `main`. Re-verify commit hashes if history is rebased.*
