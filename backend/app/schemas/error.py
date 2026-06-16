"""Pydantic schemas for standardized API error response payloads."""

from typing import Any

from pydantic import BaseModel, Field


class ErrorResponse(BaseModel):
    error_code: str
    message: str
    details: dict[str, Any] = Field(default_factory=dict)
