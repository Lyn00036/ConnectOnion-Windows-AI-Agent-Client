import type { SqliteDatabase } from './connection.ts'

// Agent CRUD against the SQLite `agents` table, plus a tiny key/value helper on
// `app_metadata` used for storing the selected agent id. The renderer never sees
// SQL: it talks to this module through agentIpc -> preload (window.desktopAgents).

export type AgentRecord = {
  id: string
  name: string
  address: string
  directUrl: string | null
}

export type AgentUpdate = {
  name: string
  address: string
  directUrl: string | null
}

type AgentRow = {
  id: string
  name: string
  address: string
  direct_url: string | null
}

function toRecord(row: AgentRow): AgentRecord {
  return {
    id: row.id,
    name: row.name,
    address: row.address,
    directUrl: row.direct_url,
  }
}

export function listAgents(db: SqliteDatabase): AgentRecord[] {
  const rows = db
    .prepare(
      'SELECT id, name, address, direct_url FROM agents ORDER BY created_at ASC',
    )
    .all() as AgentRow[]
  return rows.map(toRecord)
}

export function createAgent(db: SqliteDatabase, agent: AgentRecord): void {
  const now = new Date().toISOString()
  db.prepare(
    `INSERT INTO agents (id, name, address, direct_url, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?)
     ON CONFLICT(id) DO UPDATE SET
       name = excluded.name,
       address = excluded.address,
       direct_url = excluded.direct_url,
       updated_at = excluded.updated_at`,
  ).run(agent.id, agent.name, agent.address, agent.directUrl, now, now)
}

export function updateAgent(
  db: SqliteDatabase,
  id: string,
  fields: AgentUpdate,
): void {
  const now = new Date().toISOString()
  db.prepare(
    `UPDATE agents
       SET name = ?, address = ?, direct_url = ?, updated_at = ?
     WHERE id = ?`,
  ).run(fields.name, fields.address, fields.directUrl, now, id)
}

export function removeAgent(db: SqliteDatabase, id: string): void {
  db.prepare('DELETE FROM agents WHERE id = ?').run(id)
}

export function bulkUpsertAgents(
  db: SqliteDatabase,
  agents: AgentRecord[],
): void {
  if (agents.length === 0) return

  db.exec('BEGIN')
  try {
    for (const agent of agents) {
      createAgent(db, agent)
    }
    db.exec('COMMIT')
  } catch (error) {
    db.exec('ROLLBACK')
    throw error
  }
}

type MetaRow = {
  value: string
}

export function getMeta(db: SqliteDatabase, key: string): string | null {
  const row = db
    .prepare('SELECT value FROM app_metadata WHERE key = ?')
    .get(key) as MetaRow | undefined
  return row?.value ?? null
}

export function setMeta(db: SqliteDatabase, key: string, value: string): void {
  const now = new Date().toISOString()
  db.prepare(
    `INSERT INTO app_metadata (key, value, updated_at)
     VALUES (?, ?, ?)
     ON CONFLICT(key) DO UPDATE SET
       value = excluded.value,
       updated_at = excluded.updated_at`,
  ).run(key, value, now)
}
