# Email Agent

This agent can run as a standalone HTTP service so the desktop backend can call it over HTTP.

## Install

Run these commands from the `connectonion-desktop` repository root:

```powershell
cd .\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -e ..\..\connectonion
python -m pip install -r .\email-agent\requirements.txt
```

`..\..\connectonion` is the local ConnectOnion Python framework repository relative to the `agent` directory. If you are changing the framework itself, keep the official repository as `upstream`, push your changes to your own fork as `origin`, and keep using editable install locally while testing this agent.

Create `email-agent\.env`:

```env
OPENAI_API_KEY=your_api_key
OPENAI_BASE_URL=https://api.deepseek.com
EMAIL_AGENT_MODEL=deepseek-v4-flash
```

Use `https://aihubmix.com/v1` instead when the API key is from AIHubMix.

## Configure ConnectOnion And Gmail

The email agent uses two kinds of credentials:

- `OPENAI_API_KEY` / `OPENAI_BASE_URL` for the LLM provider used by `EMAIL_AGENT_MODEL`
- `OPENONION_API_KEY` plus `GOOGLE_*` OAuth values for Gmail tools

Run the `co` commands from this `email-agent` directory so credentials are written to `email-agent\.env`:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
co auth
co auth google
```

`co auth` creates or refreshes `OPENONION_API_KEY`. `co auth google` opens a browser Google OAuth flow and writes these values:

```env
GOOGLE_ACCESS_TOKEN=...
GOOGLE_REFRESH_TOKEN=...
GOOGLE_TOKEN_EXPIRES_AT=...
GOOGLE_SCOPES=...
GOOGLE_EMAIL=...
```

The agent loads `email-agent\.env` on startup. Restart `uvicorn` after changing API keys or Gmail credentials.

## Run As HTTP Service

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

Health check:

```text
http://127.0.0.1:8010/health
```

Chat endpoint:

```http
POST http://127.0.0.1:8010/chat
Content-Type: application/json

{"message":"Check my emails"}
```

## Run Through Desktop Backend

Start the desktop backend:

```powershell
cd .\backend
.\venv\Scripts\Activate.ps1
python -m uvicorn app.main:app --host 127.0.0.1 --port 8000
```

The frontend lets you manually add this service URL:

```text
http://127.0.0.1:8010
```

The desktop backend exposes generic agent proxy endpoints:

```text
GET  http://127.0.0.1:8000/agents/health?agent_url=http://127.0.0.1:8010
POST http://127.0.0.1:8000/agents/chat
```

## CLI Mode

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
python agent.py
```
