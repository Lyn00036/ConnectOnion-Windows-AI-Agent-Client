import type { SqliteDatabase } from './connection.ts'

type SettingRow = {
  key: string
  value_json: string
}

export function getSetting(db: SqliteDatabase, key: string): string | null {
  const row = db
    .prepare('SELECT value_json FROM settings WHERE key = ?')
    .get(key) as SettingRow | undefined
  return row?.value_json ?? null
}

export function setSetting(db: SqliteDatabase, key: string, valueJson: string): void {
  const now = new Date().toISOString()
  db.prepare(
    `INSERT INTO settings (key, value_json, updated_at)
     VALUES (?, ?, ?)
     ON CONFLICT(key) DO UPDATE SET
       value_json = excluded.value_json,
       updated_at = excluded.updated_at`,
  ).run(key, valueJson, now)
}

export function removeSetting(db: SqliteDatabase, key: string): void {
  db.prepare('DELETE FROM settings WHERE key = ?').run(key)
}

export function bulkUpsertSettings(
  db: SqliteDatabase,
  entries: Array<{ key: string; valueJson: string }>,
): void {
  if (entries.length === 0) return

  db.exec('BEGIN')
  try {
    for (const entry of entries) {
      setSetting(db, entry.key, entry.valueJson)
    }
    db.exec('COMMIT')
  } catch (error) {
    db.exec('ROLLBACK')
    throw error
  }
}
