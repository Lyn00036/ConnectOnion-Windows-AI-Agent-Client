# ConnectOnion Desktop

ConnectOnion Desktop is a Windows desktop client prototype for connecting to and chatting with external ConnectOnion-style agents.

The current app is a developer prototype with three main parts:

- Electron + React + Vite frontend
- FastAPI backend
- PostgreSQL development database

Agents are not hardcoded into the desktop app and there is no default agent URL. Users must manually add an agent service URL in the frontend before sending messages. The frontend currently stores those manually added agent entries in local browser/Electron storage, and the backend proxies chat requests to the selected agent over HTTP.

## Current Architecture

```text
User
  -> Electron BrowserWindow
  -> React frontend
  -> FastAPI backend
  -> External agent service selected by URL
  -> Agent LLM/tools

PostgreSQL is available for future persistence, but agent registry, chat history, and session persistence are not implemented yet.
```

Generic agent proxy endpoints:

```text
GET  /agents/health?agent_url=<agent-service-url>
POST /agents/chat
```

`POST /agents/chat` requires both the message and the manually selected agent URL:

```json
{
  "message": "hello",
  "agent_url": "https://your-agent.example.com"
}
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
    app/
      main.py           FastAPI app entrypoint, router registration, error handling
      routers/          API route modules
      services/         Backend business logic
      repositories/     Database access boundary for persisted entities
      clients/          External agent HTTP client adapter
      schemas/          Pydantic request/response models
      db/               SQLAlchemy session, base, and ORM models
      core/             Runtime config and application errors
    alembic/            Database migration environment and revisions
    requirements.txt
  frontend/
    src/
      app/              React providers and route table
      layouts/          Desktop shell layout
      pages/            Top-level app views
      features/         Agent, chat, session, and settings modules
      shared/           Shared API client, components, hooks, and utilities
      styles/           Split CSS by layout and feature area
    electron/           Electron shell
    Dockerfile
    nginx.conf
  .github/workflows/ci.yml
  docker-compose.yml
```

## Requirements

- Windows, macOS, or Linux
- Node.js 22+
- npm
- Python 3.12+
- Docker Desktop or Docker Engine

## ConnectOnion Framework Repository

This desktop repository is separate from the ConnectOnion Python framework repository. The desktop app can call any HTTP agent service, while the example agent under `agent/email-agent` depends on the Python framework package.

For local framework development, keep the framework repository next to this desktop repository:

```text
parent-directory/
  connectonion-desktop/
  connectonion/
```

Install the local framework into the agent virtual environment with editable mode:

Windows PowerShell:

```powershell
python -m pip install -e ..\connectonion
```

macOS/Linux:

```bash
python -m pip install -e ../connectonion
```

If you need to modify the official framework, use a fork instead of pushing directly to the official repository. A recommended remote layout inside the framework repository is:

```powershell
cd ..\connectonion
git remote rename origin upstream
git remote add origin git@github.com:<your-github-user>/connectonion.git
git fetch upstream
```

In this setup:

- `upstream` points to the official framework repository.
- `origin` points to your fork.
- Desktop development can keep using `pip install -e ..\connectonion` to test local framework changes immediately.

If a deployed environment should install your fork directly, change the framework dependency to your fork and pin a branch, tag, or commit:

```text
-e git+ssh://git@github.com:<your-github-user>/connectonion.git@<branch-or-commit>#egg=connectonion
```

Do not commit `.env`, `.co`, API keys, or local credential files.

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

Windows PowerShell path example from the repository root:

```powershell
cd .
```

macOS/Linux path example from the repository root:

```bash
cd .
```

## Quick Start: Local Development

Use three terminals.

### 1. Start an agent service

The repository includes an example email agent. It runs independently from the desktop backend.

Windows PowerShell:

```powershell
cd .\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -e ..\..\connectonion
python -m pip install -r .\email-agent\requirements.txt
```

macOS/Linux:

```bash
cd ./agent
python3 -m venv venv
source venv/bin/activate
python -m pip install -e ../../connectonion
python -m pip install -r ./email-agent/requirements.txt
```

Create `agent\email-agent\.env`:

```env
OPENAI_API_KEY=your_api_key
OPENAI_BASE_URL=https://api.deepseek.com
EMAIL_AGENT_MODEL=deepseek-v4-flash
```

If you are using AIHubMix, use this base URL instead:

```env
OPENAI_BASE_URL=https://aihubmix.com/v1
```

The email agent also uses ConnectOnion/OpenOnion credentials for Gmail OAuth.
Run these commands from `agent\email-agent` so the generated credentials are saved to `agent\email-agent\.env`, which is the file loaded by the agent:

Windows PowerShell:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
co auth
co auth google
```

macOS/Linux:

```bash
cd ./agent/email-agent
source ../venv/bin/activate
co auth
co auth google
```

`co auth` creates or refreshes `OPENONION_API_KEY`. `co auth google` opens a browser Google OAuth flow and writes `GOOGLE_ACCESS_TOKEN`, `GOOGLE_REFRESH_TOKEN`, `GOOGLE_TOKEN_EXPIRES_AT`, `GOOGLE_SCOPES`, and `GOOGLE_EMAIL`.

Keep the two key types separate:

- `OPENAI_API_KEY` / `OPENAI_BASE_URL` are for the LLM provider used by `EMAIL_AGENT_MODEL`.
- `OPENONION_API_KEY` is for ConnectOnion managed services, including Google OAuth token refresh.

Then start it:

Windows PowerShell:

```powershell
cd .\agent\email-agent
..\venv\Scripts\Activate.ps1
python -m uvicorn server:app --host 127.0.0.1 --port 8010
```

macOS/Linux:

```bash
cd ./agent/email-agent
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
cd .\backend
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
python -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

macOS/Linux:

```bash
cd ./backend
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
cd .\frontend
npm install
npm run dev
```

macOS/Linux:

```bash
cd ./frontend
npm install
npm run dev
```

Open:

```text
http://127.0.0.1:3000
```

In the Agent Console, manually add the agent service you want to use. The frontend does not ship with a default agent and does not currently provide a connection-test button; sending a chat message is the active end-to-end flow.

```text
Name: Your Agent
URL:  <agent-service-url>
```

## Electron Desktop Shell

The Electron shell loads the same Vite frontend.

Windows PowerShell:

```powershell
cd .\frontend
npm run desktop
```

macOS/Linux:

```bash
cd ./frontend
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
cd .
docker compose up --build
```

macOS/Linux:

```bash
cd .
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

If an agent is running on the Windows host machine while frontend/backend are in Docker, manually add this kind of URL in the Agent Console:

```text
http://host.docker.internal:8010
```

Do not use `http://127.0.0.1:8010` from inside Docker for a host-running agent. Inside a container, `127.0.0.1` means that container.

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

Database schema changes are managed by Alembic migrations:

```text
backend/alembic/versions/
```

The backend container runs `alembic upgrade head` before starting FastAPI, so Compose applies pending migrations automatically when the backend service starts.

Start the Docker database only:

Windows PowerShell:

```powershell
docker compose up -d postgres
docker compose ps
```

macOS/Linux:

```bash
docker compose up -d postgres
docker compose ps
```

Apply migrations through the backend container:

```powershell
docker compose up -d backend
docker exec -it connectonion-postgres psql -U connectonion -d connectonion_db -c "\dt"
```

To run migrations from a local backend virtual environment instead:

```powershell
cd .\backend
.\venv\Scripts\Activate.ps1
alembic upgrade head
```

If a local Windows PostgreSQL service already uses port `5432`, either stop it or change the Compose port mapping and local `DATABASE_URL` to another port such as `5433`.

For local PostgreSQL without Docker, create the user and database first:

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -h localhost -p 5432
```

Then run:

```sql
CREATE USER connectonion WITH PASSWORD 'connectonion_pass';
CREATE DATABASE connectonion_db OWNER connectonion;
GRANT ALL PRIVILEGES ON DATABASE connectonion_db TO connectonion;
```

If the user already exists, reset the password:

```sql
ALTER USER connectonion WITH PASSWORD 'connectonion_pass';
```

Local backend `.env` should point to localhost:

```env
DATABASE_URL=postgresql+asyncpg://connectonion:connectonion_pass@localhost:5432/connectonion_db
```

Initialize or upgrade the schema through Alembic:

```powershell
cd .\backend
.\venv\Scripts\Activate.ps1
alembic upgrade head
```

The compatibility helper still delegates to Alembic:

```powershell
python -m app.db.init_db
```

After changing ORM models in `backend/app/db/models.py`, create a migration:

```powershell
alembic revision --autogenerate -m "describe schema change"
alembic upgrade head
```

Test that the backend can really connect to the database:

```powershell
python -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

In another terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health/db
```

Expected response:

```text
status   : ok
database : connectonion_db
user     : connectonion
```

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
- Feature-based React frontend structure
- Manual add/select/remove agent URLs in frontend local storage
- Generic FastAPI agent proxy
- Basic chat send/response flow through the selected agent URL
- Unified backend error response format
- Backend database connection and `/health/db` check
- SQLAlchemy ORM models and Alembic migrations
- Example email agent HTTP service
- PostgreSQL Docker Compose service
- Frontend/backend Dockerfiles and nginx proxy
- CI workflow

## Not Implemented Yet

- Persistent agent registry in PostgreSQL
- Backend Agent CRUD APIs
- Frontend connection-test UI
- Chat/session history persistence
- Real Sessions, Agents, and Settings pages beyond placeholders
- Streaming responses
- Secure credential storage
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
