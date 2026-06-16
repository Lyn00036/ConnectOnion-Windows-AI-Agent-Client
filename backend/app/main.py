"""FastAPI application entrypoint that configures middleware and registers backend routers."""

import logging

from fastapi import FastAPI, Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.core.errors import AppError
from app.routers import agents, chat, health
from app.schemas.error import ErrorResponse

logger = logging.getLogger(__name__)

app = FastAPI(title="ConnectOnion Desktop Backend")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(health.router)
app.include_router(agents.router)
app.include_router(chat.router)


@app.exception_handler(AppError)
async def app_error_handler(request: Request, exc: AppError) -> JSONResponse:
    error = ErrorResponse(
        error_code=exc.error_code,
        message=exc.message,
        details=exc.details,
    )
    return JSONResponse(
        status_code=exc.status_code,
        content=jsonable_encoder(error),
    )


@app.exception_handler(RequestValidationError)
async def validation_error_handler(
    request: Request,
    exc: RequestValidationError,
) -> JSONResponse:
    error = ErrorResponse(
        error_code="VALIDATION_ERROR",
        message="Request validation failed.",
        details={"errors": exc.errors()},
    )
    return JSONResponse(
        status_code=422,
        content=jsonable_encoder(error),
    )


@app.exception_handler(StarletteHTTPException)
async def http_error_handler(
    request: Request,
    exc: StarletteHTTPException,
) -> JSONResponse:
    error = ErrorResponse(
        error_code="HTTP_ERROR",
        message=str(exc.detail),
        details={},
    )
    return JSONResponse(
        status_code=exc.status_code,
        content=jsonable_encoder(error),
    )


@app.exception_handler(Exception)
async def unexpected_error_handler(request: Request, exc: Exception) -> JSONResponse:
    logger.error(
        "Unhandled backend error",
        exc_info=(type(exc), exc, exc.__traceback__),
    )
    error = ErrorResponse(
        error_code="INTERNAL_ERROR",
        message="Internal server error.",
        details={},
    )
    return JSONResponse(
        status_code=500,
        content=jsonable_encoder(error),
    )
