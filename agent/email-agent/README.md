# Email Agent Example

This folder contains a standalone ConnectOnion email agent that can be used to test the desktop client with a real local or deployed agent.

The desktop app connects to this agent through the ConnectOnion TypeScript SDK. The agent is developed and deployed separately from the desktop client.

## Structure

```text
agent/email-agent/
  agent.py             Creates the ConnectOnion email agent
  server.py            Exposes the agent as an ASGI app/host
  requirements.txt     Python dependencies
  prompts/             Agent prompt files
  .env.example         Example local environment variables
```

## Local Setup

From the repository root:

```powershell
cd .\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -r .\email-agent\requirements.txt
```

Create a local `.env` file from the example:

```powershell
Copy-Item .\email-agent\.env.example .\email-agent\.env
```

Then fill in the required values.

## ConnectOnion Auth

Run these commands from `agent/email-agent` so credentials are written to this agent's `.env` file:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
co auth
co auth google
```

Keep these credentials local. Do not commit `.env`, `.co/`, API keys, or OAuth tokens.

## Run Locally

Choose one startup method:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1

# Full ConnectOnion host; reads .env and .co/host.yaml (currently port 8000)
python server.py

# Or run the ASGI app directly for local Direct URL testing
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

Use the matching Direct URL in the desktop client:

```text
python server.py  -> http://127.0.0.1:8000
Uvicorn           -> http://127.0.0.1:8010
```

### Relay URL And Endpoint Limits

`python server.py` starts the full ConnectOnion host and uses `relay_url` from
`.co/host.yaml` (currently `wss://oo.openonion.ai`). The host announces an HTTP
and WebSocket endpoint for every detected IP address. Windows machines with
Docker, WSL, VPN, link-local, or other virtual adapters can therefore exceed the
Relay limit of 10 endpoints and show this repeating message:

```text
Relay error: Too many endpoints (max 10)
```

The Relay URL itself is not necessarily wrong. The Relay rejected an ANNOUNCE
payload containing too many discovered endpoints, then the host retried it. The
local `http://127.0.0.1:<port>` and `/ws` endpoints remain available, so a desktop
Direct URL connection can still work without Relay.

Choose the treatment that matches the intended connection:

- For local Direct URL testing, use the Uvicorn command above. It serves the
  ASGI app without starting the Relay registration lifecycle.
- To use `python server.py` as a local-only host, call `host(..., relay_url="")`
  in `server.py` to disable Relay explicitly.
- If Relay discovery is required, filter and deduplicate unused network-adapter
  addresses in ConnectOnion endpoint discovery so no more than five IPs are
  announced (each IP produces two endpoints).
- For a deployed agent with a real HTTPS hostname, set `AGENT_PUBLIC_DOMAIN` to
  that hostname so the host announces only its HTTPS and WSS endpoints.

## Deploy

```powershell
cd .\agent\email-agent
co deploy
```

After deployment, add the deployed agent address or direct URL in the desktop client.

## Example Prompts

- Check my emails
- Search for emails about invoices
- Send an email to john@example.com about the project update
- Reply to email 1 saying I will attend the meeting

## Current Role In This Repository

This agent is a test and demonstration target for the desktop client. Desktop-local data, settings, and conversation history belong in the Electron client storage layer, not in this agent folder.
