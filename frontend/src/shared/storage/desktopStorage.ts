import type { StorageStatus } from './types'

export function isDesktopStorageAvailable() {
  return Boolean(window.desktopStorage)
}

export async function getStorageStatus(): Promise<StorageStatus> {
  if (!window.desktopStorage) {
    return {
      available: false,
      driver: 'browser',
      reason: 'Electron preload storage bridge is not available.',
    }
  }

  return window.desktopStorage.getStatus()
}
