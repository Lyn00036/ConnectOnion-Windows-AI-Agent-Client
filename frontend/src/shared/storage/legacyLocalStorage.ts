/**
 * One-time removal of legacy localStorage payloads after SQLite migration.
 *
 * Only runs in Electron (when the desktop bridge exists) and only after every
 * migration marker is present. Keeps `connectonion.sqliteLegacyDataCleared`.
 */

const CLEANUP_FLAG_KEY = 'connectonion.sqliteLegacyDataCleared'

const MIGRATION_FLAG_KEYS = [
  'connectonion.sqliteMigrated.agents',
  'connectonion.sqliteMigrated.sessions',
  'connectonion.sqliteMigrated.conversations',
  'connectonion.sqliteMigrated.preferences',
] as const

function isLegacyLocalStorageKey(key: string) {
  if (key === CLEANUP_FLAG_KEY) return false
  if (key.startsWith('connectonion.')) return true
  if (key.startsWith('co:agent:')) return true
  return false
}

/** True when running inside Electron with the SQLite preload bridges. */
export function isDesktopPersistenceAvailable() {
  return Boolean(window.desktopAgents && window.desktopSessions && window.desktopSettings)
}

/** Returns true when all migration markers are set (data should be in SQLite). */
export function areAllMigrationsComplete() {
  return MIGRATION_FLAG_KEYS.every((key) => localStorage.getItem(key) === '1')
}

/**
 * Remove migrated legacy keys from localStorage. Safe to call multiple times;
 * guarded by CLEANUP_FLAG_KEY.
 */
export function clearLegacyLocalStorageOnce() {
  if (!isDesktopPersistenceAvailable()) return
  if (localStorage.getItem(CLEANUP_FLAG_KEY)) return
  if (!areAllMigrationsComplete()) return

  const keysToRemove: string[] = []
  for (let index = 0; index < localStorage.length; index += 1) {
    const key = localStorage.key(index)
    if (key && isLegacyLocalStorageKey(key)) {
      keysToRemove.push(key)
    }
  }

  for (const key of keysToRemove) {
    localStorage.removeItem(key)
  }

  localStorage.setItem(CLEANUP_FLAG_KEY, '1')
}
