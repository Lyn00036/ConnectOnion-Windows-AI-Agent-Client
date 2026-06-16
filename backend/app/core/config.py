"""Centralized runtime configuration loaded from environment variables and .env files."""

import os
from dataclasses import dataclass

from dotenv import load_dotenv

load_dotenv()


@dataclass(frozen=True)
class Settings:
    database_url: str | None = os.getenv("DATABASE_URL")
    database_echo: bool = os.getenv("DATABASE_ECHO", "true").lower() == "true"


settings = Settings()
