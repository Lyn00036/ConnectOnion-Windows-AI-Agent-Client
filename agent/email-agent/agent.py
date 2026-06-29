#!/usr/bin/env python3
"""Email assistant agent and ConnectOnion host entrypoint."""

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

from connectonion import Agent, Gmail, create_app, host
from connectonion.useful_plugins import gmail_plugin, re_act, ui_stream

AGENT_TRUST = os.getenv("EMAIL_AGENT_TRUST", "open")


def create_email_agent() -> Agent:
    """Create a fresh email assistant instance for hosted requests."""
    return Agent(
        name="email_assistant",
        tools=[Gmail()],
        system_prompt=str(AGENT_DIR / "prompts" / "email_assistant.md"),
        model=os.getenv("EMAIL_AGENT_MODEL", "co/gemini-2.5-flash"),
        plugins=[re_act, gmail_plugin, ui_stream],
        log=False,
        quiet=True,
    )


# Native ConnectOnion ASGI app:
#   GET  /health
#   GET  /info
#   POST /input
#   WS   /ws
app = create_app(create_email_agent, trust=AGENT_TRUST)


def main() -> None:
    """Run the full ConnectOnion HTTP/WebSocket host."""
    host_kwargs = {
        "trust": AGENT_TRUST,
        # None defers to .co/host.yaml; an empty value disables Relay.
        "relay_url": "",
    }
    if os.getenv("EMAIL_AGENT_PORT"):
        host_kwargs["port"] = int(os.environ["EMAIL_AGENT_PORT"])

    host(create_email_agent, **host_kwargs)


if __name__ == "__main__":
    main()
