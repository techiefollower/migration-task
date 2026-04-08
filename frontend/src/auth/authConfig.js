/**
 * Microsoft Entra ID (Azure AD) — set these for production / App Service deployment.
 * Leave unset for local dev when the API uses AzureAd:DisableAuthentication.
 */
export function isAzureAuthConfigured() {
  return Boolean(
    import.meta.env.VITE_AZURE_CLIENT_ID?.trim() &&
      import.meta.env.VITE_AZURE_TENANT_ID?.trim() &&
      import.meta.env.VITE_AZURE_API_SCOPE?.trim(),
  )
}

export function buildMsalConfig() {
  return {
    auth: {
      clientId: import.meta.env.VITE_AZURE_CLIENT_ID.trim(),
      authority: `https://login.microsoftonline.com/${import.meta.env.VITE_AZURE_TENANT_ID.trim()}`,
      redirectUri: import.meta.env.VITE_AZURE_REDIRECT_URI?.trim() || window.location.origin,
      postLogoutRedirectUri:
        import.meta.env.VITE_AZURE_POST_LOGOUT_REDIRECT_URI?.trim() || window.location.origin,
    },
    cache: {
      cacheLocation: 'sessionStorage',
      storeAuthStateInCookie: false,
    },
  }
}

/** Delegated scope for your API app registration, e.g. api://{api-client-id}/access_as_user */
export function getApiScopes() {
  return [import.meta.env.VITE_AZURE_API_SCOPE.trim()]
}
