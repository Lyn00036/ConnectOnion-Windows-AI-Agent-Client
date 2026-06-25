/** Agent management page for locally saved ConnectOnion connections. */

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'

import { AgentForm } from '../features/agents/components/AgentForm'
import { AgentList } from '../features/agents/components/AgentList'
import { useAgentStore } from '../features/agents/store'

export function AgentsPage() {
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const error = useAgentStore((state) => state.error)
  const addAgent = useAgentStore((state) => state.addAgent)
  const removeAgent = useAgentStore((state) => state.removeAgent)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const selectedAgent = agents.find((agent) => agent.id === selectedAgentId)

  return (
    <section className="min-h-full bg-background px-[clamp(28px,5vw,72px)] py-[54px] max-[760px]:px-5 max-[760px]:py-[30px]">
      <header className="flex items-start justify-between gap-6 border-b border-border pb-[26px]">
        <div>
          <span className="text-[11px] font-extrabold uppercase tracking-[0.12em] text-muted-foreground">Connections</span>
          <h1 className="my-2 text-[clamp(28px,4vw,40px)] font-bold text-foreground">Agents</h1>
          <p className="m-0 text-sm text-muted-foreground">Choose the agent used by Chat, or add another connection.</p>
        </div>
        <Badge className="h-auto bg-secondary px-2.5 py-2 text-[11px] font-bold text-secondary-foreground" variant="secondary">{agents.length} saved</Badge>
      </header>

      <div className="mt-7 grid grid-cols-[minmax(0,1.35fr)_minmax(280px,0.65fr)] gap-7 max-[900px]:grid-cols-1">
        <div className="rounded-[14px] border border-border bg-card">
          <AgentList
            agents={agents}
            onSelectAgent={selectAgent}
            selectedAgentId={selectedAgentId}
          />
          {selectedAgent && (
            <div className="flex min-h-[58px] items-center justify-between gap-4 border-t border-border px-4 py-2.5 text-xs text-muted-foreground">
              <span><strong>{selectedAgent.name}</strong> is active</span>
              <Button className="min-h-[34px] text-xs font-bold" onClick={() => removeAgent(selectedAgent.id)} size="sm" type="button" variant="destructive">
                Remove
              </Button>
            </div>
          )}
        </div>

        <aside className="rounded-[14px] border border-border bg-muted p-[22px]">
          <h2 className="mb-1.5 text-[17px] font-bold text-foreground">Add an agent</h2>
          <p className="mb-[18px] text-xs text-muted-foreground">Use a relay address, a Direct URL, or both.</p>
          <AgentForm onAddAgent={addAgent} />
          {error && <div className="mt-3 rounded-lg border border-destructive/20 bg-destructive/10 p-2.5 text-xs text-destructive">{error}</div>}
        </aside>
      </div>
    </section>
  )
}
