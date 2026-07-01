/** Zustand store for local agent selection and temporary agent configuration state. */

// Persistence lives in `agentStorage.ts` and uses localStorage. This store keeps
// the in-memory working copy and writes through optimistically.

import { create } from 'zustand'

import { normalizeUrl } from '../../shared/utils/url'
import {
  persistCreateAgent,
  persistRemoveAgent,
  persistSelectedAgentId,
  persistUpdateAgent,
  readInitialAgents,
} from './agentStorage'
import type { AgentConfig } from './types'

type AgentState = {
  agents: AgentConfig[]
  selectedAgentId: string | null
  error: string | null
  hydrated: boolean
  hydrate: () => Promise<void>
  addAgent: (name: string, address: string, directUrl?: string) => void
  updateAgent: (agentId: string, name: string, address: string, directUrl?: string) => void
  removeAgent: (agentId: string) => void
  selectAgent: (agentId: string) => void
  setError: (error: string | null) => void
}

let hydrateStarted = false

export const useAgentStore = create<AgentState>((set, get) => ({
  agents: [],
  selectedAgentId: null,
  error: null,
  hydrated: false,
  hydrate: async () => {
    if (hydrateStarted) return
    hydrateStarted = true
    try {
      const { agents, selectedAgentId } = await readInitialAgents()
      set({ agents, selectedAgentId, hydrated: true })
    } catch {
      set({ hydrated: true, error: 'Failed to load saved agents.' })
    }
  },
  addAgent: (name, address, directUrl) => {
    const trimmedDirectUrl = directUrl?.trim()
    const normalizedDirectUrl = trimmedDirectUrl
      ? normalizeUrl(trimmedDirectUrl)
      : undefined
    const resolvedAddress = address.trim() || normalizedDirectUrl

    if (!name.trim() || !resolvedAddress) {
      set({ error: 'An agent address or Direct URL is required.' })
      return
    }

    const agent: AgentConfig = {
      id: `${Date.now()}`,
      name: name.trim(),
      address: resolvedAddress,
      directUrl: normalizedDirectUrl,
    }
    const nextAgents = [...get().agents, agent]

    set({ agents: nextAgents, selectedAgentId: agent.id, error: null })
    void persistCreateAgent(agent, nextAgents).catch(() =>
      set({ error: 'Failed to save the agent.' }),
    )
    void persistSelectedAgentId(agent.id)
  },
  updateAgent: (agentId, name, address, directUrl) => {
    const trimmedDirectUrl = directUrl?.trim()
    const normalizedDirectUrl = trimmedDirectUrl
      ? normalizeUrl(trimmedDirectUrl)
      : undefined
    const resolvedAddress = address.trim() || normalizedDirectUrl

    if (!name.trim() || !resolvedAddress) {
      set({ error: 'An agent address or Direct URL is required.' })
      return
    }

    let updatedAgent: AgentConfig | undefined
    const nextAgents = get().agents.map((agent) => {
      if (agent.id !== agentId) return agent
      updatedAgent = {
        ...agent,
        name: name.trim(),
        address: resolvedAddress,
        directUrl: normalizedDirectUrl,
      }
      return updatedAgent
    })

    if (!updatedAgent) return

    set({ agents: nextAgents, error: null })
    void persistUpdateAgent(updatedAgent, nextAgents).catch(() =>
      set({ error: 'Failed to update the agent.' }),
    )
  },
  removeAgent: (agentId) => {
    const currentAgents = get().agents
    const nextAgents = currentAgents.filter((agent) => agent.id !== agentId)
    const selectedAgentId =
      get().selectedAgentId === agentId
        ? nextAgents[0]?.id ?? null
        : get().selectedAgentId

    set({ agents: nextAgents, selectedAgentId, error: null })
    void persistRemoveAgent(agentId, nextAgents).catch(() =>
      set({ error: 'Failed to remove the agent.' }),
    )
    void persistSelectedAgentId(selectedAgentId)
  },
  selectAgent: (agentId) => {
    set({ selectedAgentId: agentId, error: null })
    void persistSelectedAgentId(agentId)
  },
  setError: (error) => set({ error }),
}))
