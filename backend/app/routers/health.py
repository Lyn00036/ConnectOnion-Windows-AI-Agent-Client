"""Health-check API routes for verifying backend and database availability."""

from fastapi import APIRouter
from sqlalchemy import text
from sqlalchemy.exc import SQLAlchemyError

from app.core.errors import DatabaseUnavailableError
from app.db.session import AsyncSessionLocal

router = APIRouter()


@router.get("/health")
def health_check() -> dict:
    return {"status": "ok"}


@router.get("/health/db")
async def database_health_check() -> dict:
    try:
        async with AsyncSessionLocal() as session:
            result = await session.execute(
                text("SELECT current_database(), current_user")
            )
            database_name, database_user = result.one()
    except SQLAlchemyError as exc:
        raise DatabaseUnavailableError(
            details={"reason": exc.__class__.__name__}
        ) from exc

    return {
        "status": "ok",
        "database": database_name,
        "user": database_user,
    }
