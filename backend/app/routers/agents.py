"""Agent API routes for agent status checks and future agent configuration operations."""

from fastapi import APIRouter

from app.services.agent_service import AgentService

router = APIRouter(prefix="/agents", tags=["agents"])


@router.get("/health")
async def agent_health(agent_url: str) -> dict:
    service = AgentService()
    return await service.check_agent_health(agent_url)
