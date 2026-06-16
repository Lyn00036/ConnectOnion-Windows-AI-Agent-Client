/** Shared URL helpers for normalizing user-provided agent endpoints. */

export function normalizeUrl(url: string) {
  return url.trim().replace(/\/+$/, '')
}
