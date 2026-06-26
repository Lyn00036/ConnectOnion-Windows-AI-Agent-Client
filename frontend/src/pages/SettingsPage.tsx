/** Desktop settings page wrapper. */

import { SettingsPanel } from '../features/settings/components/SettingsPanel'

export function SettingsPage() {
  return (
    <section
      aria-label="Settings"
      className="min-h-full w-full overflow-y-auto bg-background"
    >
      <SettingsPanel />
    </section>
  )
}
