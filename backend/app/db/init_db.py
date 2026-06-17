"""Database schema initializer that applies Alembic migrations."""

from pathlib import Path

from alembic import command
from alembic.config import Config


BACKEND_DIR = Path(__file__).resolve().parents[2]
ALEMBIC_INI = BACKEND_DIR / "alembic.ini"


def init_db() -> None:
    alembic_cfg = Config(str(ALEMBIC_INI))
    command.upgrade(alembic_cfg, "head")
    print("Database schema migrated to head.")


if __name__ == "__main__":
    init_db()
