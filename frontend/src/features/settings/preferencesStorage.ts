/**
 * Desktop preferences persistence (theme, sidebar, font size, shortcuts).
 *
 * Uses the SQLite `settings` table when the Electron bridge is available.
 */

import { withDesktopFallback } from '../../shared/storage/desktopFallback'

const THEME_STORAGE_KEY = 'connectonion.theme'
const SIDEBAR_STORAGE_KEY = 'connectonion.sidebarVisible'
const MESSAGE_FONT_SIZE_KEY = 'connectonion.messageFontSize'
const SHORTCUTS_STORAGE_KEY = 'connectonion.shortcuts'
const MIGRATION_FLAG_KEY = 'connectonion.sqliteMigrated.preferences'

function getDesktopSettings() {
  return window.desktopSettings ?? null
}

export type StoredThemeMode = 'light' | 'dark'
export type StoredMessageFontSize = 'sm' | 'md' | 'lg'

export type PreferencesSnapshot = {
  theme: StoredThemeMode
  sidebarVisible: boolean
  messageFontSize: StoredMessageFontSize
  shortcutOverrides: Partial<Record<string, string>>
}

function loadThemeFromLocalStorage(): StoredThemeMode {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY) === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

function loadSidebarFromLocalStorage(): boolean {
  try {
    return localStorage.getItem(SIDEBAR_STORAGE_KEY) !== 'false'
  } catch {
    return true
  }
}

function loadFontSizeFromLocalStorage(): StoredMessageFontSize {
  try {
    const stored = localStorage.getItem(MESSAGE_FONT_SIZE_KEY)
    return stored === 'sm' || stored === 'lg' ? stored : 'md'
  } catch {
    return 'md'
  }
}

function loadShortcutsFromLocalStorage(): Partial<Record<string, string>> {
  try {
    const raw = localStorage.getItem(SHORTCUTS_STORAGE_KEY)
    if (!raw) return {}
    return JSON.parse(raw) as Partial<Record<string, string>>
  } catch {
    return {}
  }
}

function loadPreferencesFromLocalStorage(): PreferencesSnapshot {
  return {
    theme: loadThemeFromLocalStorage(),
    sidebarVisible: loadSidebarFromLocalStorage(),
    messageFontSize: loadFontSizeFromLocalStorage(),
    shortcutOverrides: loadShortcutsFromLocalStorage(),
  }
}

async function migrateLegacyPreferencesOnce(
  desktop: NonNullable<ReturnType<typeof getDesktopSettings>>,
) {
  if (localStorage.getItem(MIGRATION_FLAG_KEY)) return

  const entries: Array<{ key: string; valueJson: string }> = []
  const theme = localStorage.getItem(THEME_STORAGE_KEY)
  if (theme) entries.push({ key: THEME_STORAGE_KEY, valueJson: JSON.stringify(theme) })
  const sidebar = localStorage.getItem(SIDEBAR_STORAGE_KEY)
  if (sidebar !== null) {
    entries.push({ key: SIDEBAR_STORAGE_KEY, valueJson: JSON.stringify(sidebar) })
  }
  const fontSize = localStorage.getItem(MESSAGE_FONT_SIZE_KEY)
  if (fontSize) {
    entries.push({ key: MESSAGE_FONT_SIZE_KEY, valueJson: JSON.stringify(fontSize) })
  }
  const shortcuts = localStorage.getItem(SHORTCUTS_STORAGE_KEY)
  if (shortcuts) entries.push({ key: SHORTCUTS_STORAGE_KEY, valueJson: shortcuts })

  if (entries.length > 0) {
    await desktop.bulkUpsert(entries)
  }

  localStorage.setItem(MIGRATION_FLAG_KEY, '1')
}

function parseJsonValue<T>(raw: string | null, fallback: T): T {
  if (!raw) return fallback
  try {
    return JSON.parse(raw) as T
  } catch {
    return fallback
  }
}

async function readFromDesktop(
  desktop: NonNullable<ReturnType<typeof getDesktopSettings>>,
): Promise<PreferencesSnapshot> {
  await migrateLegacyPreferencesOnce(desktop)

  const theme = parseJsonValue<StoredThemeMode>(
    await desktop.get(THEME_STORAGE_KEY),
    'light',
  )
  const sidebarRaw = parseJsonValue<string | null>(
    await desktop.get(SIDEBAR_STORAGE_KEY),
    null,
  )
  const messageFontSize = parseJsonValue<StoredMessageFontSize>(
    await desktop.get(MESSAGE_FONT_SIZE_KEY),
    'md',
  )
  const shortcutOverrides = parseJsonValue<Partial<Record<string, string>>>(
    await desktop.get(SHORTCUTS_STORAGE_KEY),
    {},
  )

  return {
    theme: theme === 'dark' ? 'dark' : 'light',
    sidebarVisible: sidebarRaw !== 'false',
    messageFontSize:
      messageFontSize === 'sm' || messageFontSize === 'lg' ? messageFontSize : 'md',
    shortcutOverrides,
  }
}

export async function readInitialPreferences(): Promise<PreferencesSnapshot> {
  const desktop = getDesktopSettings()

  return withDesktopFallback(
    Boolean(desktop),
    () => readFromDesktop(desktop!),
    () => loadPreferencesFromLocalStorage(),
  )
}

function saveSettingToLocalStorage(key: string, valueJson: string) {
  try {
    const parsed: unknown = JSON.parse(valueJson)
    localStorage.setItem(key, typeof parsed === 'string' ? parsed : valueJson)
  } catch {
    localStorage.setItem(key, valueJson)
  }
}

async function persistSetting(key: string, valueJson: string) {
  const desktop = getDesktopSettings()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.set(key, valueJson).then(() => undefined),
    () => saveSettingToLocalStorage(key, valueJson),
  )
}

export async function persistTheme(theme: StoredThemeMode) {
  await persistSetting(THEME_STORAGE_KEY, JSON.stringify(theme))
}

export async function persistSidebarVisible(visible: boolean) {
  await persistSetting(SIDEBAR_STORAGE_KEY, JSON.stringify(String(visible)))
}

export async function persistMessageFontSize(size: StoredMessageFontSize) {
  await persistSetting(MESSAGE_FONT_SIZE_KEY, JSON.stringify(size))
}

export async function persistShortcutOverrides(
  overrides: Partial<Record<string, string>>,
) {
  const desktop = getDesktopSettings()
  const payload = JSON.stringify(overrides)
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.set(SHORTCUTS_STORAGE_KEY, payload).then(() => undefined),
    () => {
      localStorage.setItem(SHORTCUTS_STORAGE_KEY, payload)
    },
  )
}
