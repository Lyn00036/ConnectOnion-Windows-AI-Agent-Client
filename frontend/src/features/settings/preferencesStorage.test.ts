import { describe, expect, it, vi } from 'vitest'

import type { DesktopSettingsApi } from '../../shared/storage/settings'
import {
  persistMessageFontSize,
  persistShortcutOverrides,
  persistSidebarVisible,
  persistTheme,
  readInitialPreferences,
} from './preferencesStorage'

function createDesktopSettings(
  storedValues: Record<string, string | null> = {},
): DesktopSettingsApi {
  return {
    get: vi.fn((key: string) => Promise.resolve(storedValues[key] ?? null)),
    set: vi.fn().mockResolvedValue(undefined),
    remove: vi.fn().mockResolvedValue(undefined),
    bulkUpsert: vi.fn().mockResolvedValue(undefined),
  }
}

describe('preferencesStorage', () => {
  it('migrates legacy preferences and reads normalized desktop values', async () => {
    localStorage.setItem('connectonion.theme', 'dark')
    localStorage.setItem('connectonion.sidebarVisible', 'false')
    localStorage.setItem('connectonion.messageFontSize', 'lg')
    localStorage.setItem('connectonion.shortcuts', '{"toggle-sidebar":"Ctrl+B"}')

    const desktop = createDesktopSettings({
      'connectonion.theme': '"dark"',
      'connectonion.sidebarVisible': '"false"',
      'connectonion.messageFontSize': '"lg"',
      'connectonion.shortcuts': '{"toggle-sidebar":"Ctrl+B"}',
    })
    window.desktopSettings = desktop

    await expect(readInitialPreferences()).resolves.toEqual({
      theme: 'dark',
      sidebarVisible: false,
      messageFontSize: 'lg',
      shortcutOverrides: { 'toggle-sidebar': 'Ctrl+B' },
    })
    expect(desktop.bulkUpsert).toHaveBeenCalledWith([
      { key: 'connectonion.theme', valueJson: '"dark"' },
      { key: 'connectonion.sidebarVisible', valueJson: '"false"' },
      { key: 'connectonion.messageFontSize', valueJson: '"lg"' },
      {
        key: 'connectonion.shortcuts',
        valueJson: '{"toggle-sidebar":"Ctrl+B"}',
      },
    ])
    expect(localStorage.getItem('connectonion.sqliteMigrated.preferences')).toBe(
      '1',
    )
  })

  it('falls back to localStorage when desktop settings are unavailable', async () => {
    localStorage.setItem('connectonion.theme', 'dark')
    localStorage.setItem('connectonion.sidebarVisible', 'false')
    localStorage.setItem('connectonion.messageFontSize', 'unexpected')
    localStorage.setItem('connectonion.shortcuts', '{"send-message":"Ctrl+Enter"}')

    await expect(readInitialPreferences()).resolves.toEqual({
      theme: 'dark',
      sidebarVisible: false,
      messageFontSize: 'md',
      shortcutOverrides: { 'send-message': 'Ctrl+Enter' },
    })
  })

  it('persists preference updates through desktop settings', async () => {
    const desktop = createDesktopSettings()
    window.desktopSettings = desktop

    await persistTheme('dark')
    await persistSidebarVisible(false)
    await persistMessageFontSize('sm')
    await persistShortcutOverrides({ 'toggle-sidebar': 'Ctrl+B' })

    expect(desktop.set).toHaveBeenCalledWith('connectonion.theme', '"dark"')
    expect(desktop.set).toHaveBeenCalledWith(
      'connectonion.sidebarVisible',
      '"false"',
    )
    expect(desktop.set).toHaveBeenCalledWith('connectonion.messageFontSize', '"sm"')
    expect(desktop.set).toHaveBeenCalledWith(
      'connectonion.shortcuts',
      '{"toggle-sidebar":"Ctrl+B"}',
    )
  })

  it('falls back to localStorage when a desktop preference write fails', async () => {
    const desktop = createDesktopSettings()
    vi.mocked(desktop.set).mockRejectedValue(new Error('sqlite locked'))
    window.desktopSettings = desktop

    await persistTheme('dark')

    expect(localStorage.getItem('connectonion.theme')).toBe('dark')
  })
})
