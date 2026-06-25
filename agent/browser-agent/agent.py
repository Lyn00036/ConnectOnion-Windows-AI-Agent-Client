#!/usr/bin/env python3
"""Browser automation agent and ConnectOnion host entrypoint."""

import os
import sys
from pathlib import Path

from dotenv import load_dotenv

AGENT_DIR = Path(__file__).resolve().parent
load_dotenv(AGENT_DIR / ".env")

# ConnectOnion's host banner contains Unicode characters that the Windows GBK
# console cannot encode. Configure this process only; setup commands stay plain
# `python` commands and do not require `-X utf8`.
for stream in (sys.stdout, sys.stderr):
    if hasattr(stream, "reconfigure"):
        stream.reconfigure(encoding="utf-8")

from connectonion import Agent, create_app, host
from connectonion.useful_plugins import image_result_formatter, ui_stream

from web_automation import WebAutomation

BROWSER_AGENT_TRUST = os.getenv("BROWSER_AGENT_TRUST", "open")
USE_CHROME_PROFILE = os.getenv("BROWSER_AGENT_USE_CHROME_PROFILE", "false").lower() in {
    "1",
    "true",
    "yes",
}

# Share one browser so follow-up messages can continue from the current page.
# This agent intentionally runs with one worker and supports one active browser
# conversation at a time.
web = WebAutomation(use_chrome_profile=USE_CHROME_PROFILE)


def create_browser_agent() -> Agent:
    """Create an agent instance backed by the shared browser session."""
    return Agent(
        name="browser_agent",
        model=os.getenv("BROWSER_AGENT_MODEL", "co/gemini-3-flash-preview"),
        system_prompt=str(AGENT_DIR / "prompts" / "agent.md"),
        tools=web,
        plugins=[image_result_formatter, ui_stream],
        max_iterations=50,
    )


# Native ConnectOnion ASGI app:
#   GET  /health
#   GET  /info
#   POST /input
#   WS   /ws
app = create_app(create_browser_agent, trust=BROWSER_AGENT_TRUST)


def main() -> None:
    """Run the full ConnectOnion HTTP/WebSocket host."""
    host_kwargs = {
        "trust": BROWSER_AGENT_TRUST,
        "relay_url": os.getenv("BROWSER_AGENT_RELAY_URL", ""),
        "co_dir": AGENT_DIR / ".co",
        "workers": 1,
    }
    if os.getenv("BROWSER_AGENT_PORT"):
        host_kwargs["port"] = int(os.environ["BROWSER_AGENT_PORT"])

    host(create_browser_agent, **host_kwargs)


if __name__ == "__main__":
    main()
