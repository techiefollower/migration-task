import { Routes, Route, Navigate } from 'react-router-dom'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { Box, CircularProgress } from '@mui/material'
import AppLayout from './layout/AppLayout'
import WizardPage from './pages/WizardPage'
import DashboardPage from './pages/DashboardPage'
import LoginPage from './pages/LoginPage'
import { WorkspaceAccountProvider, useWorkspaceAccountKey } from './context/WorkspaceAccountContext'
import { isAzureAuthConfigured } from './auth/authConfig'

function AppRoutes() {
  const workspaceKey = useWorkspaceAccountKey()
  return (
    <Routes>
      <Route element={<AppLayout key={workspaceKey} />}>
        <Route path="/" element={<Navigate to="/wizard" replace />} />
        <Route path="/wizard" element={<WizardPage />} />
        <Route path="/dashboard" element={<DashboardPage />} />
      </Route>
    </Routes>
  )
}

function AzureGate() {
  const { inProgress, accounts } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const workspaceKey = accounts[0]?.homeAccountId ?? accounts[0]?.localAccountId ?? 'signed-in'

  if (inProgress) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '50vh' }}>
        <CircularProgress />
      </Box>
    )
  }

  if (!isAuthenticated) {
    return <LoginPage />
  }

  return (
    <WorkspaceAccountProvider value={workspaceKey}>
      <AppRoutes />
    </WorkspaceAccountProvider>
  )
}

export default function App() {
  if (!isAzureAuthConfigured()) {
    return (
      <WorkspaceAccountProvider value="local">
        <AppRoutes />
      </WorkspaceAccountProvider>
    )
  }

  return <AzureGate />
}
