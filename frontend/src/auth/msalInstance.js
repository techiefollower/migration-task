import { PublicClientApplication } from '@azure/msal-browser'
import { buildMsalConfig, isAzureAuthConfigured } from './authConfig'

export const msalInstance = isAzureAuthConfigured()
  ? new PublicClientApplication(buildMsalConfig())
  : null
