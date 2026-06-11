# ConnectOnion Desktop

ConnectOnion Desktop is a Windows desktop client prototype for managing and using remote ConnectOnion agents.

The planned architecture follows:

- Electron desktop shell
- React + TypeScript + Vite frontend
- FastAPI backend
- PostgreSQL persistence
- Windows Credential Manager / Python keyring for future secret storage

This repository is currently in the foundation stage. The frontend, backend, and PostgreSQL development environment are initialized, but Agent management, chat sessions, credential storage, and packaging are still under development.

## Project Structure

```text
connectonion-desktop/
  backend/      FastAPI backend
  db/           database-related files
  frontend/    Electron + React + Vite desktop frontend
  docker-compose.yml
  connectonion_requirement_solution_mapping.md
```

## Requirements

- Windows
- Node.js 22+
- npm
- Python 3.12+
- Docker Desktop

## Frontend Setup

```bash
cd frontend
npm install
npm run dev
```

Useful scripts:

```bash
npm run dev       # start Vite dev server on 127.0.0.1:3000
npm run electron  # start Electron shell
npm run desktop   # start Vite and Electron together
npm run build     # type-check and build frontend
npm run lint      # run ESLint
```

## Backend Setup

Create and activate a Python virtual environment:

```bash
cd backend
python -m venv venv
.\venv\Scripts\activate
pip install -r requirements.txt
```

Create `backend/.env`:

```env
DATABASE_URL=postgresql+asyncpg://connectonion:connectonion_pass@localhost:5432/connectonion_db
```

Start the backend:

```bash
cd backend
.\venv\Scripts\uvicorn.exe app.main:app --reload --host 127.0.0.1 --port 8000
```

Health check:

```text
http://127.0.0.1:8000/health
```

## Database Setup

Start PostgreSQL with Docker Compose:

```bash
docker compose up -d postgres
```

The default development database is:

```text
host: localhost
port: 5432
database: connectonion_db
user: connectonion
password: connectonion_pass
```

If a local Windows PostgreSQL service already uses port `5432`, either stop that service or change the Docker port mapping and `DATABASE_URL` to another port such as `5433`.

## Current Status

Implemented:

- Electron window bootstrap
- React/Vite frontend scaffold
- FastAPI backend scaffold
- `/health` endpoint
- PostgreSQL Docker Compose setup
- Root `.gitignore`

Not implemented yet:

- Agent CRUD API and UI
- Chat API and desktop chat interface
- Streaming responses
- Session history persistence
- Secure credential storage
- Alembic migrations
- Automated tests
- Windows installer/package build

## Reference

See [connectonion_requirement_solution_mapping.md](./connectonion_requirement_solution_mapping.md) for the requirement-to-solution design.
