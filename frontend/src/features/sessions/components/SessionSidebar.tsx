/** Session sidebar placeholder for future local conversation history navigation. */

// TODO(session-ui): Replace this placeholder with a real session list.
// Expected UI states:
// - Empty history
// - Loading saved sessions
// - Selected session
// - Rename/delete actions
// - Search or filter when the session count grows

import { EmptyState } from '../../../shared/components/EmptyState'

export function SessionSidebar() {
  return (
    <EmptyState
      title="Session history"
      description="Saved local conversations will appear here when session storage is added."
    />
  )
}
