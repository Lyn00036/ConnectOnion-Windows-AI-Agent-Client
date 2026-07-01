/** Reachability check for an agent connection target, using upstream endpoints. */

// Upstream-aligned probes:
// - Direct URL agents expose an unauthenticated GET /health route.
// - Relay agents are probed with the SDK's fetchAgentInfo(), which resolves
//   through the relay and reports online status and metadata.
//
// Import from 'connectonion-connect' (the browser-safe connect bundle aliased in
// vite.config.ts), not the top-level 'connectonion' entry; the latter loads the
// full Node SDK and throws in the renderer.

import { fetchAgentInfo } from 'connectonion-connect'

export type ConnectionTestResult = {
  ok: boolean
  detail: string
}

export type ConnectionTestTarget = {
  address?: string
  directUrl?: string
}

const DEFAULT_TIMEOUT_MS = 6000
const ADDRESS_PATTERN = /^[a-zA-Z0-9]+$/

function buildHealthUrl(directUrl: string): string {
  const url = new URL(directUrl)
  url.pathname = `${url.pathname.replace(/\/+$/, '')}/health`
  url.search = ''
  url.hash = ''
  return url.toString()
}

function describeError(error: unknown): string {
  if (error instanceof DOMException && error.name === 'TimeoutError') {
    return 'Connection timed out - the agent did not respond in time.'
  }
  return 'Could not reach the agent (network error or invalid URL).'
}

function readStringField(
  record: Record<string, unknown>,
  fieldNames: string[],
): string | null {
  for (const fieldName of fieldNames) {
    const value = record[fieldName]
    if (typeof value === 'string' && value.trim()) {
      return value.trim()
    }
  }
  return null
}

function extractAgentName(value: unknown): string | null {
  if (typeof value === 'string') {
    return value.trim() || null
  }

  if (!value || typeof value !== 'object') {
    return null
  }

  const record = value as Record<string, unknown>
  return (
    readStringField(record, ['name', 'agentName', 'agent_name']) ??
    extractAgentName(record.agent)
  )
}

async function readHealthAgentName(response: Response): Promise<string | null> {
  const json = (response as { json?: () => Promise<unknown> }).json
  if (!json) return null

  try {
    return extractAgentName(await json.call(response))
  } catch {
    return null
  }
}

export async function testAgentConnection(
  target: ConnectionTestTarget,
  timeoutMs = DEFAULT_TIMEOUT_MS,
): Promise<ConnectionTestResult> {
  const directUrl = target.directUrl?.trim()

  if (directUrl) {
    let healthUrl: string
    try {
      healthUrl = buildHealthUrl(directUrl)
    } catch {
      return { ok: false, detail: 'Invalid Direct URL.' }
    }

    try {
      const response = await fetch(healthUrl, {
        signal: AbortSignal.timeout(timeoutMs),
      })
      if (!response.ok) {
        return { ok: false, detail: `Agent responded with HTTP ${response.status}.` }
      }

      const agentName = await readHealthAgentName(response)
      return {
        ok: true,
        detail: `Reachable via Direct URL${agentName ? ` - ${agentName}` : ''}.`,
      }
    } catch (error) {
      return { ok: false, detail: describeError(error) }
    }
  }

  const address = target.address?.trim()
  if (address) {
    if (!ADDRESS_PATTERN.test(address)) {
      return {
        ok: false,
        detail: 'Agent address can only contain letters and numbers.',
      }
    }

    try {
      const info = await fetchAgentInfo(address)
      if (info.online) {
        return { ok: true, detail: `Online via relay${info.name ? ` - ${info.name}` : ''}.` }
      }
      return { ok: false, detail: 'Registered on the relay but currently offline.' }
    } catch (error) {
      return { ok: false, detail: describeError(error) }
    }
  }

  return { ok: false, detail: 'Enter a Direct URL or agent address first.' }
}
