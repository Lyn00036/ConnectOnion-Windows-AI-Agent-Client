/** Zustand store for local agent selection and temporary agent configuration state. */

import { create } from 'zustand'

import { normalizeUrl } from '../../shared/utils/url'
import type { AgentConfig } from './types'

const AGENTS_STORAGE_KEY = 'connectonion.agents'
const SELECTED_AGENT_STORAGE_KEY = 'connectonion.selectedAgentId'

type AgentState = {
  agents: AgentConfig[]
  selectedAgentId: string | null
  error: string | null
  addAgent: (name: string, url: string) => void
  removeAgent: (agentId: string) => void
  selectAgent: (agentId: string) => void
  setError: (error: string | null) => void
}

function loadAgents() {
  try {
    const stored = localStorage.getItem(AGENTS_STORAGE_KEY)
    if (!stored) return []

    const agents = JSON.parse(stored) as AgentConfig[]
    if (!Array.isArray(agents)) return []

    return agents.filter(
      (agent) => agent.name && agent.url && agent.id !== 'local-agent',
    )
  } catch {
    return []
  }
}

function saveAgents(agents: AgentConfig[]) {
  localStorage.setItem(AGENTS_STORAGE_KEY, JSON.stringify(agents))
}

function loadSelectedAgentId(agents: AgentConfig[]) {
  const stored = localStorage.getItem(SELECTED_AGENT_STORAGE_KEY)
  return agents.some((agent) => agent.id === stored) ? stored! : null
}

const initialAgents = loadAgents()

export const useAgentStore = create<AgentState>((set, get) => ({
  agents: initialAgents,
  selectedAgentId: loadSelectedAgentId(initialAgents),
  error: null,
  addAgent: (name, url) => {
    const agent: AgentConfig = {
      id: `${Date.now()}`,
      name: name.trim(),
      url: normalizeUrl(url),
    }
    const nextAgents = [...get().agents, agent]

    saveAgents(nextAgents)
    localStorage.setItem(SELECTED_AGENT_STORAGE_KEY, agent.id)
    set({
      agents: nextAgents,
      selectedAgentId: agent.id,
      error: null,
    })
  },
  removeAgent: (agentId) => {
    const currentAgents = get().agents

    const nextAgents = currentAgents.filter((agent) => agent.id !== agentId)
    const selectedAgentId =
      get().selectedAgentId === agentId ? nextAgents[0]?.id ?? null : get().selectedAgentId

    saveAgents(nextAgents)
    if (selectedAgentId) {
      localStorage.setItem(SELECTED_AGENT_STORAGE_KEY, selectedAgentId)
    } else {
      localStorage.removeItem(SELECTED_AGENT_STORAGE_KEY)
    }
    set({
      agents: nextAgents,
      selectedAgentId,
      error: null,
    })
  },
  selectAgent: (agentId) => {
    localStorage.setItem(SELECTED_AGENT_STORAGE_KEY, agentId)
    set({
      selectedAgentId: agentId,
      error: null,
    })
  },
  setError: (error) => set({ error }),
}))
