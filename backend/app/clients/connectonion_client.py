"""HTTP client adapter for communicating with remote ConnectOnion agent services."""

from urllib.parse import urlparse

import httpx

from app.core.errors import InvalidAgentUrlError


class ConnectOnionClient:
    def __init__(
        self,
        agent_url: str,
        timeout: float = 30.0,
    ) -> None:
        self.base_url = self._normalize_agent_url(agent_url)
        self.timeout = timeout

    async def health_check(self) -> dict:
        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.get(f"{self.base_url}/health")
            response.raise_for_status()
            return response.json()

    async def send_message(self, message: str) -> dict:
        async with httpx.AsyncClient(timeout=self.timeout) as client:
            response = await client.post(
                f"{self.base_url}/chat",
                json={"message": message},
            )
            response.raise_for_status()
            return response.json()

    @staticmethod
    def _normalize_agent_url(agent_url: str) -> str:
        url = agent_url.strip().rstrip("/")
        parsed = urlparse(url)

        if parsed.scheme not in {"http", "https"} or not parsed.netloc:
            raise InvalidAgentUrlError(details={"agent_url": url})

        return url
