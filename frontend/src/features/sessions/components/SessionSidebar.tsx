/** Session sidebar placeholder for future conversation history navigation. */

import { EmptyState } from '../../../shared/components/EmptyState'

export function SessionSidebar() {
  return (
    <EmptyState
      title="Session history"
      description="Saved conversations will appear here after session persistence is connected."
    />
  )
}
