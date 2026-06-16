/** API functions for sending chat messages through the backend agent proxy. */

import { apiRequest } from '../../shared/api/client'
import type { ChatRequest, ChatResponse } from './types'

export function sendChatMessage(payload: ChatRequest) {
  return apiRequest<ChatResponse>('/agents/chat', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}
