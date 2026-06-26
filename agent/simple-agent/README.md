# Simple Agent Example

A standalone ConnectOnion general-assistant agent used to test the desktop
client with a real local or deployed agent. It is adapted from the ConnectOnion
`examples/simple-agent` example into the same hostable pattern as the other
agents in this repository.

The desktop app connects to this agent through the ConnectOnion TypeScript SDK.
The agent is developed and deployed separately from the desktop client.

## Structure

```text
agent/
  requirements.txt       Shared Python dependencies for agents
  simple-agent/
    agent.py             Creates and hosts the ConnectOnion simple agent
    prompts/             Agent prompt files
    .env.example         Example local environment variables
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
Copy-Item .\simple-agent\.env.example .\simple-agent\.env
```

## ConnectOnion Auth

The default model is a managed `co/` model, so authenticate once to obtain
`OPENONION_API_KEY`. Run this from `agent/simple-agent` so credentials are
written to this agent's `.env`:

```powershell
cd .\agent\simple-agent
..\venv\Scripts\Activate.ps1
co auth
```

To use your own provider key instead, set `SIMPLE_AGENT_MODEL` (for example
`gpt-4o-mini`) and the matching provider key in `.env`.

## Run Locally

Choose one startup method:

```powershell
cd .\agent\simple-agent
..\venv\Scripts\Activate.ps1

# Full ConnectOnion host (reads .env; Relay disabled by default)
python agent.py

# Or run the ASGI app directly for local Direct URL testing
python -m uvicorn agent:app --host 127.0.0.1 --port 8020
```

Use the matching Direct URL in the desktop client:

```text
python agent.py   -> http://127.0.0.1:8000 (set SIMPLE_AGENT_PORT to change)
Uvicorn           -> http://127.0.0.1:8020
```

When running several local agents at once, give each one a different port.

## Example Prompts

- Say hello to Alice
- What is ConnectOnion in one sentence?
- Explain what a WebSocket is

## Current Role In This Repository

This agent is a test and demonstration target for the desktop client. It is the
lowest-setup connectable agent in the repository and is useful for verifying
multi-agent connection. Do not commit `.env`, `.co/`, API keys, or OAuth tokens.
