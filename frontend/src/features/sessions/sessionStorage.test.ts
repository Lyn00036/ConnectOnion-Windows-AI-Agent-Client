import { describe, expect, it, vi } from 'vitest'

import type { DesktopSessionsApi } from '../../shared/storage/sessions'
import type { SessionSummary } from './types'
import {
  persistSessionListChange,
  persistSessionTouch,
  readInitialSessions,
} from './sessionStorage'

function createDesktopSessions(
  records: Awaited<ReturnType<DesktopSessionsApi['list']>> = [],
  selectedId: string | null = null,
): DesktopSessionsApi {
  return {
    list: vi.fn().mockResolvedValue(records),
    create: vi.fn().mockResolvedValue(undefined),
    update: vi.fn().mockResolvedValue(undefined),
    remove: vi.fn().mockResolvedValue(undefined),
    removeForAgent: vi.fn().mockResolvedValue(undefined),
    bulkUpsert: vi.fn().mockResolvedValue(undefined),
    getSelectedId: vi.fn().mockResolvedValue(selectedId),
    setSelectedId: vi.fn().mockResolvedValue(undefined),
  }
}

const session: SessionSummary = {
  id: 'session-1',
  agentId: 'agent-1',
  title: 'Conversation 1',
  createdAt: '2026-06-28T00:00:00.000Z',
  updatedAt: '2026-06-28T00:00:00.000Z',
}

describe('sessionStorage', () => {
  it('migrates legacy sessions and selected session metadata into desktop storage', async () => {
    localStorage.setItem('connectonion.sessions', JSON.stringify([session]))
    localStorage.setItem('connectonion.activeSessionId', session.id)

    const desktop = createDesktopSessions([session], session.id)
    window.desktopSessions = desktop

    await expect(readInitialSessions()).resolves.toEqual({
      sessions: [session],
      selectedSessionId: session.id,
    })
    expect(desktop.bulkUpsert).toHaveBeenCalledWith([session])
    expect(desktop.setSelectedId).toHaveBeenCalledWith(session.id)
    expect(localStorage.getItem('connectonion.sqliteMigrated.sessions')).toBe('1')
  })

  it('filters invalid legacy sessions in browser fallback mode', async () => {
    localStorage.setItem(
      'connectonion.sessions',
      JSON.stringify([session, { id: 'broken', agentId: '', title: '' }]),
    )
    localStorage.setItem('connectonion.activeSessionId', session.id)

    await expect(readInitialSessions()).resolves.toEqual({
      sessions: [session],
      selectedSessionId: session.id,
    })
  })

  it('creates sessions through the desktop bridge', async () => {
    const desktop = createDesktopSessions()
    window.desktopSessions = desktop

    await persistSessionListChange(session, [session])

    expect(desktop.create).toHaveBeenCalledWith(session)
    expect(localStorage.getItem('connectonion.sessions')).toBeNull()
  })

  it('falls back to localStorage when desktop session writes fail', async () => {
    const desktop = createDesktopSessions()
    vi.mocked(desktop.update).mockRejectedValue(new Error('sqlite locked'))
    window.desktopSessions = desktop

    const updated = { ...session, title: 'New title' }
    await persistSessionTouch(updated, [updated])

    expect(localStorage.getItem('connectonion.sessions')).toBe(
      JSON.stringify([updated]),
    )
  })
})
