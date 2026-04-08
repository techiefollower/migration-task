import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { msalInstance } from './msalInstance'
import { getApiScopes, isAzureAuthConfigured } from './authConfig'

/**
 * Returns a Bearer token for the Repo Migration API, or null when auth is not configured (local dev).
 */
export async function acquireApiAccessToken() {
  if (!isAzureAuthConfigured() || !msalInstance) return null

  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]
  if (!account) return null

  const scopes = getApiScopes()
  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes,
      account,
    })
    return result.accessToken
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      await msalInstance.acquireTokenRedirect({
        scopes,
        account,
      })
      return null
    }
    throw e
  }
}
