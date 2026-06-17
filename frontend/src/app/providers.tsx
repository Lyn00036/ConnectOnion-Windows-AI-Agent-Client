/** Application-level providers for routing-independent React infrastructure. */

import type { ReactNode } from 'react'

type AppProvidersProps = {
  children: ReactNode
}

export function AppProviders({ children }: AppProvidersProps) {
  return <>{children}</>
}
