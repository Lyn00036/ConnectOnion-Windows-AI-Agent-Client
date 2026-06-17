"""Create initial backend schema.

Revision ID: 20260617_0001
Revises:
Create Date: 2026-06-17 00:00:00.000000
"""

from collections.abc import Sequence

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = "20260617_0001"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "users",
        sa.Column("id", sa.Integer(), sa.Identity(), nullable=False),
        sa.Column("email", sa.Text(), nullable=False),
        sa.Column("password_hash", sa.Text(), nullable=False),
        sa.Column(
            "created_at",
            postgresql.TIMESTAMP(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("email"),
        if_not_exists=True,
    )
    op.create_table(
        "agent_configs",
        sa.Column("id", sa.Integer(), sa.Identity(), nullable=False),
        sa.Column("user_id", sa.Integer(), nullable=False),
        sa.Column("agent_name", sa.Text(), nullable=False),
        sa.Column("agent_address", sa.Text(), nullable=False),
        sa.Column("protocol", sa.Text(), nullable=False),
        sa.Column("model_name", sa.Text(), nullable=False),
        sa.Column("system_prompt", sa.Text(), nullable=True),
        sa.Column(
            "enabled",
            sa.Boolean(),
            server_default=sa.text("true"),
            nullable=False,
        ),
        sa.Column(
            "created_at",
            postgresql.TIMESTAMP(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.ForeignKeyConstraint(["user_id"], ["users.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
        if_not_exists=True,
    )
    op.create_index(
        "idx_agent_configs_user_id",
        "agent_configs",
        ["user_id"],
        unique=False,
        if_not_exists=True,
    )
    op.create_table(
        "conversations",
        sa.Column("id", sa.Integer(), sa.Identity(), nullable=False),
        sa.Column("user_id", sa.Integer(), nullable=False),
        sa.Column("agent_id", sa.Integer(), nullable=False),
        sa.Column("title", sa.Text(), nullable=False),
        sa.Column(
            "created_at",
            postgresql.TIMESTAMP(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.ForeignKeyConstraint(["agent_id"], ["agent_configs.id"], ondelete="RESTRICT"),
        sa.ForeignKeyConstraint(["user_id"], ["users.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
        if_not_exists=True,
    )
    op.create_index(
        "idx_conversations_agent_id",
        "conversations",
        ["agent_id"],
        unique=False,
        if_not_exists=True,
    )
    op.create_index(
        "idx_conversations_user_id",
        "conversations",
        ["user_id"],
        unique=False,
        if_not_exists=True,
    )
    op.create_table(
        "tool_configs",
        sa.Column("id", sa.Integer(), sa.Identity(), nullable=False),
        sa.Column("agent_id", sa.Integer(), nullable=False),
        sa.Column("tool_name", sa.Text(), nullable=False),
        sa.Column(
            "config",
            postgresql.JSONB(astext_type=sa.Text()),
            server_default=sa.text("'{}'::jsonb"),
            nullable=False,
        ),
        sa.ForeignKeyConstraint(["agent_id"], ["agent_configs.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
        if_not_exists=True,
    )
    op.create_index(
        "idx_tool_configs_agent_id",
        "tool_configs",
        ["agent_id"],
        unique=False,
        if_not_exists=True,
    )
    op.create_table(
        "messages",
        sa.Column("id", sa.Integer(), sa.Identity(), nullable=False),
        sa.Column("conversation_id", sa.Integer(), nullable=False),
        sa.Column("role", sa.Text(), nullable=False),
        sa.Column("content", sa.Text(), nullable=False),
        sa.Column(
            "created_at",
            postgresql.TIMESTAMP(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.ForeignKeyConstraint(
            ["conversation_id"],
            ["conversations.id"],
            ondelete="CASCADE",
        ),
        sa.PrimaryKeyConstraint("id"),
        if_not_exists=True,
    )
    op.create_index(
        "idx_messages_conversation_id",
        "messages",
        ["conversation_id"],
        unique=False,
        if_not_exists=True,
    )


def downgrade() -> None:
    op.drop_index(
        "idx_messages_conversation_id",
        table_name="messages",
        if_exists=True,
    )
    op.drop_table("messages", if_exists=True)
    op.drop_index(
        "idx_tool_configs_agent_id",
        table_name="tool_configs",
        if_exists=True,
    )
    op.drop_table("tool_configs", if_exists=True)
    op.drop_index(
        "idx_conversations_user_id",
        table_name="conversations",
        if_exists=True,
    )
    op.drop_index(
        "idx_conversations_agent_id",
        table_name="conversations",
        if_exists=True,
    )
    op.drop_table("conversations", if_exists=True)
    op.drop_index(
        "idx_agent_configs_user_id",
        table_name="agent_configs",
        if_exists=True,
    )
    op.drop_table("agent_configs", if_exists=True)
    op.drop_table("users", if_exists=True)
