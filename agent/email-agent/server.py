"""ConnectOnion host app for the email assistant agent."""

import os

from agent import create_email_agent
from connectonion import create_app, host

AGENT_TRUST = os.getenv("EMAIL_AGENT_TRUST", "open")

# Native ConnectOnion ASGI app:
#   GET  /health
#   GET  /info
#   POST /input
#   WS   /ws
app = create_app(create_email_agent, trust=AGENT_TRUST)


if __name__ == "__main__":
    host_kwargs = { "trust": AGENT_TRUST,
                    "relay_url": "",
    }
    if os.getenv("EMAIL_AGENT_PORT"):
        host_kwargs["port"] = int(os.environ["EMAIL_AGENT_PORT"])
    if os.getenv("EMAIL_AGENT_RELAY_URL"):
        host_kwargs["relay_url"] = os.environ["EMAIL_AGENT_RELAY_URL"]

    host(create_email_agent, **host_kwargs)
