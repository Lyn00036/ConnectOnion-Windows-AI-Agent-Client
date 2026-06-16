/** Shared HTTP client that applies the backend base URL and parses API errors. */

import { ApiError, isApiErrorPayload } from './errors'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

export async function apiRequest<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  })

  if (!response.ok) {
    const payload = await parseErrorPayload(response)
    throw new ApiError(response.status, payload)
  }

  return (await response.json()) as T
}

async function parseErrorPayload(response: Response) {
  try {
    const payload = (await response.json()) as unknown
    if (isApiErrorPayload(payload)) return payload
  } catch {
    // Fall through to a generic response below.
  }

  return {
    error_code: 'HTTP_ERROR',
    message: `Request failed with ${response.status}`,
    details: {},
  }
}
