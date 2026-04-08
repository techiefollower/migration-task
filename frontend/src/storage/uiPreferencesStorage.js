const PREFIX = 'repo-migration-ui:v1:sidebar:'

/**
 * Desktop drawer width mode per signed-in workspace (localStorage, no DB).
 * 2 = full width, 1 = compact rail — matches AppLayout desktopNavState.
 */
export function loadSidebarWidthState(accountKey) {
  try {
    const raw = localStorage.getItem(PREFIX + (accountKey || 'anonymous'))
    const n = Number(raw)
    if (n === 1 || n === 2) return n
  } catch {
    /* ignore */
  }
  return null
}

export function saveSidebarWidthState(accountKey, state) {
  if (state !== 1 && state !== 2) return
  try {
    localStorage.setItem(PREFIX + (accountKey || 'anonymous'), String(state))
  } catch {
    /* quota */
  }
}
