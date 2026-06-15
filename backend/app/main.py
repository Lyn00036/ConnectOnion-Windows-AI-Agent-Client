import os
from urllib.parse import urlparse

import httpx
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

app = FastAPI(title="ConnectOnion Desktop Backend")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/health")
def health_check():
    return {"status": "ok"}


class ChatRequest(BaseModel):
    message: str = Field(..., min_length=1)
    agent_url: str | None = None


class ChatResponse(BaseModel):
    response: str


def get_agent_url(agent_url: str | None = None) -> str:
    url = (agent_url or os.getenv("DEFAULT_AGENT_URL", "http://127.0.0.1:8010")).strip().rstrip("/")
    parsed = urlparse(url)

    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise HTTPException(
            status_code=400,
            detail="Agent URL must be a valid http:// or https:// URL",
        )

    return url


@app.get("/agents/health")
async def agent_health(agent_url: str | None = None) -> dict:
    agent_url = get_agent_url(agent_url)

    try:
        async with httpx.AsyncClient(timeout=10.0) as client:
            response = await client.get(f"{agent_url}/health")
            response.raise_for_status()
    except httpx.HTTPStatusError as exc:
        raise HTTPException(status_code=502, detail=exc.response.text) from exc
    except httpx.RequestError as exc:
        raise HTTPException(
            status_code=503,
            detail=f"Agent is unavailable at {agent_url}",
        ) from exc

    return {"status": "ok", "agent": response.json()}


@app.post("/agents/chat", response_model=ChatResponse)
async def chat_with_agent(request: ChatRequest) -> ChatResponse:
    agent_url = get_agent_url(request.agent_url)

    try:
        async with httpx.AsyncClient(timeout=120.0) as client:
            response = await client.post(
                f"{agent_url}/chat",
                json={"message": request.message},
            )
            response.raise_for_status()
    except httpx.HTTPStatusError as exc:
        detail = exc.response.text
        raise HTTPException(status_code=502, detail=detail) from exc
    except httpx.RequestError as exc:
        raise HTTPException(
            status_code=503,
            detail=f"Agent is unavailable at {agent_url}",
        ) from exc

    payload = response.json()
    return ChatResponse(response=payload.get("response", ""))
