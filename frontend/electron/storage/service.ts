import { mkdirSync } from 'node:fs'
import { join } from 'node:path'

import type { DesktopStorageStatus } from '../../src/shared/storage/types.ts'
import { openDatabase, type SqliteDatabase } from './connection.ts'
import { applyMigrations, getSchemaVersion } from './migrations.ts'

export class DesktopStorageService {
  private db: SqliteDatabase | null = null
  private databasePath: string | null = null
  private readonly getUserDataPath: () => string

  constructor(getUserDataPath: () => string) {
    this.getUserDataPath = getUserDataPath
  }

  initialize() {
    if (this.db) return

    const storageDir = join(this.getUserDataPath(), 'storage')
    mkdirSync(storageDir, { recursive: true })

    this.databasePath = join(storageDir, 'connectonion-desktop.sqlite3')
    this.db = openDatabase(this.databasePath)
    applyMigrations(this.db)
  }

  getDatabase(): SqliteDatabase {
    this.initialize()

    if (!this.db) {
      throw new Error('Desktop storage is not initialized.')
    }

    return this.db
  }

  getStatus(): DesktopStorageStatus {
    this.initialize()

    if (!this.db || !this.databasePath) {
      throw new Error('Desktop storage is not initialized.')
    }

    return {
      available: true,
      driver: 'sqlite',
      databasePath: this.databasePath,
      schemaVersion: getSchemaVersion(this.db),
    }
  }

  close() {
    this.db?.close()
    this.db = null
    this.databasePath = null
  }
}

export function createDesktopStorage(getUserDataPath: () => string) {
  return new DesktopStorageService(getUserDataPath)
}
