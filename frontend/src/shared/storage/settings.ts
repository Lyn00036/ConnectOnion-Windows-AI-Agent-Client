export type DesktopSettingEntry = {
  key: string
  valueJson: string
}

export type DesktopSettingsApi = {
  get: (key: string) => Promise<string | null>
  set: (key: string, valueJson: string) => Promise<void>
  remove: (key: string) => Promise<void>
  bulkUpsert: (entries: DesktopSettingEntry[]) => Promise<void>
}

declare global {
  interface Window {
    desktopSettings?: DesktopSettingsApi
  }
}
