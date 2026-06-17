/** Agent management page for locally saved ConnectOnion agent configs. */

import { EmptyState } from '../shared/components/EmptyState'

export function AgentsPage() {
  return (
    <section className="content-page">
      <EmptyState
        title="Agent management"
        description="Agent configs are saved locally from the chat sidebar."
      />
    </section>
  )
}
