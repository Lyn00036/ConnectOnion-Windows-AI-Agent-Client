/** API functions reserved for future settings read and update endpoints. */

import type { SettingsState } from './types'

export async function getSettings(): Promise<SettingsState> {
  return {
    theme: 'system',
  }
}
