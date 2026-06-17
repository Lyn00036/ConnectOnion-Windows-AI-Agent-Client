#!/usr/bin/env python3
"""Email assistant agent shared by the CLI and ConnectOnion host."""

import os
import sys
from pathlib import Path

from dotenv import load_dotenv

AGENT_DIR = Path(__file__).resolve().parent
LOCAL_CONNECTONION_SOURCE = (
    AGENT_DIR.parents[2] / "connectonion"
    if len(AGENT_DIR.parents) > 2
    else None
)

load_dotenv(AGENT_DIR / ".env")

if LOCAL_CONNECTONION_SOURCE and LOCAL_CONNECTONION_SOURCE.exists():
    sys.path.insert(0, str(LOCAL_CONNECTONION_SOURCE))

from connectonion import Agent, Gmail
from connectonion.useful_plugins import gmail_plugin, re_act


def create_email_agent() -> Agent:
    """Create a fresh email assistant instance for hosted requests."""
    return Agent(
        name="email_assistant",
        tools=[Gmail()],
        system_prompt=str(AGENT_DIR / "prompts" / "email_assistant.md"),
        model=os.getenv("EMAIL_AGENT_MODEL", "co/gemini-2.5-flash"),
        plugins=[re_act, gmail_plugin],
        log=False,
        quiet=True,
    )


def main() -> None:
    """Run the email assistant as an interactive CLI."""
    agent = create_email_agent()

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

            print(f"\nAgent: {agent.input(user_input)}")

        except KeyboardInterrupt:
            print("\n\nEmail assistant stopped.")
            break
        except EOFError:
            print("\nEmail assistant stopped.")
            break


if __name__ == "__main__":
    main()
