const PREFIX = 'repo-migration-workspace:v1:'

export function loadWorkspaceDraft(accountKey) {
  try {
    const raw = localStorage.getItem(PREFIX + (accountKey || 'anonymous'))
    if (!raw) return null
    const o = JSON.parse(raw)
    return o && typeof o === 'object' ? o : null
  } catch {
    return null
  }
}

/** Persists non-secret preferences per signed-in user (PATs are never stored). */
export function saveWorkspaceDraft(accountKey, draft) {
  try {
    localStorage.setItem(PREFIX + (accountKey || 'anonymous'), JSON.stringify(draft))
  } catch {
    /* ignore quota / private mode */
  }
}
