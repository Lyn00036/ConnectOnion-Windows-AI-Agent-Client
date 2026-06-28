import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { DesktopSettingsApi } from '../../shared/storage/settings'

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

async function loadConversationStorage() {
  vi.resetModules()
  return import('./conversationStorage')
}

describe('conversationStorage', () => {
  beforeEach(() => {
    vi.resetModules()
  })

  it('builds stable conversation storage keys', async () => {
    const { getConversationStorageKey } = await loadConversationStorage()

    expect(getConversationStorageKey('agent-1', 'session-1')).toBe(
      'co:agent:agent-1:session:session-1',
    )
  })

  it('migrates legacy conversation envelopes before reading from desktop settings', async () => {
    const key = 'co:agent:agent-1:session:session-1'
    localStorage.setItem(key, '{"messages":[]}')
    localStorage.setItem('connectonion.agents', '[]')

    const desktop = createDesktopSettings({ [key]: '{"messages":["from-sqlite"]}' })
    window.desktopSettings = desktop
    const { readConversationEnvelope } = await loadConversationStorage()

    await expect(readConversationEnvelope('agent-1', 'session-1')).resolves.toBe(
      '{"messages":["from-sqlite"]}',
    )
    expect(desktop.bulkUpsert).toHaveBeenCalledWith([
      { key, valueJson: '{"messages":[]}' },
    ])
    expect(localStorage.getItem('connectonion.sqliteMigrated.conversations')).toBe(
      '1',
    )
  })

  it('writes and removes envelopes through desktop settings when available', async () => {
    const desktop = createDesktopSettings()
    window.desktopSettings = desktop
    const {
      getConversationStorageKey,
      removeConversationEnvelope,
      writeConversationEnvelope,
    } = await loadConversationStorage()
    const key = getConversationStorageKey('agent-1', 'session-1')

    await writeConversationEnvelope('agent-1', 'session-1', '{"ui":[]}')
    await removeConversationEnvelope('agent-1', 'session-1')

    expect(desktop.set).toHaveBeenCalledWith(key, '{"ui":[]}')
    expect(desktop.remove).toHaveBeenCalledWith(key)
  })

  it('falls back to localStorage without the Electron settings bridge', async () => {
    const {
      readConversationEnvelope,
      removeConversationEnvelope,
      writeConversationEnvelope,
    } = await loadConversationStorage()

    await writeConversationEnvelope('agent-1', 'session-1', '{"ui":["hello"]}')
    await expect(readConversationEnvelope('agent-1', 'session-1')).resolves.toBe(
      '{"ui":["hello"]}',
    )
    await removeConversationEnvelope('agent-1', 'session-1')

    expect(
      localStorage.getItem('co:agent:agent-1:session:session-1'),
    ).toBeNull()
  })
})
