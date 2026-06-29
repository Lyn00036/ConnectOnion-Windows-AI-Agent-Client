#!/usr/bin/env python3
"""Simple assistant agent and ConnectOnion host entrypoint.

Adapted from the ConnectOnion `examples/simple-agent` example into the same
hostable pattern used by this repository's other agents, so the desktop client
can connect to it over HTTP/WebSocket.
"""

import os
import sys
from pathlib import Path

AGENT_DIR = Path(__file__).resolve().parent
_AGENT_ROOT = AGENT_DIR.parent
if str(_AGENT_ROOT) not in sys.path:
    sys.path.insert(0, str(_AGENT_ROOT))

import win_utf8

win_utf8.bootstrap()

from dotenv import load_dotenv

load_dotenv(AGENT_DIR / ".env")

from connectonion import Agent, create_app, host

SIMPLE_AGENT_TRUST = os.getenv("SIMPLE_AGENT_TRUST", "open")


def hello_world(name: str = "World") -> str:
    """Return a friendly greeting for the given name."""
    return f"Hello, {name}! Welcome to ConnectOnion."


def create_simple_agent() -> Agent:
    """Create a fresh general-assistant instance for hosted requests."""
    return Agent(
        name="simple_assistant",
        tools=[hello_world],
        system_prompt=str(AGENT_DIR / "prompts" / "agent.md"),
        model=os.getenv("SIMPLE_AGENT_MODEL", "co/gemini-2.5-flash"),
        log=False,
        quiet=True,
    )


# Native ConnectOnion ASGI app:
#   GET  /health
#   GET  /info
#   POST /input
#   WS   /ws
app = create_app(create_simple_agent, trust=SIMPLE_AGENT_TRUST)


def main() -> None:
    """Run the full ConnectOnion HTTP/WebSocket host."""
    host_kwargs = {
        "trust": SIMPLE_AGENT_TRUST,
        # Empty value disables Relay; None defers to .co/host.yaml.
        "relay_url": os.getenv("SIMPLE_AGENT_RELAY_URL", ""),
    }
    if os.getenv("SIMPLE_AGENT_PORT"):
        host_kwargs["port"] = int(os.environ["SIMPLE_AGENT_PORT"])

    host(create_simple_agent, **host_kwargs)


if __name__ == "__main__":
    main()
