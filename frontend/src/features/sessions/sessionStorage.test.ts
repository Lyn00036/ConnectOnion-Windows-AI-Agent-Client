import { describe, expect, it } from 'vitest'

import type { SessionSummary } from './types'
import {
  persistSelectedSessionId,
  persistSessionListChange,
  persistSessionTouch,
  readInitialSessions,
} from './sessionStorage'

const session: SessionSummary = {
  id: 'session-1',
  agentId: 'agent-1',
  title: 'Conversation 1',
  createdAt: '2026-06-28T00:00:00.000Z',
  updatedAt: '2026-06-28T00:00:00.000Z',
}

describe('sessionStorage', () => {
  it('reads valid sessions and selected session metadata from localStorage', async () => {
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

  it('ignores missing selected session ids', async () => {
    localStorage.setItem('connectonion.sessions', JSON.stringify([session]))
    localStorage.setItem('connectonion.activeSessionId', 'missing-session')

    await expect(readInitialSessions()).resolves.toEqual({
      sessions: [session],
      selectedSessionId: null,
    })
  })

  it('persists session list changes to localStorage', async () => {
    await persistSessionListChange(session, [session])

    expect(localStorage.getItem('connectonion.sessions')).toBe(
      JSON.stringify([session]),
    )
  })

  it('persists touched sessions and selected ids to localStorage', async () => {
    const updated = { ...session, title: 'New title' }

    await persistSessionTouch(updated, [updated])
    await persistSelectedSessionId(updated.id)

    expect(localStorage.getItem('connectonion.sessions')).toBe(
      JSON.stringify([updated]),
    )
    expect(localStorage.getItem('connectonion.activeSessionId')).toBe(updated.id)
  })
})
