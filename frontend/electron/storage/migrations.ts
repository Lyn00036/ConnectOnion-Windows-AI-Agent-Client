import type { SqliteDatabase } from './connection.ts'

// TODO(sqlite-schema): Keep migrations append-only. Do not edit statements that
// have already shipped in a Windows build; add a new migration version instead.
// Planned additions:
// - attachments table for file drag-and-drop metadata
// - indexes for agent/session lookup workflows
// - app data migration marker for localStorage to SQLite migration
// - credential references only, never plaintext secrets

export type Migration = {
  version: number
  name: string
  statements: string[]
}

export const migrations: Migration[] = [
  {
    version: 1,
    name: 'initial_desktop_storage',
    statements: [
      `CREATE TABLE IF NOT EXISTS app_metadata (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL,
        updated_at TEXT NOT NULL
      )`,
      `CREATE TABLE IF NOT EXISTS agents (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        address TEXT NOT NULL,
        direct_url TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      )`,
      `CREATE TABLE IF NOT EXISTS sessions (
        id TEXT PRIMARY KEY,
        agent_id TEXT,
        title TEXT NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        FOREIGN KEY (agent_id) REFERENCES agents(id) ON DELETE SET NULL
      )`,
      `CREATE TABLE IF NOT EXISTS messages (
        id TEXT PRIMARY KEY,
        session_id TEXT NOT NULL,
        role TEXT NOT NULL CHECK (role IN ('user', 'agent', 'system')),
        content TEXT NOT NULL,
        metadata_json TEXT,
        created_at TEXT NOT NULL,
        FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
      )`,
      `CREATE TABLE IF NOT EXISTS settings (
        key TEXT PRIMARY KEY,
        value_json TEXT NOT NULL,
        updated_at TEXT NOT NULL
      )`,
      'CREATE INDEX IF NOT EXISTS idx_sessions_updated_at ON sessions(updated_at)',
      'CREATE INDEX IF NOT EXISTS idx_messages_session_id ON messages(session_id)',
      'CREATE INDEX IF NOT EXISTS idx_messages_created_at ON messages(created_at)',
    ],
  },
]

type SchemaRow = {
  version: number
}

export function applyMigrations(db: SqliteDatabase) {
  db.exec(`CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    applied_at TEXT NOT NULL
  )`)

  for (const migration of migrations) {
    const existing = db
      .prepare('SELECT version FROM schema_migrations WHERE version = ?')
      .get(migration.version) as SchemaRow | undefined

    if (existing) continue

    db.exec('BEGIN')
    try {
      for (const statement of migration.statements) {
        db.exec(statement)
      }
      db.prepare(
        'INSERT INTO schema_migrations (version, name, applied_at) VALUES (?, ?, ?)',
      ).run(migration.version, migration.name, new Date().toISOString())
      db.exec('COMMIT')
    } catch (error) {
      db.exec('ROLLBACK')
      throw error
    }
  }
}

export function getSchemaVersion(db: SqliteDatabase) {
  const row = db
    .prepare('SELECT MAX(version) AS version FROM schema_migrations')
    .get() as SchemaRow | undefined

  return row?.version ?? 0
}
