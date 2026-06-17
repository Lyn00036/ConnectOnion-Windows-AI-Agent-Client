import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const BetterSqlite3 = require('better-sqlite3') as new (
  path: string,
) => SqliteDatabase

export type SqliteStatement = {
  get: (...params: unknown[]) => unknown
  run: (...params: unknown[]) => unknown
  all: (...params: unknown[]) => unknown[]
}

export type SqliteDatabase = {
  exec: (source: string) => void
  close: () => void
  pragma: (source: string) => unknown
  prepare: (source: string) => SqliteStatement
}

export function openDatabase(databasePath: string) {
  const db = new BetterSqlite3(databasePath)

  db.pragma('journal_mode = WAL')
  db.pragma('foreign_keys = ON')
  db.pragma('busy_timeout = 5000')

  return db
}
