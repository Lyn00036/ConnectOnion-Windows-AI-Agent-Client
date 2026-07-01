/**
 * Agent persistence adapter.
 *
 * Uses localStorage as the single durable store.
 */

import { normalizeUrl } from '../../shared/utils/url'
import type { AgentConfig } from './types'

const AGENTS_STORAGE_KEY = 'connectonion.agents'
const SELECTED_AGENT_STORAGE_KEY = 'connectonion.selectedAgentId'

function loadAgentsFromLocalStorage(): AgentConfig[] {
  try {
    const stored = localStorage.getItem(AGENTS_STORAGE_KEY)
    if (!stored) return []

    const agents = JSON.parse(stored) as Array<AgentConfig & { url?: string }>
    if (!Array.isArray(agents)) return []

    return agents
      .filter((agent) => agent.name && agent.id !== 'local-agent')
      .map((agent) => {
        if (agent.address) return agent

        return {
          id: agent.id,
          name: agent.name,
          address: agent.url ?? agent.name,
          directUrl: agent.url ? normalizeUrl(agent.url) : undefined,
        }
      })
      .filter((agent) => agent.address)
  } catch {
    return []
  }
}

function loadSelectedIdFromLocalStorage(agents: AgentConfig[]): string | null {
  const stored = localStorage.getItem(SELECTED_AGENT_STORAGE_KEY)
  return agents.some((agent) => agent.id === stored) ? stored! : null
}

function saveAgentsToLocalStorage(agents: AgentConfig[]) {
  try {
    localStorage.setItem(AGENTS_STORAGE_KEY, JSON.stringify(agents))
  } catch {
    // Keep in-memory state working if storage is unavailable.
  }
}

function saveSelectedIdToLocalStorage(selectedAgentId: string | null) {
  try {
    if (selectedAgentId) {
      localStorage.setItem(SELECTED_AGENT_STORAGE_KEY, selectedAgentId)
    } else {
      localStorage.removeItem(SELECTED_AGENT_STORAGE_KEY)
    }
  } catch {
    // Keep in-memory state working if storage is unavailable.
  }
}

export type InitialAgentsData = {
  agents: AgentConfig[]
  selectedAgentId: string | null
}

export async function readInitialAgents(): Promise<InitialAgentsData> {
  const agents = loadAgentsFromLocalStorage()

  return { agents, selectedAgentId: loadSelectedIdFromLocalStorage(agents) }
}

export async function persistCreateAgent(
  _agent: AgentConfig,
  agentsAfter: AgentConfig[],
) {
  saveAgentsToLocalStorage(agentsAfter)
}

export async function persistUpdateAgent(
  _agent: AgentConfig,
  agentsAfter: AgentConfig[],
) {
  saveAgentsToLocalStorage(agentsAfter)
}

export async function persistRemoveAgent(
  _agentId: string,
  agentsAfter: AgentConfig[],
) {
  saveAgentsToLocalStorage(agentsAfter)
}

export async function persistSelectedAgentId(selectedAgentId: string | null) {
  saveSelectedIdToLocalStorage(selectedAgentId)
}
