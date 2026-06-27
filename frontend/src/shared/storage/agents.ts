export type DesktopAgentRecord = {
  id: string
  name: string
  address: string
  directUrl: string | null
}

export type DesktopAgentUpdate = {
  name: string
  address: string
  directUrl: string | null
}

export type DesktopAgentsApi = {
  list: () => Promise<DesktopAgentRecord[]>
  create: (agent: DesktopAgentRecord) => Promise<void>
  update: (id: string, fields: DesktopAgentUpdate) => Promise<void>
  remove: (id: string) => Promise<void>
  bulkUpsert: (agents: DesktopAgentRecord[]) => Promise<void>
  getSelectedId: () => Promise<string | null>
  setSelectedId: (id: string | null) => Promise<void>
}

declare global {
  interface Window {
    desktopAgents?: DesktopAgentsApi
  }
}
