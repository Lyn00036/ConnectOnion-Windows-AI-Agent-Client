#!/usr/bin/env python3
"""Memory assistant agent and ConnectOnion host entrypoint.

Adapted from the ConnectOnion `examples/memory_agent.py` example into the same
hostable pattern used by this repository's other agents, so the desktop client
can connect to it over HTTP/WebSocket. The agent persists facts the user shares
into a local memory directory and recalls them on later turns.
"""

import os
import sys
from pathlib import Path

from dotenv import load_dotenv

AGENT_DIR = Path(__file__).resolve().parent
load_dotenv(AGENT_DIR / ".env")

# ConnectOnion's host banner contains Unicode characters that the Windows GBK
# console cannot encode. Reconfigure this process to UTF-8 so plain `python`
# commands work without the `-X utf8` flag.
for stream in (sys.stdout, sys.stderr):
    if hasattr(stream, "reconfigure"):
        stream.reconfigure(encoding="utf-8")

from connectonion import Agent, Memory, create_app, host

MEMORY_AGENT_TRUST = os.getenv("MEMORY_AGENT_TRUST", "open")
MEMORY_DIR = os.getenv("MEMORY_AGENT_DIR", str(AGENT_DIR / "memories"))


def create_memory_agent() -> Agent:
    """Create a fresh memory-backed assistant instance for hosted requests."""
    return Agent(
        name="memory_assistant",
        tools=[Memory(memory_dir=MEMORY_DIR)],
        system_prompt=str(AGENT_DIR / "prompts" / "agent.md"),
        model=os.getenv("MEMORY_AGENT_MODEL", "co/gemini-2.5-flash"),
        max_iterations=10,
        log=False,
        quiet=True,
    )


# Native ConnectOnion ASGI app:
#   GET  /health
#   GET  /info
#   POST /input
#   WS   /ws
app = create_app(create_memory_agent, trust=MEMORY_AGENT_TRUST)


def main() -> None:
    """Run the full ConnectOnion HTTP/WebSocket host."""
    host_kwargs = {
        "trust": MEMORY_AGENT_TRUST,
        # Empty value disables Relay; None defers to .co/host.yaml.
        "relay_url": os.getenv("MEMORY_AGENT_RELAY_URL", ""),
    }
    if os.getenv("MEMORY_AGENT_PORT"):
        host_kwargs["port"] = int(os.environ["MEMORY_AGENT_PORT"])

    host(create_memory_agent, **host_kwargs)


if __name__ == "__main__":
    main()
