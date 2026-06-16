"""Business logic for validating, checking, and eventually managing agent configurations."""

import httpx

from app.clients.connectonion_client import ConnectOnionClient
from app.core.errors import (
    AgentRequestFailedError,
    AgentUnavailableError,
    InvalidAgentUrlError,
)


class AgentService:
    async def check_agent_health(self, agent_url: str) -> dict:
        try:
            client = ConnectOnionClient(agent_url=agent_url, timeout=10.0)
            agent_health = await client.health_check()
        except InvalidAgentUrlError:
            raise
        except httpx.HTTPStatusError as exc:
            raise AgentRequestFailedError(
                details={
                    "agent_url": str(exc.request.url),
                    "status_code": exc.response.status_code,
                    "response": exc.response.text,
                }
            ) from exc
        except httpx.RequestError as exc:
            raise AgentUnavailableError(
                details={"agent_url": str(exc.request.url)}
            ) from exc

        return {"status": "ok", "agent": agent_health}
