/** Session history page reserved for stored conversations and message history. */

import { SessionSidebar } from '../features/sessions/components/SessionSidebar'

export function SessionsPage() {
  return (
    <section className="content-page">
      <SessionSidebar />
    </section>
  )
}
