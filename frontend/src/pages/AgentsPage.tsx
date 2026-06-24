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
    <section className="min-h-svh bg-white px-[clamp(28px,5vw,72px)] py-[54px] max-[760px]:px-5 max-[760px]:py-[30px]">
      <header className="flex items-start justify-between gap-6 border-b border-[#eceef2] pb-[26px]">
        <div>
          <span className="text-[11px] font-extrabold uppercase tracking-[0.12em] text-[#a18ac9]">Connections</span>
          <h1 className="my-2 text-[clamp(28px,4vw,40px)] font-bold tracking-[-0.04em] text-[#17181b]">Agents</h1>
          <p className="m-0 text-sm text-[#777b84]">Choose the agent used by Chat, or add another connection.</p>
        </div>
        <Badge className="h-auto bg-[#f2eef9] px-2.5 py-2 text-[11px] font-bold text-[#755f9b]" variant="secondary">{agents.length} saved</Badge>
      </header>

      <div className="mt-7 grid grid-cols-[minmax(0,1.35fr)_minmax(280px,0.65fr)] gap-7 max-[900px]:grid-cols-1">
        <div className="rounded-[14px] border border-[#e7e8eb] bg-white">
          <AgentList
            agents={agents}
            onSelectAgent={selectAgent}
            selectedAgentId={selectedAgentId}
          />
          {selectedAgent && (
            <div className="flex min-h-[58px] items-center justify-between gap-4 border-t border-[#eceef2] px-4 py-2.5 text-xs text-[#777b84]">
              <span><strong>{selectedAgent.name}</strong> is active</span>
              <Button className="min-h-[34px] text-xs font-bold" onClick={() => removeAgent(selectedAgent.id)} size="sm" type="button" variant="destructive">
                Remove
              </Button>
            </div>
          )}
        </div>

        <aside className="rounded-[14px] border border-[#e7e8eb] bg-[#fafafa] p-[22px]">
          <h2 className="mb-1.5 text-[17px] font-bold text-[#202124]">Add an agent</h2>
          <p className="mb-[18px] text-xs text-[#858891]">Use a relay address, a Direct URL, or both.</p>
          <AgentForm onAddAgent={addAgent} />
          {error && <div className="mt-3 rounded-lg border border-red-200 bg-red-50 p-2.5 text-xs text-red-700">{error}</div>}
        </aside>
      </div>
    </section>
  )
}
