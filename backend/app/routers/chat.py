"""Chat API routes that accept user messages and forward them through the chat service."""

from fastapi import APIRouter

from app.schemas.chat import ChatRequest, ChatResponse
from app.services.chat_service import ChatService

router = APIRouter(prefix="/agents", tags=["chat"])


@router.post("/chat", response_model=ChatResponse)
async def chat_with_agent(request: ChatRequest) -> ChatResponse:
    service = ChatService()
    return await service.send_to_agent(request)
