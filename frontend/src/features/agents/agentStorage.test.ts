import { describe, expect, it, vi } from 'vitest'

import type { DesktopAgentsApi } from '../../shared/storage/agents'
import type { AgentConfig } from './types'
import {
  persistCreateAgent,
  persistSelectedAgentId,
  readInitialAgents,
} from './agentStorage'

function createDesktopAgents(
  records: Awaited<ReturnType<DesktopAgentsApi['list']>> = [],
  selectedId: string | null = null,
): DesktopAgentsApi {
  return {
    list: vi.fn().mockResolvedValue(records),
    create: vi.fn().mockResolvedValue(undefined),
    update: vi.fn().mockResolvedValue(undefined),
    remove: vi.fn().mockResolvedValue(undefined),
    bulkUpsert: vi.fn().mockResolvedValue(undefined),
    getSelectedId: vi.fn().mockResolvedValue(selectedId),
    setSelectedId: vi.fn().mockResolvedValue(undefined),
  }
}

describe('agentStorage', () => {
  it('migrates legacy localStorage agents into the Electron bridge once', async () => {
    localStorage.setItem(
      'connectonion.agents',
      JSON.stringify([
        { id: 'agent-1', name: 'Relay agent', address: '0xabc' },
        { id: 'local-agent', name: 'Old placeholder', address: 'local' },
        { id: 'agent-2', name: 'Local agent', url: 'http://127.0.0.1:8010/' },
      ]),
    )
    localStorage.setItem('connectonion.selectedAgentId', 'agent-1')

    const desktop = createDesktopAgents(
      [
        {
          id: 'agent-1',
          name: 'Relay agent',
          address: '0xabc',
          directUrl: null,
        },
        {
          id: 'agent-2',
          name: 'Local agent',
          address: 'http://127.0.0.1:8010/',
          directUrl: 'http://127.0.0.1:8010',
        },
      ],
      'agent-1',
    )
    window.desktopAgents = desktop

    const result = await readInitialAgents()

    expect(desktop.bulkUpsert).toHaveBeenCalledWith([
      {
        id: 'agent-1',
        name: 'Relay agent',
        address: '0xabc',
        directUrl: null,
      },
      {
        id: 'agent-2',
        name: 'Local agent',
        address: 'http://127.0.0.1:8010/',
        directUrl: 'http://127.0.0.1:8010',
      },
    ])
    expect(desktop.setSelectedId).toHaveBeenCalledWith('agent-1')
    expect(localStorage.getItem('connectonion.sqliteMigrated.agents')).toBe('1')
    expect(result).toEqual({
      agents: [
        {
          id: 'agent-1',
          name: 'Relay agent',
          address: '0xabc',
          directUrl: undefined,
        },
        {
          id: 'agent-2',
          name: 'Local agent',
          address: 'http://127.0.0.1:8010/',
          directUrl: 'http://127.0.0.1:8010',
        },
      ],
      selectedAgentId: 'agent-1',
    })
  })

  it('falls back to localStorage when the Electron bridge is unavailable', async () => {
    localStorage.setItem(
      'connectonion.agents',
      JSON.stringify([{ id: 'agent-1', name: 'Relay agent', address: '0xabc' }]),
    )
    localStorage.setItem('connectonion.selectedAgentId', 'missing-agent')

    await expect(readInitialAgents()).resolves.toEqual({
      agents: [{ id: 'agent-1', name: 'Relay agent', address: '0xabc' }],
      selectedAgentId: null,
    })
  })

  it('falls back to localStorage when an Electron write fails', async () => {
    const desktop = createDesktopAgents()
    vi.mocked(desktop.create).mockRejectedValue(new Error('sqlite locked'))
    window.desktopAgents = desktop

    const agent: AgentConfig = {
      id: 'agent-1',
      name: 'Local',
      address: 'http://127.0.0.1:8010',
      directUrl: 'http://127.0.0.1:8010',
    }

    await persistCreateAgent(agent, [agent])

    expect(localStorage.getItem('connectonion.agents')).toBe(JSON.stringify([agent]))
  })

  it('persists selected agent ids through desktop storage when available', async () => {
    const desktop = createDesktopAgents()
    window.desktopAgents = desktop

    await persistSelectedAgentId('agent-1')

    expect(desktop.setSelectedId).toHaveBeenCalledWith('agent-1')
    expect(localStorage.getItem('connectonion.selectedAgentId')).toBeNull()
  })
})
