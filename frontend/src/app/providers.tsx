/** Application-level providers for routing-independent React infrastructure. */

import { useEffect, type ReactNode } from 'react'

import { TooltipProvider } from '@/components/ui/tooltip'
import { useAgentStore } from '@/features/agents/store'
import { MESSAGE_FONT_SIZES, useSettingsStore } from '@/features/settings/store'

type AppProvidersProps = {
  children: ReactNode
}

export function AppProviders({ children }: AppProvidersProps) {
  const theme = useSettingsStore((state) => state.theme)
  const messageFontSize = useSettingsStore((state) => state.messageFontSize)
  const hydrateAgents = useAgentStore((state) => state.hydrate)

  useEffect(() => {
    void hydrateAgents()
  }, [hydrateAgents])

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
  }, [theme])

  useEffect(() => {
    document.documentElement.style.setProperty('--co-msg-size', MESSAGE_FONT_SIZES[messageFontSize])
  }, [messageFontSize])

  return <TooltipProvider>{children}</TooltipProvider>
}
