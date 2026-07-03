/**
 * Conversation blob persistence (chat UI + SDK session state).
 *
 * Stored in localStorage under `co:agent:{address}:session:{id}`.
 */

export function getConversationStorageKey(address: string, sessionId: string) {
  // Include both agent and session identity so conversations for different
  // agents can reuse the same SDK session id without colliding.
  return `co:agent:${address}:session:${sessionId}`
}

function loadEnvelopeFromLocalStorage(storageKey: string): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

function saveEnvelopeToLocalStorage(storageKey: string, envelopeJson: string) {
  try {
    localStorage.setItem(storageKey, envelopeJson)
  } catch {
    // Keep the live conversation working if localStorage is unavailable or full.
  }
}

export async function readConversationEnvelope(
  address: string,
  sessionId: string,
): Promise<string | null> {
  return loadEnvelopeFromLocalStorage(getConversationStorageKey(address, sessionId))
}

export async function writeConversationEnvelope(
  address: string,
  sessionId: string,
  envelopeJson: string,
) {
  saveEnvelopeToLocalStorage(
    getConversationStorageKey(address, sessionId),
    envelopeJson,
  )
}

export async function removeConversationEnvelope(address: string, sessionId: string) {
  localStorage.removeItem(getConversationStorageKey(address, sessionId))
}
