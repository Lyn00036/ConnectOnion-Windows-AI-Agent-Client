export type DesktopWindowState = {
  isMaximized: boolean
}

export type DesktopWindowApi = {
  close: () => Promise<void>
  getState: () => Promise<DesktopWindowState>
  minimize: () => Promise<void>
  onStateChange: (handler: (state: DesktopWindowState) => void) => () => void
  toggleMaximize: () => Promise<DesktopWindowState>
}

declare global {
  interface Window {
    desktopWindow?: DesktopWindowApi
  }
}
