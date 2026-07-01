/**
 * Desktop preferences persistence (theme, sidebar, font size, shortcuts).
 *
 * Uses localStorage as the single durable store.
 */

const THEME_STORAGE_KEY = 'connectonion.theme'
const SIDEBAR_STORAGE_KEY = 'connectonion.sidebarVisible'
const MESSAGE_FONT_SIZE_KEY = 'connectonion.messageFontSize'
const SHORTCUTS_STORAGE_KEY = 'connectonion.shortcuts'

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

export async function readInitialPreferences(): Promise<PreferencesSnapshot> {
  return loadPreferencesFromLocalStorage()
}

function saveSettingToLocalStorage(key: string, value: string) {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Keep in-memory state working if storage is unavailable.
  }
}

export async function persistTheme(theme: StoredThemeMode) {
  saveSettingToLocalStorage(THEME_STORAGE_KEY, theme)
}

export async function persistSidebarVisible(visible: boolean) {
  saveSettingToLocalStorage(SIDEBAR_STORAGE_KEY, String(visible))
}

export async function persistMessageFontSize(size: StoredMessageFontSize) {
  saveSettingToLocalStorage(MESSAGE_FONT_SIZE_KEY, size)
}

export async function persistShortcutOverrides(
  overrides: Partial<Record<string, string>>,
) {
  saveSettingToLocalStorage(SHORTCUTS_STORAGE_KEY, JSON.stringify(overrides))
}
