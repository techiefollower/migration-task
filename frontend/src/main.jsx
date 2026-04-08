import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { CssBaseline, ThemeProvider } from '@mui/material'
import { MsalProvider } from '@azure/msal-react'
import './index.css'
import { appTheme } from './theme'
import App from './App.jsx'
import { msalInstance } from './auth/msalInstance'

const appTree = (
  <BrowserRouter>
    <ThemeProvider theme={appTheme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  </BrowserRouter>
)

async function start() {
  if (msalInstance) {
    await msalInstance.initialize()
    await msalInstance.handleRedirectPromise()
    const accounts = msalInstance.getAllAccounts()
    if (accounts.length > 0 && !msalInstance.getActiveAccount()) {
      msalInstance.setActiveAccount(accounts[0])
    }
  }

  createRoot(document.getElementById('root')).render(
    <StrictMode>
      {msalInstance ? <MsalProvider instance={msalInstance}>{appTree}</MsalProvider> : appTree}
    </StrictMode>,
  )
}

start()
