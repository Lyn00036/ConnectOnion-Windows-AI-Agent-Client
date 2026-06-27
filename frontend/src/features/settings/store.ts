/** Zustand store for desktop appearance preferences (persisted locally). */

import { create } from 'zustand'

import {
  persistMessageFontSize,
  persistSidebarVisible,
  persistTheme,
  readInitialPreferences,
} from './preferencesStorage'
import { useShortcutsStore, type ShortcutId } from './shortcuts'

export type ThemeMode = 'light' | 'dark'
export type MessageFontSize = 'sm' | 'md' | 'lg'

/** Maps the size preference to the CSS value applied via --co-msg-size. */
export const MESSAGE_FONT_SIZES: Record<MessageFontSize, string> = {
  sm: '0.8125rem',
  md: '0.9375rem',
  lg: '1.0625rem',
}

type SettingsState = {
  theme: ThemeMode
  sidebarVisible: boolean
  messageFontSize: MessageFontSize
  hydrated: boolean
  hydrate: () => Promise<void>
  setTheme: (theme: ThemeMode) => void
  toggleTheme: () => void
  setSidebarVisible: (visible: boolean) => void
  toggleSidebar: () => void
  setMessageFontSize: (size: MessageFontSize) => void
}

let hydrateStarted = false

export const useSettingsStore = create<SettingsState>((set, get) => ({
  theme: 'light',
  sidebarVisible: true,
  messageFontSize: 'md',
  hydrated: false,
  hydrate: async () => {
    if (hydrateStarted) return
    hydrateStarted = true
    try {
      const prefs = await readInitialPreferences()
      set({
        theme: prefs.theme,
        sidebarVisible: prefs.sidebarVisible,
        messageFontSize: prefs.messageFontSize,
        hydrated: true,
      })
      useShortcutsStore.setState({
        overrides: prefs.shortcutOverrides as Partial<Record<ShortcutId, string>>,
      })
    } catch {
      set({ hydrated: true })
    }
  },
  setTheme: (theme) => {
    set({ theme })
    void persistTheme(theme)
  },
  toggleTheme: () => {
    get().setTheme(get().theme === 'dark' ? 'light' : 'dark')
  },
  setSidebarVisible: (visible) => {
    set({ sidebarVisible: visible })
    void persistSidebarVisible(visible)
  },
  toggleSidebar: () => {
    get().setSidebarVisible(!get().sidebarVisible)
  },
  setMessageFontSize: (size) => {
    set({ messageFontSize: size })
    void persistMessageFontSize(size)
  },
}))
