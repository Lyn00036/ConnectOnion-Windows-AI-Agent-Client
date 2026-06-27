/**
 * Try the Electron/SQLite path first; on failure fall back to localStorage ops.
 */

export async function withDesktopFallback<T>(
  useDesktop: boolean,
  desktopOp: () => Promise<T>,
  fallbackOp: () => T | Promise<T>,
): Promise<T> {
  if (!useDesktop) {
    return fallbackOp()
  }

  try {
    return await desktopOp()
  } catch {
    return fallbackOp()
  }
}

export function withDesktopFallbackSync<T>(
  useDesktop: boolean,
  desktopOp: () => T,
  fallbackOp: () => T,
): T {
  if (!useDesktop) {
    return fallbackOp()
  }

  try {
    return desktopOp()
  } catch {
    return fallbackOp()
  }
}
