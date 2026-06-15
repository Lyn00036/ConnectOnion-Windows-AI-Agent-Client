"""HTTP server for the email assistant agent."""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from agent import ask_email_agent

app = FastAPI(title="Email Agent Service")


class ChatRequest(BaseModel):
    message: str = Field(..., min_length=1)


class ChatResponse(BaseModel):
    response: str


@app.get("/health")
def health_check() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/chat", response_model=ChatResponse)
def chat(request: ChatRequest) -> ChatResponse:
    try:
        response = ask_email_agent(request.message)
    except Exception as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc

    return ChatResponse(response=response)
