import { describe, expect, it, vi } from 'vitest'

import type { DesktopAgentsApi } from './agents'
import type { DesktopSessionsApi } from './sessions'
import type { DesktopSettingsApi } from './settings'
import {
  areAllMigrationsComplete,
  clearLegacyLocalStorageOnce,
  isDesktopPersistenceAvailable,
} from './legacyLocalStorage'

function installDesktopPersistence() {
  window.desktopAgents = {} as DesktopAgentsApi
  window.desktopSessions = {} as DesktopSessionsApi
  window.desktopSettings = {} as DesktopSettingsApi
}

describe('legacy localStorage cleanup', () => {
  it('detects whether all Electron persistence bridges are available', () => {
    expect(isDesktopPersistenceAvailable()).toBe(false)

    installDesktopPersistence()

    expect(isDesktopPersistenceAvailable()).toBe(true)
  })

  it('keeps legacy data until every migration marker is present', () => {
    installDesktopPersistence()
    localStorage.setItem('connectonion.agents', '[]')
    localStorage.setItem('connectonion.sqliteMigrated.agents', '1')

    clearLegacyLocalStorageOnce()

    expect(localStorage.getItem('connectonion.agents')).toBe('[]')
    expect(areAllMigrationsComplete()).toBe(false)
  })

  it('removes migrated ConnectOnion keys after every migration completes', () => {
    installDesktopPersistence()
    localStorage.setItem('connectonion.agents', '[]')
    localStorage.setItem('connectonion.selectedAgentId', 'agent-1')
    localStorage.setItem('co:agent:agent-1:session:session-1', '{"messages":[]}')
    localStorage.setItem('unrelated.key', 'keep')

    for (const key of [
      'connectonion.sqliteMigrated.agents',
      'connectonion.sqliteMigrated.sessions',
      'connectonion.sqliteMigrated.conversations',
      'connectonion.sqliteMigrated.preferences',
    ]) {
      localStorage.setItem(key, '1')
    }

    clearLegacyLocalStorageOnce()

    expect(localStorage.getItem('connectonion.agents')).toBeNull()
    expect(localStorage.getItem('connectonion.selectedAgentId')).toBeNull()
    expect(localStorage.getItem('co:agent:agent-1:session:session-1')).toBeNull()
    expect(localStorage.getItem('unrelated.key')).toBe('keep')
    expect(localStorage.getItem('connectonion.sqliteLegacyDataCleared')).toBe('1')
  })

  it('is idempotent after cleanup has run once', () => {
    installDesktopPersistence()
    localStorage.setItem('connectonion.sqliteLegacyDataCleared', '1')
    localStorage.setItem('connectonion.agents', '[]')

    clearLegacyLocalStorageOnce()

    expect(localStorage.getItem('connectonion.agents')).toBe('[]')
    expect(vi.isMockFunction(clearLegacyLocalStorageOnce)).toBe(false)
  })
})
