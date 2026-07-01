import { describe, expect, it } from 'vitest'

import {
  persistMessageFontSize,
  persistShortcutOverrides,
  persistSidebarVisible,
  persistTheme,
  readInitialPreferences,
} from './preferencesStorage'

describe('preferencesStorage', () => {
  it('reads normalized preferences from localStorage', async () => {
    localStorage.setItem('connectonion.theme', 'dark')
    localStorage.setItem('connectonion.sidebarVisible', 'false')
    localStorage.setItem('connectonion.messageFontSize', 'lg')
    localStorage.setItem('connectonion.shortcuts', '{"toggle-sidebar":"Ctrl+B"}')

    await expect(readInitialPreferences()).resolves.toEqual({
      theme: 'dark',
      sidebarVisible: false,
      messageFontSize: 'lg',
      shortcutOverrides: { 'toggle-sidebar': 'Ctrl+B' },
    })
  })

  it('uses defaults for unexpected or missing localStorage values', async () => {
    localStorage.setItem('connectonion.messageFontSize', 'unexpected')
    localStorage.setItem('connectonion.shortcuts', '{bad json')

    await expect(readInitialPreferences()).resolves.toEqual({
      theme: 'light',
      sidebarVisible: true,
      messageFontSize: 'md',
      shortcutOverrides: {},
    })
  })

  it('persists preference updates to localStorage', async () => {
    await persistTheme('dark')
    await persistSidebarVisible(false)
    await persistMessageFontSize('sm')
    await persistShortcutOverrides({ 'toggle-sidebar': 'Ctrl+B' })

    expect(localStorage.getItem('connectonion.theme')).toBe('dark')
    expect(localStorage.getItem('connectonion.sidebarVisible')).toBe('false')
    expect(localStorage.getItem('connectonion.messageFontSize')).toBe('sm')
    expect(localStorage.getItem('connectonion.shortcuts')).toBe(
      '{"toggle-sidebar":"Ctrl+B"}',
    )
  })
})
