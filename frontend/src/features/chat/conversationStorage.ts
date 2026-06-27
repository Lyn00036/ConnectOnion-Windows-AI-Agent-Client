/**
 * Conversation blob persistence (chat UI + SDK session state).
 *
 * Stored in the SQLite `settings` table under keys `co:agent:{address}:session:{id}`,
 * matching the legacy localStorage key format for one-time migration.
 */

const MIGRATION_FLAG_KEY = 'connectonion.sqliteMigrated.conversations'

function getDesktopSettings() {
  return window.desktopSettings ?? null
}

export function getConversationStorageKey(address: string, sessionId: string) {
  return `co:agent:${address}:session:${sessionId}`
}

function loadEnvelopeFromLocalStorage(storageKey: string): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

function saveEnvelopeToLocalStorage(storageKey: string, envelopeJson: string) {
  try {
    localStorage.setItem(storageKey, envelopeJson)
  } catch {
    // Keep the live conversation working if localStorage is unavailable or full.
  }
}

async function migrateLegacyConversationsOnce(
  desktop: NonNullable<ReturnType<typeof getDesktopSettings>>,
) {
  if (localStorage.getItem(MIGRATION_FLAG_KEY)) return

  const entries: Array<{ key: string; valueJson: string }> = []
  for (let index = 0; index < localStorage.length; index += 1) {
    const key = localStorage.key(index)
    if (!key?.startsWith('co:agent:')) continue
    const value = localStorage.getItem(key)
    if (value) entries.push({ key, valueJson: value })
  }

  if (entries.length > 0) {
    await desktop.bulkUpsert(entries)
  }

  localStorage.setItem(MIGRATION_FLAG_KEY, '1')
}

let migrationPromise: Promise<void> | null = null

function ensureConversationsMigrated() {
  const desktop = getDesktopSettings()
  if (!desktop) return Promise.resolve()
  if (localStorage.getItem(MIGRATION_FLAG_KEY)) return Promise.resolve()
  if (!migrationPromise) {
    migrationPromise = migrateLegacyConversationsOnce(desktop)
  }
  return migrationPromise
}

export async function readConversationEnvelope(
  address: string,
  sessionId: string,
): Promise<string | null> {
  const storageKey = getConversationStorageKey(address, sessionId)
  const desktop = getDesktopSettings()

  if (!desktop) {
    return loadEnvelopeFromLocalStorage(storageKey)
  }

  await ensureConversationsMigrated()
  return desktop.get(storageKey)
}

export async function writeConversationEnvelope(
  address: string,
  sessionId: string,
  envelopeJson: string,
) {
  const storageKey = getConversationStorageKey(address, sessionId)
  const desktop = getDesktopSettings()

  if (desktop) {
    await desktop.set(storageKey, envelopeJson)
    return
  }

  saveEnvelopeToLocalStorage(storageKey, envelopeJson)
}

export async function removeConversationEnvelope(address: string, sessionId: string) {
  const storageKey = getConversationStorageKey(address, sessionId)
  const desktop = getDesktopSettings()

  if (desktop) {
    await desktop.remove(storageKey)
    return
  }

  localStorage.removeItem(storageKey)
}
