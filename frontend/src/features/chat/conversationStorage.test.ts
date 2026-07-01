import { describe, expect, it } from 'vitest'

import {
  getConversationStorageKey,
  readConversationEnvelope,
  removeConversationEnvelope,
  writeConversationEnvelope,
} from './conversationStorage'

describe('conversationStorage', () => {
  it('builds stable conversation storage keys', () => {
    expect(getConversationStorageKey('agent-1', 'session-1')).toBe(
      'co:agent:agent-1:session:session-1',
    )
  })

  it('reads, writes, and removes envelopes from localStorage', async () => {
    await writeConversationEnvelope('agent-1', 'session-1', '{"ui":["hello"]}')

    await expect(readConversationEnvelope('agent-1', 'session-1')).resolves.toBe(
      '{"ui":["hello"]}',
    )

    await removeConversationEnvelope('agent-1', 'session-1')

    expect(
      localStorage.getItem('co:agent:agent-1:session:session-1'),
    ).toBeNull()
  })
})
