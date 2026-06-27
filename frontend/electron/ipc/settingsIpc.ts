import { ipcMain } from 'electron'

import { settingsChannels } from './settingsChannels.ts'
import type { DesktopStorageService } from '../storage/service.ts'
import {
  bulkUpsertSettings,
  getSetting,
  removeSetting,
  setSetting,
} from '../storage/settingsRepository.ts'

export function registerSettingsIpc(storage: DesktopStorageService) {
  ipcMain.handle(settingsChannels.get, (_event, key: string) => {
    return getSetting(storage.getDatabase(), key)
  })

  ipcMain.handle(settingsChannels.set, (_event, key: string, valueJson: string) => {
    setSetting(storage.getDatabase(), key, valueJson)
  })

  ipcMain.handle(settingsChannels.remove, (_event, key: string) => {
    removeSetting(storage.getDatabase(), key)
  })

  ipcMain.handle(
    settingsChannels.bulkUpsert,
    (_event, entries: Array<{ key: string; valueJson: string }>) => {
      bulkUpsertSettings(storage.getDatabase(), entries)
    },
  )
}
