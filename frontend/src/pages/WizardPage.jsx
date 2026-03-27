import { useMemo, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Alert,
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
  DialogTitle,
  Divider,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  Snackbar,
  Stack,
  Step,
  StepLabel,
  Stepper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
} from '@mui/material'
import { api } from '../api/client'

const steps = ['Azure DevOps', 'Project', 'Repositories & GitHub', 'Queued']

const LOADING = {
  NONE: null,
  ADO_PROJECTS: 'adoProjects',
  ADO_REPOS: 'adoRepos',
  GH_VALIDATE: 'ghValidate',
  GH_CHECK: 'ghCheck',
  QUEUE: 'queue',
}

function sanitizeTargetName(name) {
  const s = name
    .trim()
    .replace(/[^a-zA-Z0-9._-]/g, '-')
    .replace(/-+/g, '-')
    .replace(/^[-.]+|[-.]+$/g, '')
  return s.slice(0, 100) || 'repo'
}

function suggestAlternateName(takenName) {
  const base = takenName.replace(/[^a-zA-Z0-9._-]/g, '') || 'repo'
  const suffix = '-migrated'
  const withSuffix = `${base}${suffix}`.slice(0, 100)
  return withSuffix === base ? `${base}-2`.slice(0, 100) : withSuffix
}

function Btn({ loadingKey, actionKey, children, disabled, onClick, startIcon, ...props }) {
  const anyLoading = loadingKey != null && loadingKey !== LOADING.NONE
  const thisBusy = actionKey != null && loadingKey === actionKey
  return (
    <Button
      {...props}
      onClick={onClick}
      disabled={Boolean(disabled) || anyLoading}
      startIcon={thisBusy ? <CircularProgress color="inherit" size={18} thickness={5} /> : startIcon}
    >
      {children}
    </Button>
  )
}

export default function WizardPage() {
  const navigate = useNavigate()
  const [activeStep, setActiveStep] = useState(0)
  const [loadingKey, setLoadingKey] = useState(LOADING.NONE)

  const [adoOrg, setAdoOrg] = useState('')
  const [adoPat, setAdoPat] = useState('')
  const [projects, setProjects] = useState([])
  const [project, setProject] = useState(null)
  const [repos, setRepos] = useState([])
  const [selected, setSelected] = useState(() => new Set())
  const [targetNames, setTargetNames] = useState({})

  const [githubPat, setGithubPat] = useState('')
  const [githubOwner, setGithubOwner] = useState('')
  const [githubLogin, setGithubLogin] = useState('')
  const [githubValidated, setGithubValidated] = useState(false)
  const [checkResults, setCheckResults] = useState(null)
  const [targetRepoVisibility, setTargetRepoVisibility] = useState('private')
  const [adoPipeline, setAdoPipeline] = useState('')
  const [serviceConnectionId, setServiceConnectionId] = useState('')
  const [error, setError] = useState('')
  const [conflictDialogOpen, setConflictDialogOpen] = useState(false)
  const [snackbar, setSnackbar] = useState({ open: false, message: '', severity: 'success' })

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

  const showSnackbar = (message, severity = 'success') => {
    setSnackbar({ open: true, message, severity })
  }

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
      setActiveStep(1)
      showSnackbar(`Loaded ${(data.projects || []).length} project(s) from Azure DevOps.`, 'success')
    })

  const loadRepos = () =>
    runAction(LOADING.ADO_REPOS, async () => {
      if (!project) {
        setError('Select a project.')
        return
      }
      const { data } = await api.post('/ado/repositories', {
        organization: adoOrg.trim(),
        projectIdOrName: project.name,
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
      setCheckResults(null)
      setConflictDialogOpen(false)
      setActiveStep(2)
      showSnackbar(`Loaded ${list.length} repository(ies).`, 'success')
    })

  const toggleRepo = (id) => {
    setSelected((prev) => {
      const n = new Set(prev)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })
    setCheckResults(null)
    setConflictDialogOpen(false)
  }

  const updateTargetName = (id, value) => {
    setTargetNames((prev) => ({ ...prev, [id]: value }))
    setCheckResults(null)
    setConflictDialogOpen(false)
  }

  const resetGitHubFlow = (opts = { keepValidation: false }) => {
    if (!opts.keepValidation) setGithubValidated(false)
    setCheckResults(null)
    setConflictDialogOpen(false)
  }

  const validateGithub = () =>
    runAction(LOADING.GH_VALIDATE, async () => {
      const { data } = await api.post('/github/validate', {
        personalAccessToken: githubPat,
      })
      if (!data.valid) {
        setError(data.error || 'Invalid GitHub PAT.')
        setGithubValidated(false)
        return
      }
      setGithubLogin(data.login || '')
      setGithubValidated(true)
      if (!githubOwner.trim() && data.login) setGithubOwner(data.login)
      showSnackbar(`GitHub PAT is valid. Signed in as ${data.login || 'user'}.`, 'success')
    })

  const checkGithubNames = () =>
    runAction(LOADING.GH_CHECK, async () => {
      const owner = githubOwner.trim()
      if (!githubValidated) {
        setError('Validate GitHub PAT first, then check repository names.')
        return
      }
      if (!owner) {
        setError('Enter GitHub owner (user or organization).')
        return
      }
      const names = repos
        .filter((r) => selected.has(r.id))
        .map((r) => (targetNames[r.id] || '').trim())
        .filter(Boolean)
      if (names.length === 0) {
        setError('Select at least one repository.')
        return
      }
      const { data } = await api.post('/github/check-repositories', {
        personalAccessToken: githubPat,
        owner,
        repoNames: names,
      })
      if (!data.valid) {
        setError(data.error || 'Could not check GitHub.')
        return
      }
      const results = data.results || []
      setCheckResults(results)
      const conflicts = results.filter((x) => x.exists)
      if (conflicts.length > 0) {
        setConflictDialogOpen(true)
        showSnackbar(
          `${conflicts.length} name(s) already exist on GitHub. Rename them to continue.`,
          'warning',
        )
      } else {
        showSnackbar('All selected names are available on GitHub.', 'success')
      }
    })

  const selectedRepos = useMemo(
    () => repos.filter((r) => selected.has(r.id)),
    [repos, selected],
  )

  const conflictsDetail = useMemo(() => {
    if (!checkResults?.length) return []
    return selectedRepos
      .map((r) => {
        const target = (targetNames[r.id] || '').trim()
        const hit = checkResults.find(
          (x) => x.name.toLowerCase() === target.toLowerCase(),
        )
        if (!hit || !hit.exists) return null
        return {
          repoId: r.id,
          adoName: r.name,
          targetName: target,
          suggestion: suggestAlternateName(target),
        }
      })
      .filter(Boolean)
  }, [checkResults, selectedRepos, targetNames])

  const applySuggestion = (repoId, name) => {
    setTargetNames((prev) => ({ ...prev, [repoId]: name }))
    setCheckResults(null)
    setConflictDialogOpen(false)
    showSnackbar('Target name updated. Run “Check names on GitHub” again.', 'info')
  }

  const rowNameStatus = (repo) => {
    const target = (targetNames[repo.id] || '').trim()
    if (!checkResults || !target) return null
    const hit = checkResults.find((x) => x.name.toLowerCase() === target.toLowerCase())
    if (!hit) return 'unknown'
    return hit.exists ? 'taken' : 'free'
  }

  const canSubmit = useMemo(() => {
    if (selectedRepos.length === 0) return false
    if (!githubPat || !githubOwner.trim()) return false
    if (!githubValidated) return false
    if (!checkResults?.length) return false
    for (const r of selectedRepos) {
      const n = (targetNames[r.id] || '').trim()
      if (!/^[a-zA-Z0-9._-]{1,100}$/.test(n)) return false
    }
    if (checkResults?.length) {
      const taken = checkResults.filter((x) => x.exists)
      if (taken.length > 0) return false
    }
    const pipe = adoPipeline.trim()
    const sc = serviceConnectionId.trim()
    if (pipe !== sc && (pipe === '' || sc === '')) return false
    return true
  }, [
    selectedRepos,
    githubPat,
    githubOwner,
    targetNames,
    checkResults,
    adoPipeline,
    serviceConnectionId,
  ])

  const submitQueue = () =>
    runAction(LOADING.QUEUE, async () => {
      const pipe = adoPipeline.trim()
      const sc = serviceConnectionId.trim()
      const rewire =
        pipe && sc ? { adoPipeline: pipe, serviceConnectionId: sc } : {}
      const items = selectedRepos.map((r) => ({
        sourceRemoteUrl: r.remoteUrl,
        targetRepoName: (targetNames[r.id] || '').trim(),
        targetRepoVisibility,
        ...rewire,
      }))
      await api.post('/migrations/queue', {
        adoPersonalAccessToken: adoPat,
        githubPersonalAccessToken: githubPat,
        githubOwner: githubOwner.trim(),
        repositories: items,
      })
      showSnackbar(`${items.length} migration(s) queued.`, 'success')
      setActiveStep(3)
    })

  const showQueueButton = githubValidated
  const queueHint = !githubValidated
    ? 'Step required: Validate GitHub PAT first.'
    : !checkResults?.length
      ? 'Next: Check names on GitHub.'
      : checkResults.some((x) => x.exists)
        ? 'Resolve duplicate names to continue.'
        : (adoPipeline.trim() && !serviceConnectionId.trim()) ||
            (!adoPipeline.trim() && serviceConnectionId.trim())
          ? 'For pipeline rewire, fill both ADO pipeline and service connection id, or clear both.'
          : 'Ready to migrate.'

  return (
    <Box sx={{ maxWidth: 1100, mx: 'auto' }}>
      <Typography variant="h4" gutterBottom>
        Migration wizard
      </Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
        Connect to Azure DevOps, pick repositories, then queue migrations. The server runs{' '}
        <strong>gh ado2gh</strong>: inventory report, migrate-repo, and optional pipeline rewire (requires{' '}
        <code>gh</code> + extension on the API host).
      </Typography>

      <Stepper activeStep={activeStep} alternativeLabel sx={{ mb: 3 }}>
        {steps.map((label) => (
          <Step key={label}>
            <StepLabel>{label}</StepLabel>
          </Step>
        ))}
      </Stepper>

      {error ? (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError('')}>
          {error}
        </Alert>
      ) : null}

      <Card elevation={0} sx={{ border: '1px solid rgba(255,255,255,0.08)' }}>
        <CardContent sx={{ p: { xs: 2, md: 3 } }}>
          {activeStep === 0 && (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, maxWidth: 480 }}>
              <TextField
                label="Azure DevOps organization"
                placeholder="e.g. contoso"
                value={adoOrg}
                onChange={(e) => setAdoOrg(e.target.value)}
                fullWidth
                required
                helperText="Use only the org slug from https://dev.azure.com/{organization}"
              />
              <TextField
                label="Azure DevOps PAT"
                type="password"
                value={adoPat}
                onChange={(e) => setAdoPat(e.target.value)}
                fullWidth
                required
                helperText="Required scopes: Code (Read), Project and team (Read)."
              />
              <Btn
                loadingKey={loadingKey}
                actionKey={LOADING.ADO_PROJECTS}
                variant="contained"
                onClick={loadProjects}
                disabled={!adoOrg.trim() || !adoPat}
              >
                Validate &amp; load projects
              </Btn>
            </Box>
          )}

          {activeStep === 1 && (
            <Box>
              <Typography variant="subtitle1" gutterBottom>
                Projects in <strong>{adoOrg}</strong>
              </Typography>
              <TableContainer sx={{ mb: 2 }}>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Name</TableCell>
                      <TableCell>Id</TableCell>
                      <TableCell align="right">Select</TableCell>
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
                        <TableCell sx={{ color: 'text.secondary', fontFamily: 'monospace' }}>{p.id}</TableCell>
                        <TableCell align="right">
                          <Button
                            size="small"
                            variant={project?.id === p.id ? 'contained' : 'outlined'}
                            onClick={(e) => {
                              e.stopPropagation()
                              setProject(p)
                            }}
                          >
                            Use
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                <Btn loadingKey={loadingKey} actionKey={null} onClick={() => setActiveStep(0)}>
                  Back
                </Btn>
                <Btn
                  loadingKey={loadingKey}
                  actionKey={LOADING.ADO_REPOS}
                  variant="contained"
                  onClick={loadRepos}
                  disabled={!project}
                >
                  Load repositories
                </Btn>
              </Box>
            </Box>
          )}

          {activeStep === 2 && (
            <Stack spacing={3}>
              <Alert severity="info" sx={{ border: '1px solid rgba(255,255,255,0.08)' }}>
                <Typography variant="subtitle2" component="div" gutterBottom>
                  How this step works
                </Typography>
                <Box component="ul" sx={{ pl: 2.25, m: 0, '& li': { mb: 0.75 } }}>
                  <Typography component="li" variant="body2" color="text.secondary">
                    Select Azure DevOps repositories in the table.
                  </Typography>
                  <Typography component="li" variant="body2" color="text.secondary">
                    Set each <strong>new GitHub repository name</strong> in the cards (you can rename freely).
                  </Typography>
                  <Typography component="li" variant="body2" color="text.secondary">
                    Validate your GitHub PAT, then run <strong>Check names on GitHub</strong>. The{' '}
                    <strong>Name check</strong> chip shows if that name already exists for your owner.
                  </Typography>
                  <Typography component="li" variant="body2" color="text.secondary">
                    When every name is <strong>Available</strong>, queue the migrations.
                  </Typography>
                </Box>
              </Alert>

              <Box>
                <Typography variant="h6" gutterBottom>
                  1. Select repositories
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
                  Project: <strong>{project?.name}</strong> — check the repos you want to migrate.
                </Typography>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow sx={{ bgcolor: 'rgba(255,255,255,0.04)' }}>
                        <TableCell padding="checkbox" width={52} />
                        <TableCell>Repository</TableCell>
                        <TableCell>Clone URL</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {repos.map((r) => (
                        <TableRow key={r.id} hover selected={selected.has(r.id)}>
                          <TableCell padding="checkbox">
                            <Checkbox checked={selected.has(r.id)} onChange={() => toggleRepo(r.id)} />
                          </TableCell>
                          <TableCell sx={{ fontWeight: 700 }}>{r.name}</TableCell>
                          <TableCell>
                            <Typography variant="body2" color="text.secondary" sx={{ wordBreak: 'break-all', fontSize: 12 }}>
                              {r.remoteUrl}
                            </Typography>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Box>

              {selectedRepos.length > 0 ? (
                <Box>
                  <Typography variant="h6" gutterBottom>
                    2. GitHub names &amp; name check
                  </Typography>
                  <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                    Each card is one migration. The name you enter is the <strong>new private repo</strong> on GitHub.
                    Use letters, numbers, dots, underscores, and hyphens only.
                  </Typography>
                  <Alert severity="info" variant="outlined" sx={{ mb: 2, borderColor: 'rgba(94,234,212,0.25)' }}>
                    <Typography variant="subtitle2" gutterBottom>
                      What is “Name check”?
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      After you click <strong>Check names on GitHub</strong>, we call the GitHub API for each target
                      name under your <strong>owner</strong>. <strong>Available</strong> means no repo with that name
                      exists yet (safe to create). <strong>Taken</strong> means you must rename in this card and check
                      again. <strong>Not checked</strong> means you have not run the check yet.
                    </Typography>
                  </Alert>
                  <Stack spacing={2}>
                    {selectedRepos.map((r) => {
                      const status = rowNameStatus(r)
                      const tn = (targetNames[r.id] || '').trim()
                      const invalid = Boolean(tn) && !/^[a-zA-Z0-9._-]{1,100}$/.test(tn)
                      return (
                        <Card
                          key={r.id}
                          variant="outlined"
                          sx={{
                            borderRadius: 2,
                            borderColor: 'rgba(255,255,255,0.12)',
                            bgcolor: 'rgba(255,255,255,0.02)',
                          }}
                        >
                          <CardContent sx={{ p: { xs: 2, sm: 2.5 } }}>
                            <Box
                              sx={{
                                display: 'flex',
                                flexWrap: 'wrap',
                                alignItems: 'flex-start',
                                justifyContent: 'space-between',
                                gap: 1,
                                mb: 2,
                              }}
                            >
                              <Box sx={{ minWidth: 0 }}>
                                <Typography variant="overline" color="text.secondary" sx={{ display: 'block' }}>
                                  From Azure DevOps
                                </Typography>
                                <Typography variant="h6" sx={{ fontSize: '1.1rem', fontWeight: 700 }}>
                                  {r.name}
                                </Typography>
                                <Typography
                                  variant="caption"
                                  color="text.secondary"
                                  sx={{ wordBreak: 'break-all', display: 'block', mt: 0.5 }}
                                >
                                  {r.remoteUrl}
                                </Typography>
                              </Box>
                              <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: { xs: 'flex-start', sm: 'flex-end' }, gap: 0.5 }}>
                                <Typography variant="caption" color="text.secondary">
                                  Name check
                                </Typography>
                                {!checkResults ? (
                                  <Chip size="small" label="Not checked" variant="outlined" />
                                ) : status === 'taken' ? (
                                  <Chip size="small" color="error" label="Taken on GitHub" />
                                ) : status === 'free' ? (
                                  <Chip size="small" color="success" label="Available" />
                                ) : (
                                  <Chip size="small" label="Check again" variant="outlined" color="warning" />
                                )}
                              </Box>
                            </Box>
                            <Divider sx={{ borderColor: 'rgba(255,255,255,0.08)', my: 1.5 }} />
                            <Typography variant="overline" color="primary" sx={{ display: 'block', mb: 1 }}>
                              New repository name on GitHub
                            </Typography>
                            <TextField
                              fullWidth
                              size="medium"
                              value={targetNames[r.id] || ''}
                              onChange={(e) => updateTargetName(r.id, e.target.value)}
                              error={invalid}
                              helperText={
                                invalid
                                  ? 'Use only letters, numbers, . _ - (max 100 characters).'
                                  : githubOwner.trim()
                                    ? `Target: https://github.com/${githubOwner.trim()}/${tn || 'your-repo'} (${targetRepoVisibility}).`
                                    : 'Enter the GitHub owner in step 3 — the URL will use that account or organization.'
                              }
                              placeholder="e.g. my-product-api"
                            />
                            <Box sx={{ mt: 1.5, display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'center' }}>
                              <Button
                                size="small"
                                variant="outlined"
                                onClick={() => updateTargetName(r.id, sanitizeTargetName(r.name))}
                              >
                                Use cleaned ADO name
                              </Button>
                              <Typography variant="caption" color="text.secondary">
                                Suggested from ADO: <strong>{sanitizeTargetName(r.name)}</strong>
                              </Typography>
                            </Box>
                          </CardContent>
                        </Card>
                      )
                    })}
                  </Stack>
                </Box>
              ) : (
                <Alert severity="warning" variant="outlined">
                  Select at least one repository in the table above to set GitHub names and continue.
                </Alert>
              )}

              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, maxWidth: 560 }}>
                <Typography variant="h6">3. GitHub connection</Typography>
                <TextField
                  label="GitHub PAT"
                  type="password"
                  value={githubPat}
                  onChange={(e) => {
                    setGithubPat(e.target.value)
                    resetGitHubFlow()
                  }}
                  fullWidth
                  helperText="PAT with access to create and push repositories."
                />
                <TextField
                  label="GitHub owner (user or org)"
                  value={githubOwner}
                  onChange={(e) => {
                    setGithubOwner(e.target.value)
                    resetGitHubFlow({ keepValidation: true })
                  }}
                  fullWidth
                  helperText={
                    githubValidated && githubLogin
                      ? `Validated as ${githubLogin}.`
                      : 'Validate PAT to unlock the next step.'
                  }
                />
                <Alert
                  severity={githubValidated ? 'success' : 'info'}
                  sx={{ border: '1px solid rgba(255,255,255,0.08)' }}
                >
                  {githubValidated
                    ? 'GitHub PAT validated. Next: click “Check names on GitHub” to update the name check on each card.'
                    : 'Validate your GitHub PAT before checking names or queuing migrations.'}
                </Alert>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                  <Btn
                    loadingKey={loadingKey}
                    actionKey={LOADING.GH_VALIDATE}
                    onClick={validateGithub}
                    disabled={!githubPat}
                  >
                    Validate GitHub PAT
                  </Btn>
                  <Btn
                    loadingKey={loadingKey}
                    actionKey={LOADING.GH_CHECK}
                    variant="outlined"
                    onClick={checkGithubNames}
                    disabled={!githubValidated || !githubOwner.trim() || selected.size === 0}
                  >
                    Check names on GitHub
                  </Btn>
                </Box>
                {checkResults?.length && !checkResults.some((x) => x.exists) ? (
                  <Alert severity="success">
                    All selected names look available under <strong>{githubOwner.trim()}</strong>. You can queue migrations.
                  </Alert>
                ) : null}

                <Typography variant="subtitle2" color="text.secondary">
                  Migration options (gh ado2gh)
                </Typography>
                <FormControl fullWidth size="small" sx={{ maxWidth: 360 }}>
                  <InputLabel id="visibility-label">GitHub repo visibility</InputLabel>
                  <Select
                    labelId="visibility-label"
                    label="GitHub repo visibility"
                    value={targetRepoVisibility}
                    onChange={(e) => setTargetRepoVisibility(e.target.value)}
                  >
                    <MenuItem value="private">private</MenuItem>
                    <MenuItem value="internal">internal</MenuItem>
                    <MenuItem value="public">public</MenuItem>
                  </Select>
                </FormControl>
                <TextField
                  label="ADO pipeline (optional, for rewire)"
                  value={adoPipeline}
                  onChange={(e) => setAdoPipeline(e.target.value)}
                  fullWidth
                  helperText="Pipeline name or id — use with service connection id below, or leave both empty."
                />
                <TextField
                  label="ADO service connection id (optional)"
                  value={serviceConnectionId}
                  onChange={(e) => setServiceConnectionId(e.target.value)}
                  fullWidth
                  helperText="GUID from Project settings → Service connections (required if pipeline is set)."
                />
              </Box>

              <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center' }}>
                <Btn loadingKey={loadingKey} actionKey={null} onClick={() => setActiveStep(1)}>
                  Back
                </Btn>
                {showQueueButton ? (
                  <Button
                    variant="contained"
                    color="primary"
                    onClick={submitQueue}
                    disabled={!canSubmit || isLoading}
                    startIcon={
                      loadingKey === LOADING.QUEUE ? (
                        <CircularProgress color="inherit" size={18} thickness={5} />
                      ) : null
                    }
                  >
                    Queue migrations
                  </Button>
                ) : null}
                <Typography variant="body2" color="text.secondary">
                  {queueHint}
                </Typography>
              </Box>
            </Stack>
          )}

          {activeStep === 3 && (
            <Box sx={{ textAlign: 'center', py: 2 }}>
              <Typography variant="h6" gutterBottom>
                Migrations queued
              </Typography>
              <Typography color="text.secondary" sx={{ mb: 3 }}>
                Jobs run in Hangfire and execute <strong>gh ado2gh</strong> on the server. Track logs and status on the
                dashboard.
              </Typography>
              <Button variant="contained" size="large" onClick={() => navigate('/dashboard')}>
                Open dashboard
              </Button>
            </Box>
          )}
        </CardContent>
      </Card>

      <Dialog
        open={conflictDialogOpen}
        onClose={() => setConflictDialogOpen(false)}
        maxWidth="sm"
        fullWidth
        PaperProps={{ sx: { borderRadius: 2 } }}
      >
        <DialogTitle sx={{ pr: 6 }}>
          <Typography variant="h6" component="span">
            Name already on GitHub
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1, fontWeight: 400 }}>
            One or more target names already exist under <strong>{githubOwner.trim() || 'this owner'}</strong>. GitHub
            cannot create a duplicate. Rename the targets below, then run <strong>Check names on GitHub</strong> again.
          </Typography>
        </DialogTitle>
        <DialogContent dividers sx={{ bgcolor: 'rgba(0,0,0,0.2)' }}>
          <List disablePadding>
            {conflictsDetail.map((c) => (
              <ListItem
                key={c.repoId}
                sx={{
                  flexDirection: 'column',
                  alignItems: 'stretch',
                  bgcolor: 'background.paper',
                  borderRadius: 2,
                  mb: 1.5,
                  py: 2,
                  px: 2,
                  border: '1px solid rgba(255,255,255,0.08)',
                }}
              >
                <ListItemText
                  primary={
                    <Typography variant="subtitle2" color="primary">
                      {c.adoName}
                    </Typography>
                  }
                  secondary={
                    <>
                      <Typography variant="body2" color="text.secondary" component="span" display="block">
                        Target name <strong>{c.targetName}</strong> is already in use.
                      </Typography>
                      <Box sx={{ mt: 1.5, display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
                        <Typography variant="caption" color="text.secondary">
                          Suggested:
                        </Typography>
                        <Chip size="small" label={c.suggestion} variant="outlined" />
                        <Button size="small" variant="contained" onClick={() => applySuggestion(c.repoId, c.suggestion)}>
                          Use suggestion
                        </Button>
                      </Box>
                    </>
                  }
                />
              </ListItem>
            ))}
          </List>
        </DialogContent>
        <DialogActions sx={{ px: 3, py: 2 }}>
          <Button onClick={() => setConflictDialogOpen(false)}>Got it</Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={5000}
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

      {isLoading && (
        <LinearProgress
          sx={{ position: 'fixed', top: 0, left: 0, right: 0, zIndex: (t) => t.zIndex.drawer + 2 }}
        />
      )}
    </Box>
  )
}
