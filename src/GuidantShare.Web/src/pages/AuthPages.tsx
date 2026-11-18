import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import { useMe } from '../App'

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return <div className="center"><div className="card auth"><img src="/guidant-logo.png" alt="Guidant MSO" className="auth-logo" /><div className="brand-title">GuidantShare</div><h1>{title}</h1>{children}</div></div>
}

export function Login() {
  const { setMe } = useMe(); const nav = useNavigate()
  const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [err, setErr] = useState('')
  return (
    <Card title="Sign in">
      <form onSubmit={async e => { e.preventDefault(); setErr(''); try { setMe(await api('/api/auth/login', { method: 'POST', body: { email, password } })); nav('/') } catch (x: any) { setErr(x.message) } }}>
        <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} autoFocus required /></label>
        <label>Password<input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></label>
        {err && <p className="error">{err}</p>}
        <button className="primary" type="submit">Sign in</button>
        <p><Link to="/forgot-password">Forgot password?</Link></p>
      </form>
    </Card>
  )
}

export function AcceptInvite() {
  const [sp] = useSearchParams(); const token = sp.get('token') ?? ''; const nav = useNavigate()
  const [email, setEmail] = useState(''); const [pw, setPw] = useState(''); const [err, setErr] = useState(''); const [bad, setBad] = useState(false)
  useEffect(() => { api(`/api/auth/invite-info?token=${encodeURIComponent(token)}`).then(r => setEmail(r.email)).catch(x => { setBad(true); setErr(x.message) }) }, [token])
  if (bad) return <Card title="Invite"><p className="error">{err}</p><Link to="/login">Go to sign in</Link></Card>
  return (
    <Card title="Set your password">
      <form onSubmit={async e => { e.preventDefault(); setErr(''); try { await api('/api/auth/accept-invite', { method: 'POST', body: { token, password: pw } }); nav('/login') } catch (x: any) { setErr(x.message) } }}>
        <label>Email<input value={email} readOnly /></label>
        <label>New password (min 8 characters)<input type="password" value={pw} minLength={8} onChange={e => setPw(e.target.value)} required /></label>
        {err && <p className="error">{err}</p>}
        <button className="primary" type="submit">Create account</button>
      </form>
    </Card>
  )
}

export function Forgot() {
  const [email, setEmail] = useState(''); const [done, setDone] = useState(false)
  return (
    <Card title="Reset password">
      {done ? <p>If that email has an account, a reset link is on its way.</p> :
        <form onSubmit={async e => { e.preventDefault(); await api('/api/auth/forgot-password', { method: 'POST', body: { email } }); setDone(true) }}>
          <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required /></label>
          <button className="primary" type="submit">Send reset link</button>
        </form>}
      <p><Link to="/login">Back to sign in</Link></p>
    </Card>
  )
}

export function Reset() {
  const [sp] = useSearchParams(); const nav = useNavigate()
  const [pw, setPw] = useState(''); const [err, setErr] = useState('')
  return (
    <Card title="Choose a new password">
      <form onSubmit={async e => { e.preventDefault(); setErr(''); try { await api('/api/auth/reset-password', { method: 'POST', body: { email: sp.get('email'), token: sp.get('token'), password: pw } }); nav('/login') } catch (x: any) { setErr(x.message) } }}>
        <label>New password (min 8 characters)<input type="password" minLength={8} value={pw} onChange={e => setPw(e.target.value)} required /></label>
        {err && <p className="error">{err}</p>}
        <button className="primary" type="submit">Save password</button>
      </form>
    </Card>
  )
}
