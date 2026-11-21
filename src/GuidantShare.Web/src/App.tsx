import { createContext, lazy, Suspense, useContext, useEffect, useState } from 'react'
import { Link, Navigate, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { api } from './api'
import { AcceptInvite, Forgot, Login, Reset } from './pages/AuthPages'
import Home from './pages/Home'
import Plan from './pages/Plan'
import Files from './pages/Files'
import Users from './pages/Users'
import Settings from './pages/Settings'

// The FileSync screens are only for SuperAdmins, so normal users never download them
const FileSyncPage = lazy(() => import('./filesync/FileSyncPage'))

export type Me = { id: string; email: string; role: string }
const Ctx = createContext<{ me: Me | null; setMe: (m: Me | null) => void }>({ me: null, setMe: () => {} })
export const useMe = () => useContext(Ctx)

function Shell({ children }: { children: React.ReactNode }) {
  const { me, setMe } = useMe()
  const nav = useNavigate()
  const [health, setHealth] = useState<any>(null)
  const isAdmin = me?.role === 'SuperAdmin'
  const loc = useLocation()
  useEffect(() => {
    if (!isAdmin) return
    const load = () => fetch('/health').then(r => r.json()).then(setHealth).catch(() => {})
    load()
    window.addEventListener('sf-status-changed', load)
    return () => window.removeEventListener('sf-status-changed', load)
  }, [isAdmin, loc.pathname])
  return (
    <div className="container">
      <nav className="top-nav">
        <Link to="/" className="brand-left">
          <img src="/guidant-logo.png" alt="Guidant MSO" />
          <div>
            <div className="brand-title">GuidantShare</div>
            <div className="brand-subtitle">Guidant MSO Health Plan File Exchange</div>
          </div>
        </Link>
        <div className="nav-actions">
          <NavLink to="/" end className="nav-btn">Health Plans</NavLink>
          {isAdmin && <NavLink to="/admin/filesync" className="nav-btn">FileSync</NavLink>}
          {isAdmin && <NavLink to="/admin/users" className="nav-btn">Users</NavLink>}
          {isAdmin && <NavLink to="/admin/settings" className="nav-btn">Settings</NavLink>}
          <button className="nav-btn" onClick={async () => { await api('/api/auth/logout', { method: 'POST' }); setMe(null); nav('/login') }}>Log out</button>
        </div>
      </nav>
      {isAdmin && health?.sharefile !== 'connected' && (
        <div className="banner warn">ShareFile is not connected. <Link to="/admin/settings">Connect ShareFile</Link> so the team can see and send files.</div>
      )}
      {isAdmin && health?.email === 'misconfigured' && <div className="banner warn">Invite email is misconfigured. Check the Graph settings.</div>}
      {isAdmin && health?.fileSync === 'unreachable' && <div className="banner warn">FileSync can't be reached. Health plans may be out of date and the FileSync screens won't load.</div>}
      <main>{children}</main>
    </div>
  )
}

export default function App() {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)
  useEffect(() => { api<Me>('/api/auth/me').then(setMe).catch(() => setMe(null)).finally(() => setLoading(false)) }, [])
  if (loading) return <div className="center muted">Loading…</div>

  const guard = (el: React.ReactNode, admin = false) =>
    !me ? <Navigate to="/login" /> : admin && me.role !== 'SuperAdmin' ? <Navigate to="/" /> : <Shell>{el}</Shell>

  return (
    <Ctx.Provider value={{ me, setMe }}>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/accept-invite" element={<AcceptInvite />} />
        <Route path="/forgot-password" element={<Forgot />} />
        <Route path="/reset-password" element={<Reset />} />
        <Route path="/" element={guard(<Home />)} />
        <Route path="/plans/:planKey" element={guard(<Plan />)} />
        <Route path="/plans/:planKey/:direction" element={guard(<Plan />)} />
        <Route path="/plans/:planKey/folders/:folderKey" element={guard(<Files />)} />
        <Route path="/admin/filesync" element={guard(<Suspense fallback={<p className="muted">Loading…</p>}><FileSyncPage /></Suspense>, true)} />
        <Route path="/admin/users" element={guard(<Users />, true)} />
        <Route path="/admin/settings" element={guard(<Settings />, true)} />
        <Route path="*" element={<Navigate to="/" />} />
      </Routes>
    </Ctx.Provider>
  )
}
