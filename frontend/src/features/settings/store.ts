/** Zustand store for desktop appearance preferences (persisted locally). */

// TODO(settings-storage): Move preferences from localStorage to the Electron
// SQLite settings table through preload IPC once the settings repository exists.

import { create } from 'zustand'

export type ThemeMode = 'light' | 'dark'
export type MessageFontSize = 'sm' | 'md' | 'lg'

/** Maps the size preference to the CSS value applied via --co-msg-size. */
export const MESSAGE_FONT_SIZES: Record<MessageFontSize, string> = {
  sm: '0.8125rem',
  md: '0.9375rem',
  lg: '1.0625rem',
}

const THEME_STORAGE_KEY = 'connectonion.theme'
const SIDEBAR_STORAGE_KEY = 'connectonion.sidebarVisible'
const MESSAGE_FONT_SIZE_KEY = 'connectonion.messageFontSize'

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

function loadMessageFontSize(): MessageFontSize {
  try {
    const stored = localStorage.getItem(MESSAGE_FONT_SIZE_KEY)
    return stored === 'sm' || stored === 'lg' ? stored : 'md'
  } catch {
    return 'md'
  }
}

type SettingsState = {
  theme: ThemeMode
  setTheme: (theme: ThemeMode) => void
  toggleTheme: () => void
  sidebarVisible: boolean
  setSidebarVisible: (visible: boolean) => void
  toggleSidebar: () => void
  messageFontSize: MessageFontSize
  setMessageFontSize: (size: MessageFontSize) => void
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
  messageFontSize: loadMessageFontSize(),
  setMessageFontSize: (size) => {
    try {
      localStorage.setItem(MESSAGE_FONT_SIZE_KEY, size)
    } catch {
      // Keep the in-memory preference working if localStorage is unavailable.
    }
    set({ messageFontSize: size })
  },
}))
