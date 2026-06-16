/** Agent management page reserved for persistent backend-backed agent CRUD. */

import { EmptyState } from '../shared/components/EmptyState'

export function AgentsPage() {
  return (
    <section className="content-page">
      <EmptyState
        title="Agent management"
        description="Persistent agent configuration will live here once the backend agent CRUD API is implemented."
      />
    </section>
  )
}
