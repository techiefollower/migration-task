import { useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'
import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { useWorkspaceAccountKey } from '../context/WorkspaceAccountContext'
import { clearMigrationHistory, loadMigrationHistory } from '../storage/migrationHistoryStorage'
import { isAzureAuthConfigured } from '../auth/authConfig'

export default function DashboardPage() {
  const workspaceKey = useWorkspaceAccountKey()
  const location = useLocation()
  const [rows, setRows] = useState([])

  useEffect(() => {
    setRows(loadMigrationHistory(workspaceKey))
  }, [workspaceKey, location.pathname])

  const handleClear = () => {
    clearMigrationHistory(workspaceKey)
    setRows([])
  }

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={2} sx={{ mb: 3 }}>
        <Box>
          <Typography variant="h4" fontWeight={800} letterSpacing={-0.5} gutterBottom>
            Migration status
          </Typography>
          <Typography variant="body2" color="text.secondary">
            History is stored in this browser for your signed-in account only (not on the server).
            {isAzureAuthConfigured()
              ? ' Each Entra user has a separate workspace.'
              : ' Local mode uses a single workspace on this machine.'}
          </Typography>
        </Box>
        <Button variant="outlined" color="warning" size="small" onClick={handleClear} disabled={rows.length === 0}>
          Clear history
        </Button>
      </Stack>

      <Card elevation={0} sx={{ border: '1px solid rgba(255,255,255,0.08)', borderRadius: 2 }}>
        <CardContent sx={{ p: 0 }}>
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow sx={{ bgcolor: 'rgba(255,255,255,0.04)' }}>
                  <TableCell>Workspace project</TableCell>
                  <TableCell>ADO org</TableCell>
                  <TableCell>ADO project</TableCell>
                  <TableCell>ADO repo</TableCell>
                  <TableCell>GitHub</TableCell>
                  <TableCell>Migration status</TableCell>
                  <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>When</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {rows.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={7} sx={{ py: 6, textAlign: 'center', color: 'text.secondary' }}>
                      No repositories in any migration plan yet.
                    </TableCell>
                  </TableRow>
                ) : (
                  rows.map((r) => (
                    <TableRow key={r.id} hover>
                      <TableCell>{r.workspaceProject}</TableCell>
                      <TableCell sx={{ fontFamily: 'monospace', fontSize: 13 }}>{r.adoOrg}</TableCell>
                      <TableCell>{r.adoProject}</TableCell>
                      <TableCell sx={{ fontWeight: 600 }}>{r.adoRepo}</TableCell>
                      <TableCell sx={{ wordBreak: 'break-all', fontSize: 13 }}>
                        {r.githubOrg}/{r.githubRepo}
                      </TableCell>
                      <TableCell>
                        <Stack direction="row" alignItems="center" gap={0.5} flexWrap="wrap">
                          <Chip
                            size="small"
                            label={r.status}
                            color={r.status === 'Completed' ? 'success' : 'error'}
                            variant="outlined"
                          />
                          {r.errorPreview ? (
                            <Tooltip title={r.errorPreview} placement="top" enterDelay={400}>
                              <Typography variant="caption" color="text.secondary" sx={{ cursor: 'help', maxWidth: 120 }} noWrap>
                                Detail
                              </Typography>
                            </Tooltip>
                          ) : null}
                        </Stack>
                      </TableCell>
                      <TableCell
                        sx={{
                          display: { xs: 'none', md: 'table-cell' },
                          color: 'text.secondary',
                          fontSize: 12,
                          whiteSpace: 'nowrap',
                        }}
                      >
                        {r.completedAt ? new Date(r.completedAt).toLocaleString() : '—'}
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </TableContainer>
        </CardContent>
      </Card>
    </Box>
  )
}
