"""Business logic for sending chat messages to agents and shaping chat responses."""

import httpx

from app.clients.connectonion_client import ConnectOnionClient
from app.core.errors import (
    AgentRequestFailedError,
    AgentUnavailableError,
    InvalidAgentUrlError,
)
from app.schemas.chat import ChatRequest, ChatResponse


class ChatService:
    async def send_to_agent(self, request: ChatRequest) -> ChatResponse:
        try:
            client = ConnectOnionClient(agent_url=request.agent_url, timeout=120.0)
            payload = await client.send_message(request.message)
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

        return ChatResponse(response=payload.get("response", ""))
