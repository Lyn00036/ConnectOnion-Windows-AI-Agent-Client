import { describe, expect, it, vi } from 'vitest'

import { withDesktopFallback, withDesktopFallbackSync } from './desktopFallback'

describe('withDesktopFallback', () => {
  it('uses the fallback operation when desktop storage is unavailable', async () => {
    const desktopOp = vi.fn().mockResolvedValue('desktop')
    const fallbackOp = vi.fn().mockReturnValue('fallback')

    await expect(withDesktopFallback(false, desktopOp, fallbackOp)).resolves.toBe(
      'fallback',
    )
    expect(desktopOp).not.toHaveBeenCalled()
    expect(fallbackOp).toHaveBeenCalledOnce()
  })

  it('prefers the desktop operation when it succeeds', async () => {
    const desktopOp = vi.fn().mockResolvedValue('desktop')
    const fallbackOp = vi.fn().mockReturnValue('fallback')

    await expect(withDesktopFallback(true, desktopOp, fallbackOp)).resolves.toBe(
      'desktop',
    )
    expect(desktopOp).toHaveBeenCalledOnce()
    expect(fallbackOp).not.toHaveBeenCalled()
  })

  it('falls back when the desktop operation rejects', async () => {
    const desktopOp = vi.fn().mockRejectedValue(new Error('sqlite down'))
    const fallbackOp = vi.fn().mockResolvedValue('fallback')

    await expect(withDesktopFallback(true, desktopOp, fallbackOp)).resolves.toBe(
      'fallback',
    )
    expect(fallbackOp).toHaveBeenCalledOnce()
  })
})

describe('withDesktopFallbackSync', () => {
  it('falls back when the synchronous desktop operation throws', () => {
    const desktopOp = vi.fn(() => {
      throw new Error('bridge unavailable')
    })
    const fallbackOp = vi.fn(() => 'fallback')

    expect(withDesktopFallbackSync(true, desktopOp, fallbackOp)).toBe('fallback')
    expect(fallbackOp).toHaveBeenCalledOnce()
  })
})
