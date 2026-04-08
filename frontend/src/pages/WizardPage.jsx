import { useMemo, useState, useCallback, useEffect } from 'react'
import {
  Alert,
  Backdrop,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  Divider,
  FormControl,
  InputLabel,
  LinearProgress,
  MenuItem,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import RocketLaunchRoundedIcon from '@mui/icons-material/RocketLaunchRounded'
import { api } from '../api/client'
import { useWorkspaceAccountKey } from '../context/WorkspaceAccountContext'
import { loadWorkspaceDraft, saveWorkspaceDraft } from '../storage/workspaceStorage'
import { appendMigrationRun } from '../storage/migrationHistoryStorage'
import { isAzureAuthConfigured } from '../auth/authConfig'

const LOADING = { NONE: null, ADO_PROJECTS: 'adoProjects', ADO_REPOS: 'adoRepos' }

const EXECUTE_TIMEOUT_MS = 3_600_000

function sanitizeTargetName(name) {
  const s = name
    .trim()
    .replace(/[^a-zA-Z0-9._-]/g, '-')
    .replace(/-+/g, '-')
    .replace(/^[-.]+|[-.]+$/g, '')
  return s.slice(0, 100) || 'repo'
}

function Btn({ loadingKey, actionKey, children, disabled, onClick, ...props }) {
  const anyLoading = loadingKey != null && loadingKey !== LOADING.NONE
  const thisBusy = actionKey != null && loadingKey === actionKey
  return (
    <Button
      {...props}
      onClick={onClick}
      disabled={Boolean(disabled) || anyLoading}
      startIcon={thisBusy ? <CircularProgress color="inherit" size={18} thickness={5} /> : null}
    >
      {children}
    </Button>
  )
}

export default function WizardPage() {
  const [loadingKey, setLoadingKey] = useState(LOADING.NONE)
  const [migrating, setMigrating] = useState(false)

  const [adoOrg, setAdoOrg] = useState('')
  const [adoPat, setAdoPat] = useState('')
  const [projects, setProjects] = useState([])
  const [project, setProject] = useState(null)
  const [repos, setRepos] = useState([])
  const [selected, setSelected] = useState(() => new Set())
  const [targetNames, setTargetNames] = useState({})

  const [githubPat, setGithubPat] = useState('')
  const [githubOwner, setGithubOwner] = useState('')
  const [targetRepoVisibility, setTargetRepoVisibility] = useState('private')
  const [error, setError] = useState('')

  const [resultOpen, setResultOpen] = useState(false)
  const [resultPayload, setResultPayload] = useState(null)

  const workspaceKey = useWorkspaceAccountKey()

  useEffect(() => {
    const d = loadWorkspaceDraft(workspaceKey)
    if (!d) return
    if (typeof d.adoOrg === 'string') setAdoOrg(d.adoOrg)
    if (typeof d.githubOwner === 'string') setGithubOwner(d.githubOwner)
    if (typeof d.targetRepoVisibility === 'string') setTargetRepoVisibility(d.targetRepoVisibility)
  }, [workspaceKey])

  useEffect(() => {
    const t = setTimeout(() => {
      saveWorkspaceDraft(workspaceKey, { adoOrg, githubOwner, targetRepoVisibility })
    }, 500)
    return () => clearTimeout(t)
  }, [workspaceKey, adoOrg, githubOwner, targetRepoVisibility])

  const isLoading = loadingKey != null && loadingKey !== LOADING.NONE

  const runAction = useCallback(async (key, fn) => {
    setLoadingKey(key)
    setError('')
    try {
      await fn()
    } catch (e) {
      const msg = e?.response?.data?.error || e?.message || 'Request failed'
      setError(typeof msg === 'string' ? msg : 'Request failed')
    } finally {
      setLoadingKey(LOADING.NONE)
    }
  }, [])

  const loadProjects = () =>
    runAction(LOADING.ADO_PROJECTS, async () => {
      const { data } = await api.post('/ado/projects', {
        organization: adoOrg.trim(),
        personalAccessToken: adoPat,
      })
      if (!data.valid) {
        setError(data.error || 'Could not validate Azure DevOps credentials.')
        return
      }
      setProjects(data.projects || [])
      setProject(null)
      setRepos([])
      setSelected(new Set())
      setTargetNames({})
    })

  const loadRepos = () =>
    runAction(LOADING.ADO_REPOS, async () => {
      if (!project) {
        setError('Select a project.')
        return
      }
      const { data } = await api.post('/ado/repositories', {
        organization: adoOrg.trim(),
        projectIdOrName: project.id,
        personalAccessToken: adoPat,
      })
      if (!data.valid) {
        setError(data.error || 'Could not load repositories.')
        return
      }
      const list = data.repositories || []
      setRepos(list)
      const nextTargets = {}
      for (const r of list) nextTargets[r.id] = sanitizeTargetName(r.name)
      setTargetNames(nextTargets)
      setSelected(new Set())
    })

  const toggleRepo = (id) => {
    setSelected((prev) => {
      const n = new Set(prev)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })
  }

  const updateTargetName = (id, value) => {
    setTargetNames((prev) => ({ ...prev, [id]: value }))
  }

  const selectedRepos = useMemo(() => repos.filter((r) => selected.has(r.id)), [repos, selected])

  const canMigrate = useMemo(() => {
    if (!project || selectedRepos.length === 0) return false
    if (!adoPat || !githubPat.trim() || !githubOwner.trim()) return false
    const nameRe = /^[a-zA-Z0-9._-]{1,100}$/
    const names = selectedRepos.map((r) => (targetNames[r.id] || '').trim())
    if (names.some((n) => !n || !nameRe.test(n))) return false
    const lower = names.map((n) => n.toLowerCase())
    if (lower.length !== new Set(lower).size) return false
    return true
  }, [project, selectedRepos, adoPat, githubPat, githubOwner, targetNames])

  const migrateBlockReason = useMemo(() => {
    if (!project) return 'Pick a project and load repositories.'
    if (selectedRepos.length === 0) return 'Select at least one repository.'
    if (!adoPat.trim()) return 'Azure DevOps PAT is required.'
    if (!githubPat.trim()) return 'GitHub PAT is required.'
    if (!githubOwner.trim()) return 'GitHub organization is required.'
    const nameRe = /^[a-zA-Z0-9._-]{1,100}$/
    for (const r of selectedRepos) {
      const n = (targetNames[r.id] || '').trim()
      if (!n || !nameRe.test(n)) return 'Each selected repo needs a valid GitHub name (letters, numbers, . _ -).'
    }
    const lower = selectedRepos.map((r) => (targetNames[r.id] || '').trim().toLowerCase())
    if (lower.length !== new Set(lower).size) return 'Target names must be unique.'
    return null
  }, [project, selectedRepos, adoPat, githubPat, githubOwner, targetNames])

  const executeMigrate = async () => {
    setMigrating(true)
    setError('')
    try {
      const { data } = await api.post(
        '/migrations/execute',
        {
          adoPersonalAccessToken: adoPat,
          githubPersonalAccessToken: githubPat,
          githubOwner: githubOwner.trim(),
          targetRepoVisibility,
          repositories: selectedRepos.map((r) => ({
            sourceRemoteUrl: r.remoteUrl,
            targetRepoName: (targetNames[r.id] || '').trim(),
            adoRepoName: r.name,
          })),
        },
        { timeout: EXECUTE_TIMEOUT_MS },
      )
      appendMigrationRun(workspaceKey, {
        workspaceProject: project?.name || '—',
        adoOrg: adoOrg.trim(),
        adoProject: project?.name || '—',
        githubOrg: githubOwner.trim(),
        results: data.results || [],
      })
      setResultPayload(data)
      setResultOpen(true)
    } catch (e) {
      const msg = e?.response?.data?.error || e?.message || 'Migration request failed'
      setError(typeof msg === 'string' ? msg : 'Migration request failed')
    } finally {
      setMigrating(false)
    }
  }

  const closeResult = () => {
    setResultOpen(false)
    setResultPayload(null)
  }

  const successList = resultPayload?.results?.filter((r) => r.success) || []
  const failList = resultPayload?.results?.filter((r) => !r.success) || []
  const allOk = Boolean(resultPayload?.allSucceeded)

  return (
    <Box sx={{ maxWidth: 1100, mx: 'auto' }}>
      <Typography variant="h4" gutterBottom fontWeight={800} letterSpacing={-0.5}>
        Migrate repositories
      </Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mb: 3, maxWidth: 720 }}>
        Connect to Azure DevOps, choose repositories and GitHub names, then run migration. Validation and{' '}
        <code>gh ado2gh migrate-repo</code> run on the server when you click <strong>Migrate</strong> — large repos can take
        several minutes each.
        {isAzureAuthConfigured() ? (
          <>
            {' '}
            Your saved organization and GitHub owner (not PATs) stay in this browser for your signed-in account only.
          </>
        ) : null}
      </Typography>

      {error ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError('')}>
          {error}
        </Alert>
      ) : null}

      <Stack spacing={3}>
        <Card elevation={0} sx={{ border: '1px solid rgba(255,255,255,0.08)', borderRadius: 2 }}>
          <CardContent sx={{ p: { xs: 2, md: 3 } }}>
            <Typography variant="overline" color="primary" sx={{ letterSpacing: 1.2 }}>
              Azure DevOps
            </Typography>
            <Typography variant="h6" sx={{ mb: 2, fontWeight: 700 }}>
              Organization &amp; access
            </Typography>
            <Stack direction={{ xs: 'column', sm: 'col' }} spacing={2} sx={{ mb: 2, maxWidth: 900 }}>
              <TextField
                label="Organization"
                placeholder="e.g. contoso"
                value={adoOrg}
                onChange={(e) => setAdoOrg(e.target.value)}
                fullWidth
                helperText="Slug from https://dev.azure.com/{organization}"
              />
              <TextField
                label="Azure DevOps PAT"
                type="password"
                name="ado-pat-user-entry"
                autoComplete="new-password"
                value={adoPat}
                onChange={(e) => setAdoPat(e.target.value)}
                fullWidth
                helperText="Code (Read), Project and team (Read). Never stored on the server."
              />
            </Stack>
            <Btn
              loadingKey={loadingKey}
              actionKey={LOADING.ADO_PROJECTS}
              variant="contained"
              onClick={loadProjects}
              disabled={!adoOrg.trim() || !adoPat}
            >
              Load projects
            </Btn>

            {projects.length > 0 ? (
              <>
                <Divider sx={{ my: 3, borderColor: 'rgba(255,255,255,0.08)' }} />
                <Typography variant="subtitle1" gutterBottom fontWeight={700}>
                  Projects in <strong>{adoOrg}</strong>
                </Typography>
                <TableContainer sx={{ mb: 2 }}>
                  <Table size="small">
                    <TableHead>
                      <TableRow sx={{ bgcolor: 'rgba(255,255,255,0.04)' }}>
                        <TableCell>Name</TableCell>
                        <TableCell>Id</TableCell>
                        <TableCell align="right">Use</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {projects.map((p) => (
                        <TableRow
                          key={p.id}
                          hover
                          selected={project?.id === p.id}
                          sx={{ cursor: 'pointer' }}
                          onClick={() => setProject(p)}
                        >
                          <TableCell>{p.name}</TableCell>
                          <TableCell sx={{ color: 'text.secondary', fontFamily: 'monospace', fontSize: 12 }}>{p.id}</TableCell>
                          <TableCell align="right">
                            <Button
                              size="small"
                              variant={project?.id === p.id ? 'contained' : 'outlined'}
                              onClick={(e) => {
                                e.stopPropagation()
                                setProject(p)
                              }}
                            >
                              Select
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
                <Btn
                  loadingKey={loadingKey}
                  actionKey={LOADING.ADO_REPOS}
                  variant="contained"
                  color="secondary"
                  onClick={loadRepos}
                  disabled={!project}
                >
                  Load repositories
                </Btn>
              </>
            ) : null}
          </CardContent>
        </Card>

        {repos.length > 0 ? (
          <Card elevation={0} sx={{ border: '1px solid rgba(255,255,255,0.08)', borderRadius: 2 }}>
            <CardContent sx={{ p: { xs: 2, md: 3 } }}>
              <Typography variant="overline" color="primary" sx={{ letterSpacing: 1.2 }}>
                Repositories
              </Typography>
              <Typography variant="h6" sx={{ mb: 0.5, fontWeight: 700 }}>
                Select &amp; rename for GitHub
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Project: <strong>{project?.name}</strong>
              </Typography>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow sx={{ bgcolor: 'rgba(255,255,255,0.04)' }}>
                      <TableCell padding="checkbox" width={48} />
                      <TableCell>ADO repo</TableCell>
                      <TableCell>GitHub repo name</TableCell>
                      <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Clone URL</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {repos.map((r) => {
                      const checked = selected.has(r.id)
                      const tn = targetNames[r.id] || ''
                      const invalid = Boolean(tn) && !/^[a-zA-Z0-9._-]{1,100}$/.test(tn.trim())
                      return (
                        <TableRow key={r.id} hover selected={checked}>
                          <TableCell padding="checkbox">
                            <Checkbox checked={checked} onChange={() => toggleRepo(r.id)} />
                          </TableCell>
                          <TableCell sx={{ fontWeight: 700 }}>{r.name}</TableCell>
                          <TableCell sx={{ minWidth: 200 }}>
                            <TextField
                              size="small"
                              fullWidth
                              value={tn}
                              onChange={(e) => updateTargetName(r.id, e.target.value)}
                              disabled={!checked}
                              placeholder="target-repo-name"
                              error={invalid}
                              helperText={invalid ? 'Use letters, numbers, . _ -' : ' '}
                              FormHelperTextProps={{ sx: { m: 0, minHeight: 0 } }}
                            />
                            {checked ? (
                              <Button size="small" sx={{ mt: 0.5 }} onClick={() => updateTargetName(r.id, sanitizeTargetName(r.name))}>
                                Reset from ADO name
                              </Button>
                            ) : null}
                          </TableCell>
                          <TableCell
                            sx={{
                              display: { xs: 'none', md: 'table-cell' },
                              wordBreak: 'break-all',
                              color: 'text.secondary',
                              fontSize: 12,
                            }}
                          >
                            {r.remoteUrl}
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              </TableContainer>
            </CardContent>
          </Card>
        ) : null}

        {repos.length > 0 ? (
          <Card elevation={0} sx={{ border: '1px solid rgba(255,255,255,0.08)', borderRadius: 2 }}>
            <CardContent sx={{ p: { xs: 2, md: 3 } }}>
              <Typography variant="overline" color="primary" sx={{ letterSpacing: 1.2 }}>
                GitHub
              </Typography>
              <Typography variant="h6" sx={{ mb: 2, fontWeight: 700 }}>
                Destination organization
              </Typography>
              <Stack spacing={2} sx={{ maxWidth: 560 }}>
                <TextField
                  label="GitHub PAT"
                  type="password"
                  name="github-pat-user-entry"
                  autoComplete="new-password"
                  value={githubPat}
                  onChange={(e) => setGithubPat(e.target.value)}
                  fullWidth
                  helperText="Needs permission to create repositories under the organization. Never stored on the server."
                />
                <TextField
                  label="GitHub organization"
                  placeholder="e.g. my-company-org"
                  value={githubOwner}
                  onChange={(e) => setGithubOwner(e.target.value)}
                  fullWidth
                  helperText="Exact org login (URL slug). If the org uses SAML SSO, authorize your PAT for that org in GitHub settings."
                />
                <FormControl size="small" sx={{ maxWidth: 360 }}>
                  <InputLabel id="vis-label">New repo visibility</InputLabel>
                  <Select
                    labelId="vis-label"
                    label="New repo visibility"
                    value={targetRepoVisibility}
                    onChange={(e) => setTargetRepoVisibility(e.target.value)}
                  >
                    <MenuItem value="private">private</MenuItem>
                    <MenuItem value="internal">internal (Enterprise)</MenuItem>
                    <MenuItem value="public">public</MenuItem>
                  </Select>
                </FormControl>
              </Stack>
              <Divider sx={{ my: 3, borderColor: 'rgba(255,255,255,0.08)' }} />
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'stretch', sm: 'center' }}>
                <Button
                  variant="contained"
                  size="large"
                  onClick={executeMigrate}
                  disabled={!canMigrate || migrating || isLoading}
                  startIcon={
                    migrating ? <CircularProgress size={22} color="inherit" /> : <RocketLaunchRoundedIcon />
                  }
                  sx={{
                    px: 3,
                    py: 1.25,
                    fontWeight: 800,
                    borderRadius: 2,
                    boxShadow: '0 8px 32px rgba(45, 212, 191, 0.25)',
                  }}
                >
                  {migrating ? 'Migrating…' : 'Migrate'}
                </Button>
                {!canMigrate && migrateBlockReason ? (
                  <Typography variant="body2" color="text.secondary">
                    {migrateBlockReason}
                  </Typography>
                ) : null}
              </Stack>
            </CardContent>
          </Card>
        ) : null}
      </Stack>

      <Backdrop
        open={migrating}
        sx={{
          zIndex: (t) => t.zIndex.modal + 1,
          flexDirection: 'column',
          gap: 3,
          bgcolor: 'rgba(2, 6, 23, 0.88)',
          backdropFilter: 'blur(10px)',
        }}
      >
        <Box
          sx={{
            position: 'relative',
            width: 120,
            height: 120,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <CircularProgress
            size={120}
            thickness={2}
            sx={{ color: 'primary.light', position: 'absolute', opacity: 0.35 }}
            variant="determinate"
            value={100}
          />
          <CircularProgress size={88} thickness={3} sx={{ color: 'primary.main' }} />
        </Box>
        <Typography variant="h6" fontWeight={700} textAlign="center" sx={{ maxWidth: 360 }}>
          Migrating repositories
        </Typography>
        <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ maxWidth: 400, px: 2 }}>
          Running validation and <code>gh ado2gh migrate-repo</code> on the server. This can take several minutes per
          repository — please keep this page open.
        </Typography>
      </Backdrop>

      <Dialog
        open={resultOpen}
        onClose={closeResult}
        maxWidth="sm"
        fullWidth
        slotProps={{
          backdrop: { sx: { backdropFilter: 'blur(6px)' } },
        }}
        PaperProps={{
          elevation: 24,
          sx: {
            borderRadius: 3,
            overflow: 'hidden',
            border: allOk ? '1px solid rgba(52, 211, 153, 0.45)' : '1px solid rgba(251, 191, 36, 0.45)',
            background: allOk
              ? 'linear-gradient(160deg, rgba(16, 185, 129, 0.22) 0%, rgba(15, 23, 42, 0.97) 42%, #0f172a 100%)'
              : 'linear-gradient(160deg, rgba(245, 158, 11, 0.18) 0%, rgba(15, 23, 42, 0.97) 45%, #0f172a 100%)',
          },
        }}
      >
        <DialogContent sx={{ pt: 4, pb: 2, textAlign: 'center' }}>
          {allOk ? (
            <CheckCircleRoundedIcon sx={{ fontSize: 80, color: '#34d399', filter: 'drop-shadow(0 0 24px rgba(52,211,153,0.55))' }} />
          ) : (
            <WarningAmberRoundedIcon sx={{ fontSize: 80, color: '#fbbf24', filter: 'drop-shadow(0 0 20px rgba(251,191,36,0.45))' }} />
          )}
          <Typography variant="h5" fontWeight={800} sx={{ mt: 2, mb: 1 }}>
            {allOk ? 'Repositories migrated' : 'Migration finished with issues'}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            {allOk
              ? `${successList.length} repo${successList.length === 1 ? '' : 's'} migrated to GitHub successfully.`
              : `${successList.length} succeeded, ${failList.length} failed. Review details below.`}
          </Typography>

          {successList.length > 0 ? (
            <Box sx={{ textAlign: 'left', mb: 2 }}>
              <Typography variant="overline" color="success.light" sx={{ letterSpacing: 1 }}>
                Successful
              </Typography>
              <Stack direction="row" flexWrap="wrap" gap={1} sx={{ mt: 1 }}>
                {successList.map((r) => (
                  <Chip
                    key={`ok-${r.targetRepoName}`}
                    label={`${r.adoRepoName} → ${r.targetRepoName}`}
                    color="success"
                    variant="outlined"
                    sx={{ borderColor: 'rgba(52, 211, 153, 0.5)' }}
                  />
                ))}
              </Stack>
            </Box>
          ) : null}

          {failList.length > 0 ? (
            <Box sx={{ textAlign: 'left' }}>
              <Typography variant="overline" color="warning.light" sx={{ letterSpacing: 1 }}>
                Needs attention
              </Typography>
              <Stack spacing={1.5} sx={{ mt: 1 }}>
                {failList.map((r) => (
                  <Alert key={`fail-${r.targetRepoName}`} severity="error" variant="outlined" sx={{ bgcolor: 'rgba(0,0,0,0.2)' }}>
                    <Typography variant="subtitle2" fontWeight={700}>
                      {r.adoRepoName} → {r.targetRepoName}
                    </Typography>
                    <Typography
                      variant="body2"
                      component="div"
                      sx={{ mt: 0.5, whiteSpace: 'pre-wrap', wordBreak: 'break-word', fontFamily: 'ui-monospace, monospace', fontSize: 12 }}
                    >
                      {r.error || 'Migration failed — see API logs for details.'}
                    </Typography>
                  </Alert>
                ))}
              </Stack>
            </Box>
          ) : null}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3, justifyContent: 'center' }}>
          <Button variant="contained" size="large" onClick={closeResult} sx={{ px: 4, borderRadius: 2, fontWeight: 700 }}>
            Done
          </Button>
        </DialogActions>
      </Dialog>

      {isLoading && (
        <LinearProgress sx={{ position: 'fixed', top: 0, left: 0, right: 0, zIndex: (t) => t.zIndex.drawer + 2 }} />
      )}
    </Box>
  )
}
