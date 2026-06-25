# Browser Agent

This ConnectOnion agent exposes browser automation through HTTP and WebSocket
host endpoints so it can be used as a conversational agent from the desktop
client.

## Local Setup

From the repository root:

```powershell
cd .\agent
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r .\requirements.txt
python -m playwright install chromium
Copy-Item .\browser-agent\.env.example .\browser-agent\.env
```

## Run The Host

```powershell
cd .\agent\browser-agent
..\.venv\Scripts\Activate.ps1
python agent.py
```

The host provides `GET /health`, `GET /info`, `POST /input`, and `WS /ws`.
`BROWSER_AGENT_PORT` selects the local port, and an empty
`BROWSER_AGENT_RELAY_URL` keeps the host in local-only mode.

The browser is shared between turns so a follow-up message can continue from
the current page. Run only one active browser conversation at a time.
