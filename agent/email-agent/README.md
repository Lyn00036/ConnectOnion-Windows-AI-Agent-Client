# Email Agent

This agent can run as a standalone HTTP service so the desktop backend can call it over HTTP.

## Install

```powershell
cd D:\COMP9900\connectonion-desktop\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -e D:\COMP9900\connectonion
python -m pip install -r .\email-agent\requirements.txt
```

Create `email-agent\.env`:

```env
OPENAI_API_KEY=your_api_key
OPENAI_BASE_URL=https://api.deepseek.com
EMAIL_AGENT_MODEL=deepseek-v4-flash
```

Use `https://aihubmix.com/v1` instead when the API key is from AIHubMix.

## Run As HTTP Service

```powershell
cd D:\COMP9900\connectonion-desktop\agent\email-agent
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
cd D:\COMP9900\connectonion-desktop\backend
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
cd D:\COMP9900\connectonion-desktop\agent\email-agent
..\venv\Scripts\Activate.ps1
python agent.py
```
