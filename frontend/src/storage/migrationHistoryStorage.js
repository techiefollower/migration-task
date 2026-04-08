const PREFIX = 'repo-migration-status:v1:'
const MAX_ROWS = 400

/**
 * Migration rows for the signed-in workspace only (localStorage per auth account key).
 * No server database — each browser profile / Entra user has separate data.
 */
export function loadMigrationHistory(accountKey) {
  try {
    const raw = localStorage.getItem(PREFIX + (accountKey || 'anonymous'))
    if (!raw) return []
    const arr = JSON.parse(raw)
    return Array.isArray(arr) ? arr : []
  } catch {
    return []
  }
}

function saveAll(accountKey, rows) {
  try {
    localStorage.setItem(PREFIX + (accountKey || 'anonymous'), JSON.stringify(rows))
  } catch {
    /* quota */
  }
}

/**
 * @param {string} accountKey - from WorkspaceAccountContext (Entra homeAccountId or "local")
 * @param {{
 *   workspaceProject: string,
 *   adoOrg: string,
 *   adoProject: string,
 *   githubOrg: string,
 *   results: { adoRepoName: string, targetRepoName: string, success: boolean, error?: string|null }[]
 * }} run
 */
export function appendMigrationRun(accountKey, run) {
  const prev = loadMigrationHistory(accountKey)
  const completedAt = new Date().toISOString()
  const batchId = `${completedAt}-${Math.random().toString(36).slice(2, 9)}`

  const newRows = (run.results || []).map((r, i) => ({
    id: `${batchId}-${i}`,
    batchId,
    completedAt,
    workspaceProject: run.workspaceProject || '—',
    adoOrg: run.adoOrg || '—',
    adoProject: run.adoProject || '—',
    adoRepo: r.adoRepoName || '—',
    githubOrg: run.githubOrg || '—',
    githubRepo: r.targetRepoName || '—',
    status: r.success ? 'Completed' : 'Failed',
    errorPreview: r.error ? String(r.error).slice(0, 500) : null,
  }))

  const merged = [...newRows, ...prev].slice(0, MAX_ROWS)
  saveAll(accountKey, merged)
  return merged
}

export function clearMigrationHistory(accountKey) {
  try {
    localStorage.removeItem(PREFIX + (accountKey || 'anonymous'))
  } catch {
    /* ignore */
  }
}
