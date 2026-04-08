import { useState } from 'react'
import { Outlet, NavLink, useLocation } from 'react-router-dom'
import {
  Box,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Toolbar,
  Tooltip,
  Typography,
  Divider,
  useMediaQuery,
} from '@mui/material'
import { useTheme } from '@mui/material/styles'
import { isAzureAuthConfigured } from '../auth/authConfig'
import { useWorkspaceAccountKey } from '../context/WorkspaceAccountContext'
import { loadSidebarWidthState, saveSidebarWidthState } from '../storage/uiPreferencesStorage'
import AzureAccountMenu from './AzureAccountMenu'

const drawerWidth = 260
const drawerMiniWidth = 92

const nav = [
  { to: '/wizard', label: 'Migrate repositories' },
  { to: '/dashboard', label: 'Dashboard' },
]

function NavDrawerBody({ isMini, onAfterNavigate, location }) {
  return (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <Toolbar
        sx={{
          px: 1.5,
          py: 1.5,
          minHeight: 56,
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 1,
        }}
      >
        {!isMini && (
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="overline" color="primary" sx={{ letterSpacing: 2, display: 'block' }}>
              ADO → GitHub
            </Typography>
            <Typography variant="h6" sx={{ lineHeight: 1.2 }} noWrap>
              Repo Migration
            </Typography>
          </Box>
        )}
      </Toolbar>
      <Divider sx={{ borderColor: 'rgba(255,255,255,0.08)' }} />
      <List sx={{ px: 1, py: 2 }}>
        {nav.map((item) => (
          <ListItemButton
            key={item.to}
            component={NavLink}
            to={item.to}
            selected={location.pathname === item.to}
            onClick={onAfterNavigate}
            sx={{
              borderRadius: 2,
              mb: 0.5,
              '&.active': {
                bgcolor: 'rgba(94, 234, 212, 0.12)',
                borderLeft: '3px solid',
                borderColor: 'primary.main',
              },
            }}
          >
            <ListItemText
              primary={isMini ? item.label.charAt(0) : item.label}
              primaryTypographyProps={{
                fontWeight: 600,
                textAlign: isMini ? 'center' : 'left',
              }}
            />
          </ListItemButton>
        ))}
      </List>
      <Box sx={{ flex: 1 }} />
      {isAzureAuthConfigured() ? (
        <Box
          sx={{
            px: isMini ? 0.5 : 2,
            pb: 2,
            pt: 1,
            borderTop: '1px solid rgba(255,255,255,0.06)',
          }}
        >
          <AzureAccountMenu compact={isMini} />
        </Box>
      ) : null}
    </Box>
  )
}

/** Mobile-only: unmounting this tree resets drawer open state (no effects/refs for breakpoint). */
function AppLayoutMobile({ location }) {
  const [mobileOpen, setMobileOpen] = useState(false)
  const closeDrawer = () => setMobileOpen(false)

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Drawer
        variant="temporary"
        anchor="left"
        open={mobileOpen}
        onClose={closeDrawer}
        ModalProps={{ keepMounted: true }}
        sx={{
          flexShrink: 0,
          width: drawerWidth,
          '& .MuiDrawer-paper': {
            width: drawerWidth,
            boxSizing: 'border-box',
            bgcolor: 'background.paper',
            borderRight: '1px solid rgba(255,255,255,0.06)',
          },
        }}
      >
        <NavDrawerBody isMini={false} onAfterNavigate={closeDrawer} location={location} />
      </Drawer>

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          minWidth: 0,
          p: { xs: 2, md: 4 },
        }}
      >
        <Toolbar
          disableGutters
          sx={{
            minHeight: 48,
            mb: 1,
            gap: 1,
            alignItems: 'center',
          }}
        >
          <Tooltip title="Open menu">
            <IconButton
              color="inherit"
              edge="start"
              onClick={() => setMobileOpen(true)}
              aria-label="Open menu"
              sx={{
                border: '1px solid rgba(255,255,255,0.12)',
                borderRadius: 2,
              }}
            >
              <Typography component="span" sx={{ fontSize: '1.25rem', lineHeight: 1 }}>
                ☰
              </Typography>
            </IconButton>
          </Tooltip>
          <Typography variant="subtitle1" fontWeight={700} noWrap>
            Repo Migration
          </Typography>
        </Toolbar>
        <Outlet />
      </Box>
    </Box>
  )
}

function AppLayoutDesktop({ theme, location, desktopNavState, toggleDesktopSidebar }) {
  const isMini = desktopNavState === 1
  const drawerCurrentWidth = desktopNavState === 2 ? drawerWidth : drawerMiniWidth

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Drawer
        variant="permanent"
        anchor="left"
        open
        sx={{
          flexShrink: 0,
          width: drawerCurrentWidth,
          transition: theme.transitions.create('width', {
            easing: theme.transitions.easing.sharp,
            duration: theme.transitions.duration.shorter,
          }),
          '& .MuiDrawer-paper': {
            width: drawerCurrentWidth,
            boxSizing: 'border-box',
            bgcolor: 'background.paper',
            borderRight: '1px solid rgba(255,255,255,0.06)',
            overflowX: 'hidden',
            transition: theme.transitions.create('width', {
              easing: theme.transitions.easing.sharp,
              duration: theme.transitions.duration.shorter,
            }),
          },
        }}
      >
        <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
          <Toolbar
            sx={{
              px: 1.5,
              py: 1.5,
              minHeight: 56,
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 1,
            }}
          >
            {!isMini && (
              <Box sx={{ minWidth: 0 }}>
                <Typography variant="overline" color="primary" sx={{ letterSpacing: 2, display: 'block' }}>
                  ADO → GitHub
                </Typography>
                <Typography variant="h6" sx={{ lineHeight: 1.2 }} noWrap>
                  Repo Migration
                </Typography>
              </Box>
            )}
            <Tooltip
              title={
                desktopNavState === 2 ? 'Compact sidebar (icons only)' : 'Expand sidebar'
              }
              placement="right"
            >
              <IconButton
                edge="end"
                size="small"
                onClick={toggleDesktopSidebar}
                aria-label={desktopNavState === 2 ? 'Compact sidebar' : 'Expand sidebar'}
                aria-expanded={desktopNavState === 2}
                sx={{
                  border: '1px solid rgba(255,255,255,0.12)',
                  borderRadius: 2,
                  flexShrink: 0,
                  ml: isMini ? 0 : 'auto',
                }}
              >
                <Typography component="span" sx={{ fontSize: '1.15rem', lineHeight: 1 }} aria-hidden>
                  {desktopNavState === 2 ? '☰' : '»'}
                </Typography>
              </IconButton>
            </Tooltip>
          </Toolbar>
          <Divider sx={{ borderColor: 'rgba(255,255,255,0.08)' }} />
          <List sx={{ px: 1, py: 2 }}>
            {nav.map((item) => (
              <ListItemButton
                key={item.to}
                component={NavLink}
                to={item.to}
                selected={location.pathname === item.to}
                sx={{
                  borderRadius: 2,
                  mb: 0.5,
                  '&.active': {
                    bgcolor: 'rgba(94, 234, 212, 0.12)',
                    borderLeft: '3px solid',
                    borderColor: 'primary.main',
                  },
                }}
              >
                <ListItemText
                  primary={isMini ? item.label.charAt(0) : item.label}
                  primaryTypographyProps={{
                    fontWeight: 600,
                    textAlign: isMini ? 'center' : 'left',
                  }}
                />
              </ListItemButton>
            ))}
          </List>
          <Box sx={{ flex: 1 }} />
          {isAzureAuthConfigured() ? (
            <Box
              sx={{
                px: isMini ? 0.5 : 2,
                pb: 2,
                pt: 1,
                borderTop: '1px solid rgba(255,255,255,0.06)',
              }}
            >
              <AzureAccountMenu compact={isMini} />
            </Box>
          ) : null}
        </Box>
      </Drawer>

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          minWidth: 0,
          transition: theme.transitions.create(['margin', 'width'], {
            easing: theme.transitions.easing.sharp,
            duration: theme.transitions.duration.shorter,
          }),
          p: { xs: 2, md: 4 },
        }}
      >
        <Outlet />
      </Box>
    </Box>
  )
}

export default function AppLayout() {
  const theme = useTheme()
  const workspaceKey = useWorkspaceAccountKey()
  const isMobile = useMediaQuery(theme.breakpoints.down('md'))
  const location = useLocation()

  const [desktopNavState, setDesktopNavState] = useState(() => {
    const saved = loadSidebarWidthState(workspaceKey)
    return saved === 1 || saved === 2 ? saved : 2
  })

  const toggleDesktopSidebar = () => {
    setDesktopNavState((s) => {
      const next = s === 2 ? 1 : 2
      saveSidebarWidthState(workspaceKey, next)
      return next
    })
  }

  if (isMobile) {
    return <AppLayoutMobile location={location} />
  }

  return (
    <AppLayoutDesktop
      theme={theme}
      location={location}
      desktopNavState={desktopNavState}
      toggleDesktopSidebar={toggleDesktopSidebar}
    />
  )
}
