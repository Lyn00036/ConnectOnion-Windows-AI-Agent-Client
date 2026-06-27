/**
 * Agent persistence adapter.
 *
 * The agents store keeps the working copy in memory (Zustand) and treats this
 * module as its durable backing store:
 * - When the Electron SQLite bridge (`window.desktopAgents`) is available, all
 *   reads/writes go through it. SQLite is the single source of truth.
 * - When it is not (e.g. running the renderer in a plain browser during dev),
 *   we gracefully fall back to localStorage.
 *
 * On first launch under SQLite we run a one-time migration of any legacy
 * localStorage agents into the database, guarded by a marker so it only ever
 * happens once.
 */

import type { DesktopAgentRecord } from '../../shared/storage/agents'
import { normalizeUrl } from '../../shared/utils/url'
import type { AgentConfig } from './types'

const AGENTS_STORAGE_KEY = 'connectonion.agents'
const SELECTED_AGENT_STORAGE_KEY = 'connectonion.selectedAgentId'
const MIGRATION_FLAG_KEY = 'connectonion.sqliteMigrated.agents'

function getDesktopAgents() {
  return window.desktopAgents ?? null
}

function toRecord(agent: AgentConfig): DesktopAgentRecord {
  return {
    id: agent.id,
    name: agent.name,
    address: agent.address,
    directUrl: agent.directUrl ?? null,
  }
}

function toConfig(record: DesktopAgentRecord): AgentConfig {
  return {
    id: record.id,
    name: record.name,
    address: record.address,
    directUrl: record.directUrl ?? undefined,
  }
}

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
  localStorage.setItem(AGENTS_STORAGE_KEY, JSON.stringify(agents))
}

function saveSelectedIdToLocalStorage(selectedAgentId: string | null) {
  if (selectedAgentId) {
    localStorage.setItem(SELECTED_AGENT_STORAGE_KEY, selectedAgentId)
  } else {
    localStorage.removeItem(SELECTED_AGENT_STORAGE_KEY)
  }
}

async function migrateLegacyAgentsOnce(
  desktop: NonNullable<ReturnType<typeof getDesktopAgents>>,
) {
  if (localStorage.getItem(MIGRATION_FLAG_KEY)) return

  const legacyAgents = loadAgentsFromLocalStorage()
  if (legacyAgents.length > 0) {
    await desktop.bulkUpsert(legacyAgents.map(toRecord))
    const legacySelected = loadSelectedIdFromLocalStorage(legacyAgents)
    if (legacySelected) await desktop.setSelectedId(legacySelected)
  }

  localStorage.setItem(MIGRATION_FLAG_KEY, '1')
}

export type InitialAgentsData = {
  agents: AgentConfig[]
  selectedAgentId: string | null
}

export async function readInitialAgents(): Promise<InitialAgentsData> {
  const desktop = getDesktopAgents()

  if (!desktop) {
    const agents = loadAgentsFromLocalStorage()
    return { agents, selectedAgentId: loadSelectedIdFromLocalStorage(agents) }
  }

  await migrateLegacyAgentsOnce(desktop)

  const records = await desktop.list()
  const agents = records.map(toConfig)
  const storedSelectedId = await desktop.getSelectedId()
  const selectedAgentId = agents.some((agent) => agent.id === storedSelectedId)
    ? storedSelectedId
    : (agents[0]?.id ?? null)

  return { agents, selectedAgentId }
}

export async function persistCreateAgent(
  agent: AgentConfig,
  agentsAfter: AgentConfig[],
) {
  const desktop = getDesktopAgents()
  if (desktop) {
    await desktop.create(toRecord(agent))
    return
  }
  saveAgentsToLocalStorage(agentsAfter)
}

export async function persistUpdateAgent(
  agent: AgentConfig,
  agentsAfter: AgentConfig[],
) {
  const desktop = getDesktopAgents()
  if (desktop) {
    await desktop.update(agent.id, {
      name: agent.name,
      address: agent.address,
      directUrl: agent.directUrl ?? null,
    })
    return
  }
  saveAgentsToLocalStorage(agentsAfter)
}

export async function persistRemoveAgent(
  agentId: string,
  agentsAfter: AgentConfig[],
) {
  const desktop = getDesktopAgents()
  if (desktop) {
    await desktop.remove(agentId)
    return
  }
  saveAgentsToLocalStorage(agentsAfter)
}

export async function persistSelectedAgentId(selectedAgentId: string | null) {
  const desktop = getDesktopAgents()
  if (desktop) {
    await desktop.setSelectedId(selectedAgentId)
    return
  }
  saveSelectedIdToLocalStorage(selectedAgentId)
}
