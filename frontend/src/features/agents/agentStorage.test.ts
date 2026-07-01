import { describe, expect, it } from 'vitest'

import type { AgentConfig } from './types'
import {
  persistCreateAgent,
  persistSelectedAgentId,
  readInitialAgents,
} from './agentStorage'

describe('agentStorage', () => {
  it('reads agents from localStorage and normalizes older url-only records', async () => {
    localStorage.setItem(
      'connectonion.agents',
      JSON.stringify([
        { id: 'agent-1', name: 'Relay agent', address: '0xabc' },
        { id: 'local-agent', name: 'Old placeholder', address: 'local' },
        { id: 'agent-2', name: 'Local agent', url: 'http://127.0.0.1:8010/' },
      ]),
    )
    localStorage.setItem('connectonion.selectedAgentId', 'agent-2')

    await expect(readInitialAgents()).resolves.toEqual({
      agents: [
        { id: 'agent-1', name: 'Relay agent', address: '0xabc' },
        {
          id: 'agent-2',
          name: 'Local agent',
          address: 'http://127.0.0.1:8010/',
          directUrl: 'http://127.0.0.1:8010',
        },
      ],
      selectedAgentId: 'agent-2',
    })
  })

  it('ignores missing selected agent ids', async () => {
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

  it('persists agent changes to localStorage', async () => {
    const agent: AgentConfig = {
      id: 'agent-1',
      name: 'Local',
      address: 'http://127.0.0.1:8010',
      directUrl: 'http://127.0.0.1:8010',
    }

    await persistCreateAgent(agent, [agent])

    expect(localStorage.getItem('connectonion.agents')).toBe(JSON.stringify([agent]))
  })

  it('persists and clears selected agent ids in localStorage', async () => {
    await persistSelectedAgentId('agent-1')
    expect(localStorage.getItem('connectonion.selectedAgentId')).toBe('agent-1')

    await persistSelectedAgentId(null)
    expect(localStorage.getItem('connectonion.selectedAgentId')).toBeNull()
  })
})
