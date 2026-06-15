#!/usr/bin/env python3
"""Email assistant agent shared by the CLI and HTTP server."""

import os
import sys
from functools import lru_cache
from pathlib import Path

from dotenv import load_dotenv

AGENT_DIR = Path(__file__).resolve().parent
REPO_ROOT = AGENT_DIR.parents[2]
CONNECTONION_SOURCE = REPO_ROOT.parent / "connectonion"

load_dotenv(AGENT_DIR / ".env")

if CONNECTONION_SOURCE.exists():
    sys.path.insert(0, str(CONNECTONION_SOURCE))

from connectonion import Agent, Gmail
from connectonion.useful_plugins import gmail_plugin, re_act


@lru_cache(maxsize=1)
def create_email_agent() -> Agent:
    """Create one long-lived email assistant instance."""
    return Agent(
        name="email_assistant",
        tools=[Gmail()],
        system_prompt=str(AGENT_DIR / "prompts" / "email_assistant.md"),
        model=os.getenv("EMAIL_AGENT_MODEL", "deepseek-v4-flash"),
        plugins=[re_act, gmail_plugin],
    )


def ask_email_agent(message: str) -> str:
    """Send a user message to the email assistant."""
    return str(create_email_agent().input(message))


def main() -> None:
    """Run the email assistant as an interactive CLI."""
    create_email_agent()

    print("Email Assistant")
    print("=" * 50)
    print("Type 'quit' to exit.")

    while True:
        try:
            user_input = input("\nYou: ")

            if user_input.lower() in {"quit", "exit", "bye", "q"}:
                print("\nGoodbye.")
                break

            if not user_input.strip():
                continue

            print(f"\nAgent: {ask_email_agent(user_input)}")

        except KeyboardInterrupt:
            print("\n\nEmail assistant stopped.")
            break
        except EOFError:
            print("\nEmail assistant stopped.")
            break


if __name__ == "__main__":
    main()
