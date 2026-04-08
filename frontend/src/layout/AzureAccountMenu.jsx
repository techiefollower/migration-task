import { useMsal } from '@azure/msal-react'
import { Box, Button, Typography } from '@mui/material'
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded'

export default function AzureAccountMenu({ compact = false }) {
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  const label =
    account?.name || account?.username || account?.localAccountId || 'Signed in'

  const signOut = () => {
    instance.logoutRedirect({
      account,
    })
  }

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        flexWrap: 'wrap',
        justifyContent: compact ? 'center' : 'flex-start',
      }}
    >
      {!compact ? (
        <Typography variant="body2" color="text.secondary" noWrap sx={{ maxWidth: 160 }}>
          {label}
        </Typography>
      ) : null}
      <Button
        size="small"
        variant="outlined"
        color="inherit"
        startIcon={<LogoutRoundedIcon fontSize="small" />}
        onClick={signOut}
        sx={{ borderColor: 'rgba(255,255,255,0.2)' }}
      >
        {compact ? 'Out' : 'Sign out'}
      </Button>
    </Box>
  )
}
