import { useMsal } from '@azure/msal-react'
import { Box, Button, Card, CardContent, Stack, Typography } from '@mui/material'
import { getApiScopes } from '../auth/authConfig'

export default function LoginPage() {
  const { instance } = useMsal()

  const signIn = () => {
    instance.loginRedirect({
      scopes: ['openid', 'profile', 'email', ...getApiScopes()],
    })
  }

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        p: 2,
        background: 'radial-gradient(ellipse at top, rgba(45, 212, 191, 0.12), transparent 55%), #0b1220',
      }}
    >
      <Card
        elevation={0}
        sx={{
          maxWidth: 440,
          width: 1,
          borderRadius: 3,
          border: '1px solid rgba(255,255,255,0.1)',
          bgcolor: 'rgba(15, 23, 42, 0.85)',
          backdropFilter: 'blur(12px)',
        }}
      >
        <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
          <Typography variant="overline" color="primary" sx={{ letterSpacing: 2 }}>
            Sign in required
          </Typography>
          <Typography variant="h5" fontWeight={800} sx={{ mt: 1, mb: 1.5 }}>
            Repo Migration
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            Use your work account to open your own workspace. Azure DevOps and GitHub tokens are not shared between
            users and are not stored on the server.
          </Typography>
          <Stack spacing={1.5}>
            <Button variant="contained" size="large" onClick={signIn} sx={{ fontWeight: 700, py: 1.25 }}>
              Sign in with Microsoft
            </Button>
            <Typography variant="caption" color="text.secondary" sx={{ textAlign: 'center' }}>
              Microsoft Entra ID (Azure AD)
            </Typography>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  )
}
