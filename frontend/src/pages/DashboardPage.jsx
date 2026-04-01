import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Alert,
  Backdrop,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Grid,
  LinearProgress,
  Link,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { api } from '../api/client'

function statusChip(status) {
  const s = (status || '').toLowerCase()
  const map = {
    pending: { color: 'default', label: 'Pending', tip: 'Waiting for the API to begin this migration.' },
    inprogress: { color: 'info', label: 'In progress', tip: 'gh ado2gh is running on the API host.' },
    completed: { color: 'success', label: 'Completed', tip: 'ADO → GitHub migration finished (ado2gh).' },
    failed: { color: 'error', label: 'Failed', tip: 'See logs for details. You can retry with fresh PATs.' },
  }
  const cfg = map[s] || { color: 'default', label: status, tip: '' }
  return (
    <Tooltip title={cfg.tip} arrow>
      <Chip size="small" color={cfg.color} label={cfg.label} sx={{ fontWeight: 600 }} />
    </Tooltip>
  )
}

function parseGithubOwner(targetUrl) {
  try {
    const u = new URL(targetUrl)
    const seg = u.pathname.replace(/^\//, '').replace(/\.git$/i, '').split('/').filter(Boolean)
    return seg[0] || ''
  } catch {
    return ''
  }
}

function githubRepoWebUrl(targetUrl) {
  try {
    const u = new URL(targetUrl)
    const path = u.pathname.replace(/\.git$/i, '')
    return `https://github.com${path}`
  } catch {
    return null
  }
}

function StatCard({ title, value, caption, color = 'primary.main' }) {
  return (
    <Card
      elevation={0}
      sx={{
        height: '100%',
        border: '1px solid rgba(255,255,255,0.08)',
        background: 'linear-gradient(145deg, rgba(17,26,46,0.95) 0%, rgba(11,18,32,0.98) 100%)',
      }}
    >
      <CardContent sx={{ py: 2 }}>
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 1 }}>
          {title}
        </Typography>
        <Typography variant="h4" sx={{ color, fontWeight: 800, lineHeight: 1.1 }}>
          {value}
        </Typography>
        {caption ? (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
            {caption}
          </Typography>
        ) : null}
      </CardContent>
    </Card>
  )
}

export default function DashboardPage() {
  const [rows, setRows] = useState([])
  const [summary, setSummary] = useState(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [lastUpdated, setLastUpdated] = useState(null)

  const [logOpen, setLogOpen] = useState(false)
  const [logRow, setLogRow] = useState(null)

  const [retryOpen, setRetryOpen] = useState(false)
  const [retryRow, setRetryRow] = useState(null)
  const [retryAdoPat, setRetryAdoPat] = useState('')
  const [retryGhPat, setRetryGhPat] = useState('')
  const [retryOwner, setRetryOwner] = useState('')
  const [retryBusy, setRetryBusy] = useState(false)

  const [snackbar, setSnackbar] = useState({ open: false, message: '', severity: 'success' })

  const load = useCallback(async ({ isManual = false, initial = false } = {}) => {
    if (isManual) setRefreshing(true)
    if (initial) setLoading(true)
    setError('')
    try {
      const [listRes, sumRes] = await Promise.all([api.get('/migrations'), api.get('/migrations/summary')])
      setRows(listRes.data || [])
      setSummary(sumRes.data || null)
      setLastUpdated(new Date())
    } catch (e) {
      setError(e?.message || 'Failed to load migrations')
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    load({ initial: true })
    const id = setInterval(() => load({}), 5000)
    return () => clearInterval(id)
  }, [load])

  const openLogs = (row) => {
    setLogRow(row)
    setLogOpen(true)
  }

  const openRetry = (row) => {
    setRetryRow(row)
    setRetryOwner(parseGithubOwner(row.targetUrl))
    setRetryAdoPat('')
    setRetryGhPat('')
    setRetryOpen(true)
  }

  const submitRetry = async () => {
    if (!retryRow) return
    setRetryBusy(true)
    setError('')
    try {
      await api.post(`/migrations/${retryRow.id}/retry`, {
        adoPersonalAccessToken: retryAdoPat,
        githubPersonalAccessToken: retryGhPat,
        githubOwner: retryOwner.trim(),
      })
      setRetryOpen(false)
      setSnackbar({ open: true, message: 'Migration re-queued.', severity: 'success' })
      await load({})
    } catch (e) {
      setError(e?.response?.data?.error || e?.message || 'Retry failed')
    } finally {
      setRetryBusy(false)
    }
  }

  const headerSubtitle = useMemo(() => {
    if (!lastUpdated) return 'Status refreshes every 5 seconds.'
    return `Last updated ${lastUpdated.toLocaleTimeString()} · Auto-refresh every 5s`
  }, [lastUpdated])

  return (
    <Box sx={{ maxWidth: 1280, mx: 'auto' }}>
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'flex-start',
          justifyContent: 'space-between',
          gap: 2,
          mb: 2,
        }}
      >
        <Box>
          <Typography variant="h4" gutterBottom>
            Dashboard
          </Typography>
          <Typography variant="body1" color="text.secondary">
            {headerSubtitle}. Failed jobs can be re-queued with fresh PATs.
          </Typography>
        </Box>
        <Tooltip title="Fetch the latest statuses immediately (auto-refresh still runs).">
          <Button
            variant="outlined"
            onClick={() => load({ isManual: true })}
            disabled={refreshing || loading}
            startIcon={refreshing ? <CircularProgress size={16} thickness={5} /> : null}
          >
            Refresh now
          </Button>
        </Tooltip>
      </Box>

      {summary ? (
        <Grid container spacing={2} sx={{ mb: 3 }}>
          <Grid item xs={6} sm={4} md={2.4}>
            <StatCard title="Total" value={summary.total} caption="All migration rows" color="text.primary" />
          </Grid>
          <Grid item xs={6} sm={4} md={2.4}>
            <StatCard title="Pending" value={summary.pending} caption="Queued" color="grey.400" />
          </Grid>
          <Grid item xs={6} sm={4} md={2.4}>
            <StatCard title="In progress" value={summary.inProgress} caption="Running" color="info.light" />
          </Grid>
          <Grid item xs={6} sm={4} md={2.4}>
            <StatCard title="Completed" value={summary.completed} caption="Success" color="success.light" />
          </Grid>
          <Grid item xs={6} sm={4} md={2.4}>
            <StatCard title="Failed" value={summary.failed} caption="Needs attention" color="error.light" />
          </Grid>
        </Grid>
      ) : null}

      {error ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError('')}>
          {error}
        </Alert>
      ) : null}

      <Card
        elevation={0}
        sx={{
          border: '1px solid rgba(255,255,255,0.08)',
          overflow: 'hidden',
        }}
      >
        <CardContent sx={{ p: 0 }}>
          {loading && !rows.length ? (
            <Box sx={{ p: 6, textAlign: 'center' }}>
              <CircularProgress size={40} />
              <Typography color="text.secondary" sx={{ mt: 2 }}>
                Loading migrations…
              </Typography>
            </Box>
          ) : (
            <TableContainer sx={{ maxHeight: { xs: 'none', md: 'min(70vh, 640px)' } }}>
              <Table size="small" stickyHeader>
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ fontWeight: 700, bgcolor: 'background.paper' }}>Target</TableCell>
                    <TableCell sx={{ fontWeight: 700, bgcolor: 'background.paper' }}>Source</TableCell>
                    <TableCell sx={{ fontWeight: 700, bgcolor: 'background.paper' }}>Status</TableCell>
                    <TableCell sx={{ fontWeight: 700, bgcolor: 'background.paper', minWidth: 200 }}>Logs</TableCell>
                    <TableCell sx={{ fontWeight: 700, bgcolor: 'background.paper' }}>Updated</TableCell>
                    <TableCell align="right" sx={{ fontWeight: 700, bgcolor: 'background.paper' }}>
                      Actions
                    </TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {rows.map((r, idx) => (
                    <TableRow
                      key={r.id}
                      hover
                      sx={{
                        bgcolor: idx % 2 === 1 ? 'rgba(255,255,255,0.02)' : 'transparent',
                      }}
                    >
                      <TableCell sx={{ maxWidth: 280, verticalAlign: 'top' }}>
                        <Typography fontWeight={700}>{r.repoName}</Typography>
                        <Tooltip title={r.targetUrl}>
                          <Typography
                            variant="caption"
                            color="text.secondary"
                            sx={{ wordBreak: 'break-all', display: 'block', cursor: 'default' }}
                          >
                            {r.targetUrl}
                          </Typography>
                        </Tooltip>
                      </TableCell>
                      <TableCell sx={{ maxWidth: 300, verticalAlign: 'top' }}>
                        <Tooltip title={r.sourceUrl}>
                          <Typography variant="body2" color="text.secondary" sx={{ wordBreak: 'break-all' }}>
                            {r.sourceUrl}
                          </Typography>
                        </Tooltip>
                      </TableCell>
                      <TableCell sx={{ verticalAlign: 'top' }}>{statusChip(r.status)}</TableCell>
                      <TableCell sx={{ verticalAlign: 'top', maxWidth: 280 }}>
                        {r.logs ? (
                          <Box>
                            <Typography
                              variant="body2"
                              color="text.secondary"
                              sx={{
                                display: '-webkit-box',
                                WebkitLineClamp: 4,
                                WebkitBoxOrient: 'vertical',
                                overflow: 'hidden',
                                whiteSpace: 'pre-wrap',
                                wordBreak: 'break-word',
                                fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
                                fontSize: 12,
                                lineHeight: 1.45,
                              }}
                            >
                              {r.logs}
                            </Typography>
                            <Tooltip title="Open full log in a dialog (easier to read and copy).">
                              <Button size="small" sx={{ mt: 0.5, p: 0, minWidth: 'auto' }} onClick={() => openLogs(r)}>
                                View full log
                              </Button>
                            </Tooltip>
                          </Box>
                        ) : (
                          <Typography variant="body2" color="text.disabled">
                            —
                          </Typography>
                        )}
                      </TableCell>
                      <TableCell sx={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
                        {new Date(r.updatedAt).toLocaleString()}
                      </TableCell>
                      <TableCell align="right" sx={{ verticalAlign: 'top' }}>
                        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 0.5 }}>
                          {String(r.status).toLowerCase() === 'failed' ? (
                            <Tooltip title="Submit PATs again and re-queue this migration.">
                              <Button size="small" variant="outlined" color="warning" onClick={() => openRetry(r)}>
                                Retry
                              </Button>
                            </Tooltip>
                          ) : null}
                          {String(r.status).toLowerCase() === 'completed' ? (
                            <Tooltip title="Open this repository on GitHub in a new tab.">
                              <Button
                                size="small"
                                component={Link}
                                href={githubRepoWebUrl(r.targetUrl) || '#'}
                                target="_blank"
                                rel="noopener noreferrer"
                                disabled={!githubRepoWebUrl(r.targetUrl)}
                              >
                                Open on GitHub
                              </Button>
                            </Tooltip>
                          ) : null}
                          {String(r.status).toLowerCase() !== 'failed' &&
                          String(r.status).toLowerCase() !== 'completed' ? (
                            <Typography variant="caption" color="text.secondary">
                              —
                            </Typography>
                          ) : null}
                        </Box>
                      </TableCell>
                    </TableRow>
                  ))}
                  {!rows.length ? (
                    <TableRow>
                      <TableCell colSpan={6}>
                        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
                          No migrations yet. Start the wizard to queue one.
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ) : null}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </CardContent>
      </Card>

      <Dialog
        open={logOpen}
        onClose={() => setLogOpen(false)}
        maxWidth="md"
        fullWidth
        PaperProps={{ sx: { borderRadius: 2 } }}
      >
        <DialogTitle>
          Migration log
          {logRow ? (
            <Typography variant="body2" color="text.secondary" display="block" fontWeight={400}>
              {logRow.repoName}
            </Typography>
          ) : null}
        </DialogTitle>
        <DialogContent dividers>
          <Box
            component="pre"
            sx={{
              m: 0,
              p: 2,
              bgcolor: 'rgba(0,0,0,0.45)',
              borderRadius: 2,
              overflow: 'auto',
              maxHeight: '60vh',
              fontSize: 12,
              fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}
          >
            {logRow?.logs || 'No log lines yet.'}
          </Box>
        </DialogContent>
        <DialogActions sx={{ px: 3, py: 2 }}>
          <Button onClick={() => setLogOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={retryOpen} onClose={() => !retryBusy && setRetryOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Retry migration</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            Provide PATs again. They are not stored on the server after the job runs.
          </Typography>
          <Tooltip title="GitHub user or organization that owns the target repository.">
            <TextField
              label="GitHub owner"
              value={retryOwner}
              onChange={(e) => setRetryOwner(e.target.value)}
              fullWidth
            />
          </Tooltip>
          <TextField
            label="Azure DevOps PAT"
            type="password"
            value={retryAdoPat}
            onChange={(e) => setRetryAdoPat(e.target.value)}
            fullWidth
          />
          <TextField
            label="GitHub PAT"
            type="password"
            value={retryGhPat}
            onChange={(e) => setRetryGhPat(e.target.value)}
            fullWidth
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Tooltip title="Close without re-queueing.">
            <span>
              <Button onClick={() => setRetryOpen(false)} disabled={retryBusy}>
                Cancel
              </Button>
            </span>
          </Tooltip>
          <Tooltip title="Requires all fields. Re-runs the migration on the API server.">
            <span>
              <Button
                onClick={submitRetry}
                disabled={retryBusy || !retryAdoPat || !retryGhPat || !retryOwner.trim()}
                variant="contained"
                startIcon={retryBusy ? <CircularProgress color="inherit" size={18} thickness={5} /> : null}
              >
                Re-queue
              </Button>
            </span>
          </Tooltip>
        </DialogActions>
      </Dialog>

      <Backdrop open={retryBusy} sx={{ color: '#fff', zIndex: (t) => t.zIndex.modal + 1 }}>
        <CircularProgress color="inherit" />
      </Backdrop>

      {refreshing && rows.length > 0 ? (
        <LinearProgress sx={{ position: 'fixed', top: 0, left: 0, right: 0, zIndex: (t) => t.zIndex.drawer + 2 }} />
      ) : null}

      <Snackbar
        open={snackbar.open}
        autoHideDuration={4000}
        onClose={() => setSnackbar((s) => ({ ...s, open: false }))}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          onClose={() => setSnackbar((s) => ({ ...s, open: false }))}
          severity={snackbar.severity}
          variant="filled"
          sx={{ width: '100%' }}
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Box>
  )
}
