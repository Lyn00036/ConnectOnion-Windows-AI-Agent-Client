export type DesktopSessionRecord = {
  id: string
  agentId: string
  title: string
  createdAt: string
  updatedAt: string
}

export type DesktopSessionUpdate = {
  title: string
  updatedAt: string
}

export type DesktopSessionsApi = {
  list: () => Promise<DesktopSessionRecord[]>
  create: (session: DesktopSessionRecord) => Promise<void>
  update: (id: string, fields: DesktopSessionUpdate) => Promise<void>
  remove: (id: string) => Promise<void>
  removeForAgent: (agentId: string) => Promise<void>
  bulkUpsert: (sessions: DesktopSessionRecord[]) => Promise<void>
  getSelectedId: () => Promise<string | null>
  setSelectedId: (id: string | null) => Promise<void>
}

declare global {
  interface Window {
    desktopSessions?: DesktopSessionsApi
  }
}
