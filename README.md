# ConnectOnion Desktop

ConnectOnion Desktop is a Windows desktop client prototype for connecting to and chatting with external ConnectOnion-style agents.

The current app is a developer prototype with three main parts:

- Electron + React + Vite frontend
- FastAPI backend
- PostgreSQL development database

Agents are not hardcoded into the desktop app. The frontend lets users manually add any agent service URL, and the backend proxies chat requests to that agent over HTTP.

## Current Architecture

```text
User
  -> Electron / React frontend
  -> FastAPI backend
  -> External agent service by URL
  -> Agent LLM/tools

PostgreSQL is available for future persistence, but chat/session persistence is not implemented yet.
```

Generic agent proxy endpoints:

```text
GET  /agents/health?agent_url=http://127.0.0.1:8010
POST /agents/chat
```

The expected agent service contract is:

```text
GET  /health
POST /chat
```

`POST /chat` should accept:

```json
{
  "message": "hello"
}
```

and return:

```json
{
  "response": "agent reply"
}
```

## Project Structure

```text
connectonion-desktop/
  agent/
    email-agent/        Example standalone HTTP agent
  backend/
    app/main.py         FastAPI backend and generic agent proxy
    requirements.txt
  frontend/
    src/                React Agent Console UI
    electron/           Electron shell
    Dockerfile
    nginx.conf
  db/
  .github/workflows/ci.yml
  docker-compose.yml
```

## Requirements

- Windows, macOS, or Linux
- Node.js 22+
- npm
- Python 3.12+
- Docker Desktop or Docker Engine

## Cross-Platform Command Notes

Most commands below are shown with Windows PowerShell paths because the project is currently developed on Windows. Use the matching shell form for your platform.

Windows PowerShell virtual environment activation:

```powershell
.\venv\Scripts\Activate.ps1
```

macOS/Linux virtual environment activation:

```bash
source venv/bin/activate
```

Windows path example:

```powershell
cd D:\COMP9900\connectonion-desktop
```

macOS/Linux path example:

```bash
cd ~/COMP9900/connectonion-desktop
```

## Quick Start: Local Development

Use three terminals.

### 1. Start an agent service

The repository includes an example email agent. It runs independently from the desktop backend.

Windows PowerShell:

```powershell
cd D:\COMP9900\connectonion-desktop\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -e D:\COMP9900\connectonion
python -m pip install -r .\email-agent\requirements.txt
```

macOS/Linux:

```bash
cd ~/COMP9900/connectonion-desktop/agent
python3 -m venv venv
source venv/bin/activate
python -m pip install -e ~/COMP9900/connectonion
python -m pip install -r ./email-agent/requirements.txt
```

Create `agent\email-agent\.env`:

```env
OPENAI_API_KEY=your_api_key
OPENAI_BASE_URL=https://api.deepseek.com
EMAIL_AGENT_MODEL=deepseek-v4-flash
```

Then start it:

Windows PowerShell:

```powershell
cd D:\COMP9900\connectonion-desktop\agent\email-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

macOS/Linux:

```bash
cd ~/COMP9900/connectonion-desktop/agent/email-agent
source ../venv/bin/activate
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

Health check:

```text
http://127.0.0.1:8010/health
```

### 2. Start the backend

Windows PowerShell:

```powershell
cd D:\COMP9900\connectonion-desktop\backend
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
python -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

macOS/Linux:

```bash
cd ~/COMP9900/connectonion-desktop/backend
python3 -m venv venv
source venv/bin/activate
python -m pip install -r requirements.txt
python -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

Health check:

```text
http://127.0.0.1:8000/health
```

API documentation for frontend developers:

```text
http://127.0.0.1:8000/docs
```

This opens the FastAPI Swagger UI, where frontend developers can inspect request and response schemas for backend endpoints such as `/agents/health` and `/agents/chat`.

### 3. Start the frontend

Windows PowerShell:

```powershell
cd D:\COMP9900\connectonion-desktop\frontend
npm install
npm run dev
```

macOS/Linux:

```bash
cd ~/COMP9900/connectonion-desktop/frontend
npm install
npm run dev
```

Open:

```text
http://127.0.0.1:3000
```

In the Agent Console, add:

```text
Name: Local Agent
URL:  http://127.0.0.1:8010
```

## Electron Desktop Shell

The Electron shell loads the same Vite frontend.

Windows PowerShell:

```powershell
cd D:\COMP9900\connectonion-desktop\frontend
npm run desktop
```

macOS/Linux:

```bash
cd ~/COMP9900/connectonion-desktop/frontend
npm run desktop
```

Useful frontend scripts:

```text
npm run dev       Start Vite dev server on 127.0.0.1:3000
npm run electron  Start Electron shell
npm run desktop   Start Vite and Electron together
npm run lint      Run ESLint
npm run build     Type-check and build frontend
```

## Docker Compose

Docker Compose starts PostgreSQL, backend, and frontend. Agents can run separately.

Windows PowerShell:

```powershell
cd D:\COMP9900\connectonion-desktop
docker compose up --build
```

macOS/Linux:

```bash
cd ~/COMP9900/connectonion-desktop
docker compose up --build
```

Open:

```text
http://127.0.0.1:3000
```

Backend Swagger docs are still available directly through the backend port:

```text
http://127.0.0.1:8000/docs
```

Inside Docker, frontend requests are proxied by nginx:

```text
/agents/health -> backend:8000/agents/health
/agents/chat   -> backend:8000/agents/chat
```

If the agent is running on the Windows host machine while frontend/backend are in Docker, use this agent URL:

```text
http://host.docker.internal:8010
```

Do not use `http://127.0.0.1:8010` from inside Docker for a host-running agent. Inside a container, `127.0.0.1` means that container.

Optional override:

Windows PowerShell:

```powershell
$env:DEFAULT_AGENT_URL="http://host.docker.internal:8010"
docker compose up --build
```

macOS/Linux:

```bash
DEFAULT_AGENT_URL="http://host.docker.internal:8010" docker compose up --build
```

## Database

The Docker Compose PostgreSQL service uses:

```text
host: localhost
port: 5432
database: connectonion_db
user: connectonion
password: connectonion_pass
```

Backend container database URL:

```text
postgresql+asyncpg://connectonion:connectonion_pass@postgres:5432/connectonion_db
```

If a local Windows PostgreSQL service already uses port `5432`, either stop it or change the Compose port mapping and local `DATABASE_URL` to another port such as `5433`.

## CI

GitHub Actions workflow:

```text
.github/workflows/ci.yml
```

It runs on pushes, pull requests, and manual dispatch:

- Backend dependency install and Python compile check
- Frontend `npm ci`, lint, and build
- Docker Compose config validation and image build

## Implemented

- Electron window bootstrap
- React Agent Console UI
- Manual add/select/remove agent URLs in the frontend
- Generic FastAPI agent proxy
- Example email agent HTTP service
- PostgreSQL Docker Compose service
- Frontend/backend Dockerfiles and nginx proxy
- CI workflow

## Not Implemented Yet

- Persistent agent registry in PostgreSQL
- Chat/session history persistence
- Streaming responses
- Secure credential storage
- Alembic migrations
- User authentication
- Windows installer/package build
- Production CD deployment

## Security Notes

- Do not commit `.env` files.
- Do not commit `.co/` agent keys or local agent state.
- API keys should stay in local environment files or deployment secrets.
- Frontend should call the desktop backend, not third-party LLM APIs directly.

## References

- [Requirement-to-solution mapping](./connectonion_requirement_solution_mapping.md)
- [Email agent example](./agent/email-agent/README.md)
