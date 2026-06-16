/** Shared API error utilities for backend error responses and unknown failures. */

export type ApiErrorPayload = {
  error_code: string
  message: string
  details?: Record<string, unknown>
}

export class ApiError extends Error {
  status: number
  errorCode: string
  details: Record<string, unknown>

  constructor(status: number, payload: ApiErrorPayload) {
    super(payload.message)
    this.name = 'ApiError'
    this.status = status
    this.errorCode = payload.error_code
    this.details = payload.details ?? {}
  }
}

export function isApiErrorPayload(value: unknown): value is ApiErrorPayload {
  if (!value || typeof value !== 'object') return false

  const payload = value as Record<string, unknown>
  return (
    typeof payload.error_code === 'string' &&
    typeof payload.message === 'string'
  )
}

export function parseUnknownError(error: unknown) {
  if (error instanceof ApiError) {
    return `${error.errorCode}: ${error.message}`
  }

  if (error instanceof Error) {
    return error.message
  }

  return 'Request failed'
}
