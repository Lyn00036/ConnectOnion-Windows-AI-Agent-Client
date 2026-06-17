"""Shared SQLAlchemy declarative base for ORM models and migrations."""

from sqlalchemy.orm import DeclarativeBase


class Base(DeclarativeBase):
    pass
