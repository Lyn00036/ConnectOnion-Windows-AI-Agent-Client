/**
 * Agent persistence adapter.
 *
 * - Electron + SQLite is the primary store when preload bridges exist.
 * - localStorage is the fallback when bridges are missing (browser dev) or when
 *   an IPC/SQLite call fails at runtime.
 * - Legacy localStorage is migrated once, then cleared by legacyLocalStorage.ts.
 */

import type { DesktopAgentRecord } from '../../shared/storage/agents'
import { withDesktopFallback } from '../../shared/storage/desktopFallback'
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

async function readFromDesktop(
  desktop: NonNullable<ReturnType<typeof getDesktopAgents>>,
): Promise<{ agents: AgentConfig[]; selectedAgentId: string | null }> {
  await migrateLegacyAgentsOnce(desktop)
  const records = await desktop.list()
  const agents = records.map(toConfig)
  const storedSelectedId = await desktop.getSelectedId()
  const selectedAgentId = agents.some((agent) => agent.id === storedSelectedId)
    ? storedSelectedId
    : (agents[0]?.id ?? null)
  return { agents, selectedAgentId }
}

export type InitialAgentsData = {
  agents: AgentConfig[]
  selectedAgentId: string | null
}

export async function readInitialAgents(): Promise<InitialAgentsData> {
  const desktop = getDesktopAgents()
  const agents = loadAgentsFromLocalStorage()

  return withDesktopFallback(
    Boolean(desktop),
    () => readFromDesktop(desktop!),
    () => ({ agents, selectedAgentId: loadSelectedIdFromLocalStorage(agents) }),
  )
}

export async function persistCreateAgent(
  agent: AgentConfig,
  agentsAfter: AgentConfig[],
) {
  const desktop = getDesktopAgents()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.create(toRecord(agent)).then(() => undefined),
    () => saveAgentsToLocalStorage(agentsAfter),
  )
}

export async function persistUpdateAgent(
  agent: AgentConfig,
  agentsAfter: AgentConfig[],
) {
  const desktop = getDesktopAgents()
  await withDesktopFallback(
    Boolean(desktop),
    () =>
      desktop!.update(agent.id, {
        name: agent.name,
        address: agent.address,
        directUrl: agent.directUrl ?? null,
      }).then(() => undefined),
    () => saveAgentsToLocalStorage(agentsAfter),
  )
}

export async function persistRemoveAgent(
  agentId: string,
  agentsAfter: AgentConfig[],
) {
  const desktop = getDesktopAgents()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.remove(agentId).then(() => undefined),
    () => saveAgentsToLocalStorage(agentsAfter),
  )
}

export async function persistSelectedAgentId(selectedAgentId: string | null) {
  const desktop = getDesktopAgents()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.setSelectedId(selectedAgentId).then(() => undefined),
    () => saveSelectedIdToLocalStorage(selectedAgentId),
  )
}
