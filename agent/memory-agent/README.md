# Memory Agent Example

A standalone ConnectOnion assistant with persistent memory, used to test the
desktop client with a real local or deployed agent. It is adapted from the
ConnectOnion `examples/memory_agent.py` example into the same hostable pattern
as the other agents in this repository.

The agent saves facts the user shares into a local memory directory and recalls
them on later turns. The desktop app connects to it through the ConnectOnion
TypeScript SDK.

## Structure

```text
agent/
  requirements.txt       Shared Python dependencies for agents
  memory-agent/
    agent.py             Creates and hosts the ConnectOnion memory agent
    prompts/             Agent prompt files
    .env.example         Example local environment variables
    memories/            Local memory files (created at runtime, git-ignored)
```

## Local Setup

From the repository root, install the shared dependencies once:

```powershell
cd .\agent
python -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install -r .\requirements.txt
```

Create a local `.env` file from the example:

```powershell
Copy-Item .\memory-agent\.env.example .\memory-agent\.env
```

## ConnectOnion Auth

The default model is a managed `co/` model, so authenticate once to obtain
`OPENONION_API_KEY`. Run this from `agent/memory-agent` so credentials are
written to this agent's `.env`:

```powershell
cd .\agent\memory-agent
..\venv\Scripts\Activate.ps1
co auth
```

To use your own provider key instead, set `MEMORY_AGENT_MODEL` (for example
`gpt-4o-mini`) and the matching provider key in `.env`.

## Run Locally

Choose one startup method:

```powershell
cd .\agent\memory-agent
..\venv\Scripts\Activate.ps1

# Full ConnectOnion host (reads .env; Relay disabled by default)
python agent.py

# Or run the ASGI app directly for local Direct URL testing
python -m uvicorn agent:app --host 127.0.0.1 --port 8021
```

Use the matching Direct URL in the desktop client:

```text
python agent.py   -> http://127.0.0.1:8000 (set MEMORY_AGENT_PORT to change)
Uvicorn           -> http://127.0.0.1:8021
```

When running several local agents at once, give each one a different port.

## Memory Storage

Memories are written to `agent/memory-agent/memories/` by default. The folder is
git-ignored. Change the location with `MEMORY_AGENT_DIR` in `.env`.

## Example Prompts

- Remember that Alice from TechCorp prefers email and has a $50k budget
- What do I know about Alice?
- What memories do I have?
- Search for anything related to email

## Current Role In This Repository

This agent is a test and demonstration target for the desktop client. It is
useful for showing stateful, memory-backed conversations across sessions. Do not
commit `.env`, `.co/`, memory files, API keys, or OAuth tokens.
