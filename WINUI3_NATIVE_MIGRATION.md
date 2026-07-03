# ConnectOnion Desktop WinUI3 Native Migration Plan

This document explains how to migrate the current `frontend` project from `React + Vite + TypeScript + Electron` to a fully native Windows application built with `C# + .NET + WinUI3`.

## Current Project State

The current frontend project is located at:

```text
frontend/
```

Current technology stack:

```text
React
Vite
TypeScript
Electron
Tailwind CSS
Zustand
ConnectOnion TypeScript SDK
localStorage
```

Key directories:

```text
frontend/electron/        Electron main process, preload, and window IPC
frontend/src/pages/       React pages
frontend/src/features/    chat, agents, sessions, and settings feature modules
frontend/src/shared/      desktop window API, shared components, and utilities
```

After the native migration, the app should no longer use Electron, React, Vite, or WebView2 to host the UI. The existing React code should be treated as a reference for business behavior and UI requirements.

## Target Architecture

Target technology stack:

```text
C#
.NET
WinUI3
Windows App SDK
XAML
System.Text.Json
HttpClient
ClientWebSocket
```

Recommended new project location:

```text
connectonion-desktop/
  windows_desktop/
    ConnectOnion.Desktop/
```

Recommended directory structure:

```text
ConnectOnion.Desktop/
  App.xaml
  MainWindow.xaml

  Views/
    ChatPage.xaml
    AgentsPage.xaml
    SessionsPage.xaml
    SettingsPage.xaml

  ViewModels/
    ChatViewModel.cs
    AgentsViewModel.cs
    SessionsViewModel.cs
    SettingsViewModel.cs

  Models/
    AgentConfig.cs
    SessionSummary.cs
    ChatMessage.cs
    PreferencesSnapshot.cs

  Services/
    AgentConnectionService.cs
    AgentRepository.cs
    SessionRepository.cs
    ConversationRepository.cs
    PreferencesRepository.cs
    WindowService.cs
```

## Environment Setup

The machine already has `.NET SDK 9/10` installed, but `dotnet --info` shows that the WinUI workload is not installed.

Required setup:

```text
Visual Studio 2026
WinUI application development workload
Windows Developer Mode
Windows App SDK Stable
```

Official documentation:

```text
https://learn.microsoft.com/en-us/windows/apps/get-started/start-here
https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels
```

## Migration Principles

1. Do not directly rewrite the existing React project. Create a new native WinUI3 project first.
2. Keep the old project as a behavior reference until the native version reaches feature parity.
3. Migrate module by module instead of doing one large rewrite.
4. Migrate stable local features before migrating chat connectivity.
5. The `connectonion` TypeScript SDK must be ported to C#. Its full source is available at `D:\COMP9900\connectonion-ts\src`, so this is a faithful port, not a reverse-engineering task. The one hard prerequisite is byte-identical Ed25519 canonical-JSON signing (see Step 9).

## Step 1: Create The Native WinUI3 Project

Create the project in Visual Studio:

```text
Project type: WinUI Blank App (Packaged)
Language: C#
Project name: ConnectOnion.Desktop
```

Recommended location:

```text
D:\COMP9900\connectonion-desktop\windows_desktop
```

After creation, run the empty project first to confirm that the WinUI3 environment works correctly.

## Step 2: Migrate Data Models

Start by translating the TypeScript types into C# models.

Source files:

```text
frontend/src/features/agents/types.ts
frontend/src/features/sessions/types.ts
frontend/src/features/chat/types.ts
frontend/src/features/settings/preferencesStorage.ts
```

Example:

```csharp
public sealed class AgentConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string? DirectUrl { get; set; }
}
```

Create these models first:

```text
AgentConfig
AgentHealthResponse
SessionSummary
ChatMessage
OnboardSubmission
PreferencesSnapshot
```

## Step 3: Migrate Local Persistence

The current project uses `localStorage`.

For the first native WinUI3 version, use JSON files. Do not introduce SQLite at the beginning unless the data volume requires it.

Recommended storage location:

```text
%AppData%\ConnectOnion\
  agents.json
  sessions.json
  preferences.json
  conversations\
```

Migration mapping:

```text
frontend/src/features/agents/agentStorage.ts
  -> AgentRepository.cs

frontend/src/features/sessions/sessionStorage.ts
  -> SessionRepository.cs

frontend/src/features/chat/conversationStorage.ts
  -> ConversationRepository.cs

frontend/src/features/settings/preferencesStorage.ts
  -> PreferencesRepository.cs
```

The first repository implementation only needs to support:

```text
Read
Create
Update
Delete
Persist selected id
```

## Step 4: Create The Main Window And Navigation

Current React pages:

```text
frontend/src/pages/ChatPage.tsx
frontend/src/pages/AgentsPage.tsx
frontend/src/pages/SessionsPage.tsx
frontend/src/pages/SettingsPage.tsx
```

Corresponding WinUI3 pages:

```text
Views/ChatPage.xaml
Views/AgentsPage.xaml
Views/SessionsPage.xaml
Views/SettingsPage.xaml
```

Use `NavigationView` with a `Frame` in the main window.

Example:

```xml
<NavigationView>
    <NavigationView.MenuItems>
        <NavigationViewItem Content="Chat" Tag="chat" />
        <NavigationViewItem Content="Agents" Tag="agents" />
        <NavigationViewItem Content="Sessions" Tag="sessions" />
        <NavigationViewItem Content="Settings" Tag="settings" />
    </NavigationView.MenuItems>

    <Frame x:Name="ContentFrame" />
</NavigationView>
```

## Step 5: Migrate The Settings Page First

The Settings page has the lowest risk and should be the first native page to migrate.

Migrate:

```text
Theme
Sidebar visible
Message font size
Shortcut overrides
```

Target files:

```text
SettingsPage.xaml
SettingsViewModel.cs
PreferencesRepository.cs
```

Completion criteria:

```text
The page displays current settings
Setting changes are written to preferences.json
Settings persist after restarting the app
```

## Step 6: Migrate The Agents Page

Source modules:

```text
frontend/src/features/agents/
frontend/src/pages/AgentsPage.tsx
```

Migrate:

```text
Agent list
Create agent
Edit agent
Delete agent
Selected agent
Connection test
```

Connection test source:

```text
frontend/src/features/agents/connectionTest.ts
```

Direct URL test behavior:

```text
GET {directUrl}/health
```

C# implementation should use:

```text
HttpClient
CancellationTokenSource
System.Text.Json
```

Completion criteria:

```text
Can create an agent
Can edit an agent
Can delete an agent
Can persist the selected agent
Can test a Direct URL health endpoint
```

## Step 7: Migrate The Sessions Page

Source modules:

```text
frontend/src/features/sessions/
frontend/src/pages/SessionsPage.tsx
```

Migrate:

```text
Session list
Active session
Create session
Delete session
Delete sessions by agent
Touch/update session timestamp
```

Completion criteria:

```text
Can create sessions
Can switch the active session
Can delete sessions
Session list persists after restarting the app
```

## Step 8: Migrate The Chat UI

Build the UI first before connecting it to a real agent.

Source modules:

```text
frontend/src/features/chat/components/
frontend/src/pages/ChatPage.tsx
```

Migrate:

```text
Message list
User message bubble
Agent message bubble
Input box
Send button
Processing state
Error state
Reconnect button
```

Completion criteria:

```text
Can enter a message
Can display the user message in the list
Can simulate an agent reply
Can save the conversation envelope
Can restore the conversation after restarting the app
```

## Step 9: Port The ConnectOnion Protocol To C#

C# cannot use the TypeScript SDK directly, so this layer must be reimplemented.
This is NOT reverse-engineering an unknown protocol: the full protocol is
readable in the SDK source. Treat Step 9 as a faithful port of the following
files, not as a research task.

Reference source (the real protocol lives here):

```text
D:\COMP9900\connectonion-ts\src\address.ts              Ed25519 identity, key storage, canonical-JSON signing
D:\COMP9900\connectonion-ts\src\connect\auth.ts         ensureKeys + signPayload envelope shape
D:\COMP9900\connectonion-ts\src\connect\endpoint.ts     relay lookup + endpoint resolution
D:\COMP9900\connectonion-ts\src\connect\remote-agent.ts WebSocket state machine (CONNECT/INPUT/OUTPUT/reconnect)
D:\COMP9900\connectonion-ts\src\connect\chat-item-mapper.ts  stream event -> UI item reduction
D:\COMP9900\connectonion-ts\src\connect\types.ts        message/state type definitions
```

Current chat core that wraps the SDK (behavior reference, not to port literally):

```text
frontend/src/features/chat/useDirectAgentForHuman.ts
```

### Confirmed protocol facts (no further investigation needed)

```text
Transport          Persistent WebSocket, JSON text frames (NOT HTTP SSE, NOT token streaming)
Direct URL         Connect to ws(s)://{directUrl}/ws
Relay resolution   GET {relay}/api/relay/agents/{address} -> probe each endpoint /info -> match address -> {httpUrl, wsUrl}
Handshake          send CONNECT {timestamp, to, signed} -> receive CONNECTED {session_id, status, server_newer?, session?, chat_items?}
Send input         send INPUT {input_id, prompt, images?, files?} on the SAME socket
Streaming events   discrete JSON frames: thinking, llm_call, llm_result, tool_call, tool_result,
                   assistant, agent_image, intent, eval, compact, tool_blocked, files_received
Interactive        ask_user, approval_needed, plan_review, ONBOARD_REQUIRED, ulw_turns_reached
Client responses   ASK_USER_RESPONSE, APPROVAL_RESPONSE, PLAN_REVIEW_RESPONSE, ULW_RESPONSE, ONBOARD_SUBMIT, mode_change
Turn end           OUTPUT {result, session?, chat_items?} resolves the pending input()
Keepalive          server PING -> client PONG; 60s of silence closes the socket
Session status     send SESSION_STATUS {session:{session_id}} over WS -> SESSION_STATUS reply
Reconnect          close socket, send CONNECT {session_id, session, signed}, 60s establish deadline
```

### Correctness traps (address these before writing the network layer)

1. Canonical-JSON signing is the one thing that silently breaks everything.
   The SDK signs `Object.keys(payload).sort()` then `JSON.stringify` with NO
   whitespace, UTF-8 bytes, Ed25519, hex signature (see `address.ts`
   `canonicalJSON` / `sign` and `auth.ts` `signPayload`). The C# serializer must
   produce a BYTE-IDENTICAL string (sorted keys, no spaces, matching number
   format). Write a cross-language conformance test FIRST: sign the same payload
   in the JS SDK and in C#, assert the hex signatures are equal. Do this before
   any WebSocket code.

2. Ed25519 is not in the .NET BCL. Add `NSec.Cryptography` or `BouncyCastle`.
   Key format: `address = "0x" + hex(32-byte public key)`, private key is a
   32-byte raw seed (Node path). The current Electron app uses the BROWSER path
   (`generateBrowser`/`signBrowser`, tweetnacl, 64-byte secretKey = seed+public),
   persisted in `localStorage` under `connectonion_keys`. Decide how C# stores
   keys and whether to migrate existing localStorage keys.

3. `input()` has NO overall timeout by design — `ask_user` legitimately waits on
   the human indefinitely. Only the 60s PING-silence monitor detects a dead
   socket. Do not attach a request/response timeout to a turn.

4. `chat-item-mapper.ts` plus `_mergeServerChatItems`, the `__optimistic__`
   placeholder, and interjection handling in `remote-agent.ts` are stateful UI
   reduction logic. Port them faithfully and unit-test them — divergence here
   desyncs the chat UI from server state even when the network layer is correct.

### Implementation targets

```text
Identity/signing service  <- address.ts + auth.ts   (do this + its conformance test first)
AgentConnectionService.cs <- remote-agent.ts + endpoint.ts
ChatItem mapping          <- chat-item-mapper.ts
```

Likely APIs:

```csharp
ClientWebSocket
HttpClient
System.Text.Json
TaskCompletionSource<T>   // replaces the SDK's _inputResolve / _connectResolve promises
NSec.Cryptography         // or BouncyCastle, for Ed25519
```

Completion criteria:

```text
C# and JS produce identical signatures for the same payload (conformance test)
Can connect to a Direct URL agent
Can send user input and receive the OUTPUT reply
Can render streaming events (thinking / tool_call / tool_result / agent_image)
Can handle ask_user / approval_needed interactive turns
Can reconnect a session and merge server chat_items
Can save and restore session state
```

## Step 10: Packaging And Distribution

WinUI3 Packaged apps should be distributed as MSIX packages.

Confirm:

```text
App icon
App display name
Publisher identity
Version
Runtime dependencies
File system capabilities
Network capabilities
```

## Recommended Implementation Order

```text
1. Create the WinUI3 solution
2. Create models
3. Create JSON repositories
4. Create MainWindow + NavigationView
5. Migrate SettingsPage
6. Migrate AgentsPage
7. Migrate SessionsPage
8. Migrate Chat UI
9. Port ConnectOnion agent communication from connectonion-ts (signing conformance test first)
10. Package as MSIX
11. Remove the old Electron/React implementation
```

## Risk List

### High Risk

```text
Ed25519 canonical-JSON signing producing byte-identical signatures to the JS SDK (blocks everything else)
chat-item-mapper + server chat_items merge (stateful UI reduction — silent desync if wrong)
```

The protocol itself (streaming, reconnect, relay resolution) is fully specified
in the SDK source and is now a MEDIUM-risk port, not a high-risk unknown.
See Step 9 for the confirmed protocol facts.

### Medium Risk

```text
Conversation persistence structure
UI state restoration
Shortcut system
Window title bar and native window behavior
```

### Low Risk

```text
Settings page
Agents CRUD
Sessions CRUD
JSON file persistence
NavigationView page navigation
```

## Conclusion

Migrating to a fully native Windows app is not just replacing the Electron shell. It means building a new WinUI3 client.

The existing React project is still valuable because it defines:

```text
Existing features
Data structures
Page behavior
How the ConnectOnion SDK is currently used
```

The recommended path is:

```text
First complete the native UI and local persistence
Then focus on the ConnectOnion communication protocol
Finally remove the old frontend implementation
```
