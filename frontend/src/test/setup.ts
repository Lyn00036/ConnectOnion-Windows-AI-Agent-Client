import '@testing-library/jest-dom/vitest'

import { afterEach, vi } from 'vitest'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  localStorage.clear()

  delete window.desktopStorage
  delete window.desktopAgents
  delete window.desktopSessions
  delete window.desktopSettings
})
