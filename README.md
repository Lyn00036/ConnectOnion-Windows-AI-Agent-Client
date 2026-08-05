# ConnectOnion Desktop

<p align="center">
  <img src="./ConnectOnion.WinUIClient/Assets/icon.png" alt="ConnectOnion Desktop" width="96" />
</p>

ConnectOnion Desktop is a native Windows app for chatting with ConnectOnion-compatible agents.
It keeps conversations and your ConnectOnion identity on your PC, supports direct and relay
connections, and presents tool activity and approval requests inside the conversation.

## Download and run

ConnectOnion Desktop currently ships as an **x64 portable ZIP** for Windows 10 version 1809 or
newer. It includes the required .NET and Windows App SDK runtimes, so you do not need to install
them separately.

1. Open the [latest release](../../releases/latest).
2. Download `ConnectOnion.Desktop-v<version>-x64-portable.zip` and `SHA256SUMS.txt`.
3. Extract the entire ZIP to a folder. Do not run the app from inside the archive.
4. Run `ConnectOnion.WinUIClient.exe`.

The current portable release is not an MSIX installer. Windows may show a reputation warning for
a newly downloaded build; continue only when the file came from this repository's Releases page
and its checksum matches `SHA256SUMS.txt`.

To verify the download in PowerShell:

```powershell
Get-FileHash .\ConnectOnion.Desktop-v<version>-x64-portable.zip -Algorithm SHA256
```

## First-time setup

### 1. Save your recovery phrase

On first launch, the app creates a local ConnectOnion identity and shows its 12-word recovery
phrase. Write the words down in order and keep them offline.

Anyone with the phrase controls the identity. Never share it or paste it into a conversation.
You can reveal it again later under **Settings → Identity → Show recovery phrase**.

### 2. Add an agent

Select **Add Agent** from the Home page or sidebar, then enter the details supplied by the agent
operator:

| Field | What to enter |
|---|---|
| Agent name | A name used only on this PC |
| Agent address | The agent's `0x…` ConnectOnion address |
| Direct URL | Optional HTTPS/HTTP address for a directly hosted agent |
| Invite code | Optional code required by some agents during onboarding |

Use **Test connection** to check the endpoint, then select **Add agent**. Choose the agent from the
sidebar, start a conversation, and send a message.

## What you can do

- Chat with multiple agents and keep separate conversation histories.
- Connect through a direct URL or ConnectOnion relay lookup.
- Send images and files when the selected agent supports them.
- Review tool activity, approval requests, plans, and interactive questions inline.
- Search conversation titles and transcript content.
- Use speech-to-text dictation for message input.
- Switch between English and Simplified Chinese, light and dark themes, and three text sizes.
- Customize keyboard shortcuts and keep the app available from the system tray.
- Review per-model token usage and activity history in Settings.

## Your data and identity

The portable app stores its data under:

```text
%APPDATA%\ConnectOnion\
```

This folder contains the SQLite conversation database, agent icons, received-image cache, and
diagnostic logs. Removing or replacing the extracted application folder does not remove this
data.

The private identity is encrypted for your current Windows account. Copying the database to
another computer is **not** an identity backup because the other Windows account cannot decrypt
it. Restore the identity on another device with the recovery phrase from **Settings → Identity**.

## Updating or removing the app

To update:

1. Close ConnectOnion Desktop from **File → Exit** or the tray menu.
2. Download and extract the new release to a new folder.
3. Run the new `ConnectOnion.WinUIClient.exe`.

Your existing data under `%APPDATA%\ConnectOnion` is upgraded in place when necessary.

To remove the portable app, delete its extracted folder. If you also want to erase conversations,
agent settings, logs, and the local identity, delete `%APPDATA%\ConnectOnion` separately—but first
save the recovery phrase for any identity you may want to restore.

## Troubleshooting

### The app does not start

- Confirm that Windows is 64-bit Windows 10 version 1809 or newer.
- Extract every file from the ZIP and keep the files together.
- Check whether Windows Security or another security product quarantined a file.
- Review the newest log under `%APPDATA%\ConnectOnion\logs`.

### An agent does not connect

- Open the agent details and run **Test connection** again.
- Confirm the address, Direct URL, and invite code with the agent operator.
- If a Direct URL is configured, verify that the host is reachable from your network.
- A grey or amber status can mean the agent is offline or temporarily unavailable.

### Windows notifications are unavailable

In-app alerts continue to work while the window is open. Background Windows notifications depend
on the Windows notification service available on the PC and may be unavailable for a portable
deployment.

### My identity changed after moving data

Identity encryption is tied to the Windows account that created it. Use **Settings → Identity →
Restore** and enter the original recovery phrase instead of copying `connectonion.db`.

For problems that remain, open an [issue](../../issues) and attach the app version, Windows
version, reproduction steps, and relevant log lines. Remove addresses, invite codes, API keys,
recovery phrases, private keys, and conversation content before sharing a log.

## For developers

Architecture, build instructions, test commands, trimming policy, CI, and release engineering are
documented in the [developer guide](./docs/DEVELOPMENT.md). The complete documentation index is in
[`docs/README.md`](./docs/README.md).

ConnectOnion protocol implementations:

- [Python](https://github.com/openonion/connectonion)
- [TypeScript](https://github.com/openonion/connectonion-ts)
