export type DesktopStorageStatus = {
  available: true
  driver: 'sqlite'
  databasePath: string
  schemaVersion: number
}

export type BrowserStorageStatus = {
  available: false
  driver: 'browser'
  reason: string
}

export type StorageStatus = DesktopStorageStatus | BrowserStorageStatus

export type DesktopStorageApi = {
  getStatus: () => Promise<DesktopStorageStatus>
}

declare global {
  interface Window {
    desktopStorage?: DesktopStorageApi
  }
}
