import type { SqliteDatabase } from './connection.ts'

export type SessionRecord = {
  id: string
  agentId: string
  title: string
  createdAt: string
  updatedAt: string
}

export type SessionUpdate = {
  title: string
  updatedAt: string
}

type SessionRow = {
  id: string
  agent_id: string
  title: string
  created_at: string
  updated_at: string
}

function toRecord(row: SessionRow): SessionRecord {
  return {
    id: row.id,
    agentId: row.agent_id,
    title: row.title,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  }
}

export function listSessions(db: SqliteDatabase): SessionRecord[] {
  const rows = db
    .prepare(
      'SELECT id, agent_id, title, created_at, updated_at FROM sessions ORDER BY updated_at DESC',
    )
    .all() as SessionRow[]
  return rows.map(toRecord)
}

export function createSession(db: SqliteDatabase, session: SessionRecord): void {
  db.prepare(
    `INSERT INTO sessions (id, agent_id, title, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?)
     ON CONFLICT(id) DO UPDATE SET
       agent_id = excluded.agent_id,
       title = excluded.title,
       created_at = excluded.created_at,
       updated_at = excluded.updated_at`,
  ).run(
    session.id,
    session.agentId,
    session.title,
    session.createdAt,
    session.updatedAt,
  )
}

export function updateSession(
  db: SqliteDatabase,
  id: string,
  fields: SessionUpdate,
): void {
  db.prepare(
    `UPDATE sessions SET title = ?, updated_at = ? WHERE id = ?`,
  ).run(fields.title, fields.updatedAt, id)
}

export function removeSession(db: SqliteDatabase, id: string): void {
  db.prepare('DELETE FROM sessions WHERE id = ?').run(id)
}

export function removeSessionsForAgent(db: SqliteDatabase, agentId: string): void {
  db.prepare('DELETE FROM sessions WHERE agent_id = ?').run(agentId)
}

export function bulkUpsertSessions(
  db: SqliteDatabase,
  sessions: SessionRecord[],
): void {
  if (sessions.length === 0) return

  db.exec('BEGIN')
  try {
    for (const session of sessions) {
      createSession(db, session)
    }
    db.exec('COMMIT')
  } catch (error) {
    db.exec('ROLLBACK')
    throw error
  }
}
