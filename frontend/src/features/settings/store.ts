/** Zustand store for desktop appearance preferences (persisted locally). */

// TODO(settings-storage): Move preferences from localStorage to the Electron
// SQLite settings table through preload IPC once the settings repository exists.

import { create } from 'zustand'

export type ThemeMode = 'light' | 'dark'

const THEME_STORAGE_KEY = 'connectonion.theme'

function loadTheme(): ThemeMode {
  try {
    return localStorage.getItem(THEME_STORAGE_KEY) === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

type SettingsState = {
  theme: ThemeMode
  setTheme: (theme: ThemeMode) => void
  toggleTheme: () => void
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
}))
