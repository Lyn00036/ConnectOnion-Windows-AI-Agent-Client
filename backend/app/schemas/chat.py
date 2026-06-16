"""Pydantic schemas for chat request and response payloads."""

from pydantic import BaseModel, Field


class ChatRequest(BaseModel):
    message: str = Field(..., min_length=1)
    agent_url: str = Field(..., min_length=1)


class ChatResponse(BaseModel):
    response: str
