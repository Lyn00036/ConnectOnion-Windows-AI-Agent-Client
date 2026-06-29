"""Windows UTF-8 bootstrap for hosted ConnectOnion agents in this repo.

ConnectOnion's SessionStorage opens `.co/session_results.jsonl` without an
encoding, which defaults to GBK on Chinese Windows and crashes on UTF-8 chat
content. Patch read/write to UTF-8 before the ASGI app handles connections.
"""

from __future__ import annotations

import json
import sys
import time

_SESSION_ENCODING = "utf-8"
_applied = False


def bootstrap() -> None:
    global _applied
    if _applied:
        return
    _applied = True

    _configure_stdio()
    _patch_session_storage()


def _configure_stdio() -> None:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding=_SESSION_ENCODING)


def _patch_session_storage() -> None:
    from connectonion.network.host.session import storage as mod

    def save(self, session) -> None:
        with open(self.path, "a", encoding=_SESSION_ENCODING) as f:
            f.write(session.model_dump_json() + "\n")

    def get(self, session_id: str):
        if not self.path.exists():
            return None
        now = time.time()
        with open(self.path, encoding=_SESSION_ENCODING, errors="replace") as f:
            lines = f.readlines()
        for line in reversed(lines):
            if not line.strip():
                continue
            try:
                data = json.loads(line)
            except json.JSONDecodeError:
                continue
            if data["session_id"] == session_id:
                session = mod.Session(**data)
                if (
                    session.status == "running"
                    or not session.expires
                    or session.expires > now
                ):
                    return session
                return None
        return None

    def list_sessions(self):
        if not self.path.exists():
            return []
        sessions = {}
        now = time.time()
        with open(self.path, encoding=_SESSION_ENCODING, errors="replace") as f:
            for line in f:
                if not line.strip():
                    continue
                try:
                    data = json.loads(line)
                except json.JSONDecodeError:
                    continue
                sessions[data["session_id"]] = mod.Session(**data)
        valid = [
            s
            for s in sessions.values()
            if s.status == "running" or not s.expires or s.expires > now
        ]
        return sorted(valid, key=lambda s: s.created or 0, reverse=True)

    mod.SessionStorage.save = save
    mod.SessionStorage.get = get
    mod.SessionStorage.list = list_sessions
