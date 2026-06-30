import { describe, expect, it, vi } from 'vitest'

import { testAgentConnection } from './connectionTest'

const fetchAgentInfoMock = vi.hoisted(() => vi.fn())

vi.mock('connectonion-connect', () => ({
  fetchAgentInfo: fetchAgentInfoMock,
}))

describe('testAgentConnection', () => {
  it('checks Direct URL agents through /health under the configured base path', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      testAgentConnection({ directUrl: 'http://127.0.0.1:8010/base' }, 50),
    ).resolves.toMatchObject({ ok: true })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://127.0.0.1:8010/base/health',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(fetchAgentInfoMock).not.toHaveBeenCalled()
  })

  it('checks root Direct URL agents through /health', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      testAgentConnection({ directUrl: 'http://127.0.0.1:8010' }, 50),
    ).resolves.toMatchObject({ ok: true })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://127.0.0.1:8010/health',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
  })

  it('reports invalid Direct URLs before attempting network calls', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    await expect(testAgentConnection({ directUrl: 'not a url' })).resolves.toEqual({
      ok: false,
      detail: 'Invalid Direct URL.',
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('reports HTTP failures from direct agent health checks', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 503 }))

    await expect(
      testAgentConnection({ directUrl: 'http://127.0.0.1:8010' }),
    ).resolves.toEqual({
      ok: false,
      detail: 'Agent responded with HTTP 503.',
    })
  })

  it('checks relay agents through the ConnectOnion SDK', async () => {
    fetchAgentInfoMock.mockResolvedValue({ online: true, name: 'Email agent' })

    await expect(testAgentConnection({ address: '0xabc' })).resolves.toMatchObject({
      ok: true,
    })
    expect(fetchAgentInfoMock).toHaveBeenCalledWith('0xabc')
  })

  it('reports offline relay agents', async () => {
    fetchAgentInfoMock.mockResolvedValue({ online: false })

    await expect(testAgentConnection({ address: '0xabc' })).resolves.toEqual({
      ok: false,
      detail: 'Registered on the relay but currently offline.',
    })
  })

  it('requires either a Direct URL or relay address', async () => {
    await expect(testAgentConnection({})).resolves.toEqual({
      ok: false,
      detail: 'Enter a Direct URL or agent address first.',
    })
  })
})
