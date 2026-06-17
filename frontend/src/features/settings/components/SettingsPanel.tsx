/** Settings panel placeholder for future desktop preferences and defaults. */

// TODO(settings-ui): Replace this placeholder with settings grouped by concern:
// - Appearance
// - Agent defaults
// - Local data/history
// - Network timeout and retry behavior
// - Secure credential management status

import { EmptyState } from '../../../shared/components/EmptyState'

export function SettingsPanel() {
  return (
    <EmptyState
      title="Settings"
      description="Desktop preferences and local storage options will live here."
    />
  )
}
