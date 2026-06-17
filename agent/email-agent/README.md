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

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

Use this direct URL in the desktop client:

```text
http://127.0.0.1:8010
```

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
