import { useEffect, useState } from 'react'
import { Outlet, NavLink, useLocation } from 'react-router-dom'
import {
  Box,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Toolbar,
  Typography,
  Divider,
  useMediaQuery,
} from '@mui/material'
import { useTheme } from '@mui/material/styles'

const drawerWidth = 260
const drawerMiniWidth = 92

const nav = [
  { to: '/wizard', label: 'New migration' },
  { to: '/dashboard', label: 'Dashboard' },
]

export default function AppLayout() {
  const theme = useTheme()
  const isMobile = useMediaQuery(theme.breakpoints.down('md'))
  // Desktop states: 2 = full, 1 = mini, 0 = closed
  const [desktopNavState, setDesktopNavState] = useState(2)
  const [mobileOpen, setMobileOpen] = useState(false)

  useEffect(() => {
    if (isMobile) setMobileOpen(false)
    else setDesktopNavState(2)
  }, [isMobile])

  const location = useLocation()

  const closeOnNavigate = () => {
    if (isMobile) setMobileOpen(false)
  }

  const isMini = !isMobile && desktopNavState === 1
  const isClosed = !isMobile && desktopNavState === 0
  const drawerCurrentWidth = isMobile
    ? drawerWidth
    : desktopNavState === 2
      ? drawerWidth
      : desktopNavState === 1
        ? drawerMiniWidth
        : 0

  const collapseStep = () => {
    if (isMobile) return
    setDesktopNavState((s) => (s === 2 ? 1 : s === 1 ? 0 : 2))
  }

  const drawerContent = (
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
        {!isMobile ? (
          <IconButton
            edge="end"
            size="small"
            onClick={collapseStep}
            aria-label="Collapse sidebar"
            title={
              desktopNavState === 2
                ? 'Shrink menu'
                : desktopNavState === 1
                  ? 'Close menu'
                  : 'Open menu'
            }
            sx={{
              border: '1px solid rgba(255,255,255,0.12)',
              borderRadius: 2,
              flexShrink: 0,
              ml: isMini ? 0 : 'auto',
            }}
          >
            <Typography component="span" sx={{ fontSize: '1.1rem', lineHeight: 1 }} aria-hidden>
              {desktopNavState === 2 ? '⟨' : desktopNavState === 1 ? '×' : '☰'}
            </Typography>
          </IconButton>
        ) : null}
      </Toolbar>
      {!isMini && (
        <Typography variant="body2" color="text.secondary" sx={{ px: 2, pb: 1 }}>
          gh ado2gh (inventory, migrate, rewire)
        </Typography>
      )}
      <Divider sx={{ borderColor: 'rgba(255,255,255,0.08)' }} />
      <List sx={{ px: 1, py: 2 }}>
        {nav.map((item) => (
          <ListItemButton
            key={item.to}
            component={NavLink}
            to={item.to}
            selected={location.pathname === item.to}
            onClick={closeOnNavigate}
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
      {!isMini && (
        <Typography variant="caption" color="text.secondary" sx={{ px: 2, pb: 2 }}>
        </Typography>
      )}
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Drawer
        variant={isMobile ? 'temporary' : 'permanent'}
        anchor="left"
        open={isMobile ? mobileOpen : true}
        onClose={() => setMobileOpen(false)}
        ModalProps={{ keepMounted: true }}
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
            borderRight: isClosed ? 'none' : '1px solid rgba(255,255,255,0.06)',
            overflowX: 'hidden',
            transition: theme.transitions.create('width', {
              easing: theme.transitions.easing.sharp,
              duration: theme.transitions.duration.shorter,
            }),
          },
        }}
      >
        {drawerContent}
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
        {(isMobile || isClosed) && (
          <Toolbar
            disableGutters
            sx={{
              minHeight: 48,
              mb: 1,
              gap: 1,
              alignItems: 'center',
            }}
          >
            <IconButton
              color="inherit"
              edge="start"
              onClick={() => {
                if (isMobile) setMobileOpen(true)
                else setDesktopNavState(2)
              }}
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
            <Typography variant="subtitle1" fontWeight={700} noWrap>
              Repo Migration
            </Typography>
          </Toolbar>
        )}
        <Outlet />
      </Box>
    </Box>
  )
}
