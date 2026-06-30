/** Reachability check for an agent connection target, using upstream endpoints. */

// Upstream-aligned probes (confirmed against connectonion / connectonion-ts source):
// - Direct URL agents are hosted by `host()` which exposes an unauthenticated
//   `GET /health` route and sends `access-control-allow-origin: *`, so a plain
//   fetch from the renderer works without CORS issues.
// - Relay (0x address) agents are probed with the SDK's `fetchAgentInfo()`, which
//   resolves through the relay and reports `online`.
//
// Import from 'connectonion-connect' (the browser-safe connect bundle aliased in
// vite.config.ts), NOT the top-level 'connectonion' entry — the latter loads the
// full Node SDK (dotenv `config()` etc.) and throws "process is not defined" in
// the renderer.

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

function buildHealthUrl(directUrl: string): string {
  const url = new URL(directUrl)
  url.pathname = `${url.pathname.replace(/\/+$/, '')}/health`
  url.search = ''
  url.hash = ''
  return url.toString()
}

function describeError(error: unknown): string {
  if (error instanceof DOMException && error.name === 'TimeoutError') {
    return 'Connection timed out — the agent did not respond in time.'
  }
  return 'Could not reach the agent (network error or invalid URL).'
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
      const response = await fetch(healthUrl, { signal: AbortSignal.timeout(timeoutMs) })
      return response.ok
        ? { ok: true, detail: 'Reachable — agent responded on /health.' }
        : { ok: false, detail: `Agent responded with HTTP ${response.status}.` }
    } catch (error) {
      return { ok: false, detail: describeError(error) }
    }
  }

  const address = target.address?.trim()
  if (address) {
    try {
      const info = await fetchAgentInfo(address)
      if (info.online) {
        return { ok: true, detail: `Online via relay${info.name ? ` · ${info.name}` : ''}.` }
      }
      return { ok: false, detail: 'Registered on the relay but currently offline.' }
    } catch (error) {
      return { ok: false, detail: describeError(error) }
    }
  }

  return { ok: false, detail: 'Enter a Direct URL or agent address first.' }
}
