"""Application error classes used by services and global exception handlers."""

from typing import Any


class AppError(Exception):
    status_code = 500
    error_code = "INTERNAL_ERROR"
    message = "Internal server error."

    def __init__(
        self,
        message: str | None = None,
        details: dict[str, Any] | None = None,
    ) -> None:
        self.message = message or self.message
        self.details = details or {}
        super().__init__(self.message)


class InvalidAgentUrlError(AppError):
    status_code = 400
    error_code = "INVALID_AGENT_URL"
    message = "Agent URL must be a valid http:// or https:// URL."


class AgentUnavailableError(AppError):
    status_code = 503
    error_code = "AGENT_UNAVAILABLE"
    message = "Agent is unavailable."


class AgentRequestFailedError(AppError):
    status_code = 502
    error_code = "AGENT_REQUEST_FAILED"
    message = "Agent request failed."


class DatabaseUnavailableError(AppError):
    status_code = 503
    error_code = "DATABASE_UNAVAILABLE"
    message = "Database is unavailable."
