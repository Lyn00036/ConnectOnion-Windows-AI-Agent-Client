/** Zustand store for desktop appearance preferences (persisted locally). */

// TODO(settings-storage): Move preferences from localStorage to the Electron
// SQLite settings table through preload IPC once the settings repository exists.

import { create } from 'zustand'

export type ThemeMode = 'light' | 'dark'

const THEME_STORAGE_KEY = 'connectonion.theme'
const SIDEBAR_STORAGE_KEY = 'connectonion.sidebarVisible'

function loadTheme(): ThemeMode {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY) === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

function loadSidebarVisible(): boolean {
  try {
    // Default to visible; only an explicit "false" hides it.
    return localStorage.getItem(SIDEBAR_STORAGE_KEY) !== 'false'
  } catch {
    return true
  }
}

type SettingsState = {
  theme: ThemeMode
  setTheme: (theme: ThemeMode) => void
  toggleTheme: () => void
  sidebarVisible: boolean
  setSidebarVisible: (visible: boolean) => void
  toggleSidebar: () => void
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  theme: loadTheme(),
  setTheme: (theme) => {
    try {
      localStorage.setItem(THEME_STORAGE_KEY, theme)
    } catch {
      // Keep the in-memory theme working if localStorage is unavailable.
    }
    set({ theme })
  },
  toggleTheme: () => {
    get().setTheme(get().theme === 'dark' ? 'light' : 'dark')
  },
  sidebarVisible: loadSidebarVisible(),
  setSidebarVisible: (visible) => {
    try {
      localStorage.setItem(SIDEBAR_STORAGE_KEY, String(visible))
    } catch {
      // Keep the in-memory state working if localStorage is unavailable.
    }
    set({ sidebarVisible: visible })
  },
  toggleSidebar: () => {
    get().setSidebarVisible(!get().sidebarVisible)
  },
}))
