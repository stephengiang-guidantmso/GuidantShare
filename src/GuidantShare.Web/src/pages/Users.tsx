import { useEffect, useState } from 'react'
import { api, fmtDate } from '../api'
import { useMe } from '../App'

export default function Users() {
  const { me } = useMe()
  const [d, setD] = useState<any>(null); const [email, setEmail] = useState(''); const [role, setRole] = useState('User')
  const [msg, setMsg] = useState<{ ok: boolean; text: string; link?: string } | null>(null)
  const [pwFor, setPwFor] = useState<{ id: string; email: string } | null>(null); const [newPw, setNewPw] = useState('')
  const load = () => api('/api/users').then(setD)
  useEffect(() => { load() }, [])
  const show = (r: any, verb: string) => setMsg(r.emailStatus === 'Failed'
    ? { ok: false, text: `Email failed: ${r.emailError}`, link: r.inviteLink }
    : { ok: true, text: `${verb} ${r.email}.`, link: r.inviteLink })
  const run = async (fn: () => Promise<any>) => { try { await fn() } catch (x: any) { setMsg({ ok: false, text: x.message }) } load() }

  if (!d) return <p className="muted">Loading…</p>
  return (
    <>
      <h1>Users</h1>
      <form className="row card" onSubmit={e => { e.preventDefault(); run(async () => { const r = await api('/api/invites', { method: 'POST', body: { email, role } }); show(r, 'Invite sent to'); setEmail('') }) }}>
        <input type="email" placeholder="Email to invite" value={email} onChange={e => setEmail(e.target.value)} required />
        <select value={role} onChange={e => setRole(e.target.value)}><option>User</option><option>SuperAdmin</option></select>
        <button className="primary">Invite</button>
      </form>
      {pwFor && (
        <div className="modal"><form className="card" onSubmit={e => { e.preventDefault(); run(async () => { await api(`/api/users/${pwFor.id}/password`, { method: 'POST', body: { password: newPw } }); setMsg({ ok: true, text: `Password changed for ${pwFor.email}. They are signed out everywhere. Tell them the new password.` }); setPwFor(null); setNewPw('') }) }}>
          <h2>Change password for {pwFor.email}</h2>
          <label>New password (at least 8 characters, not a common password)<input type="password" autoComplete="new-password" value={newPw} onChange={e => setNewPw(e.target.value)} autoFocus required /></label>
          <div className="row"><button type="button" onClick={() => { setPwFor(null); setNewPw('') }}>Cancel</button><button className="primary">Change password</button></div>
        </form></div>
      )}
      {msg && <div className={`banner ${msg.ok ? 'ok' : 'warn'}`}>{msg.text}
        {msg.link && <div>Link (shown once, copy it if the email doesn't arrive): <input readOnly value={msg.link} onFocus={e => e.target.select()} style={{ width: '60%' }} /> <button onClick={() => navigator.clipboard.writeText(msg.link!)}>Copy</button></div>}</div>}

      {d.invites.length > 0 && <>
        <h2>Pending invites</h2>
        <table><thead><tr><th>Email</th><th>Role</th><th>Email status</th><th>Expires</th><th></th></tr></thead><tbody>
          {d.invites.map((i: any) => <tr key={i.id}><td>{i.email}</td><td>{i.role}</td>
            <td>{i.emailStatus === 'Failed' ? <span className="error">Email failed: {i.lastEmailError}</span> : i.emailStatus}</td>
            <td>{i.expired ? <span className="error">Expired</span> : fmtDate(i.expiresUtc)}</td>
            <td><button onClick={() => run(async () => show(await api(`/api/invites/${i.id}/resend`, { method: 'POST' }), 'Invite re-sent to'))}>Resend</button>{' '}
              <button onClick={() => run(() => api(`/api/invites/${i.id}/revoke`, { method: 'POST' }))}>Revoke</button></td></tr>)}
        </tbody></table></>}

      <h2>Users</h2>
      <table><thead><tr><th>Email</th><th>Role</th><th>Status</th><th>Last login</th><th></th></tr></thead><tbody>
        {d.users.map((u: any) => <tr key={u.id}><td>{u.email}</td>
          <td><select value={u.role} disabled={u.id === me?.id} onChange={e => run(() => api(`/api/users/${u.id}`, { method: 'PATCH', body: { role: e.target.value } }))}><option>User</option><option>SuperAdmin</option></select></td>
          <td>{u.isActive ? 'Active' : 'Deactivated'}</td><td>{fmtDate(u.lastLoginUtc)}</td>
          <td>{u.id !== me?.id && <button onClick={() => run(() => api(`/api/users/${u.id}`, { method: 'PATCH', body: { isActive: !u.isActive } }))}>{u.isActive ? 'Deactivate' : 'Reactivate'}</button>}{' '}
            <button onClick={() => setPwFor({ id: u.id, email: u.email })}>Change password</button>{' '}
            <button onClick={() => run(async () => { const r = await api(`/api/users/${u.id}/reset-password`, { method: 'POST' }); setMsg({ ok: r.ok, text: r.ok ? `Reset link sent to ${u.email}.` : `Email failed: ${r.error}`, link: r.resetLink }) })}>Send reset link</button>
            {u.id !== me?.id && <>{' '}<button onClick={() => { if (confirm(`Remove ${u.email}? They will no longer be able to sign in. Their upload history is kept.`)) run(async () => { await api(`/api/users/${u.id}`, { method: 'DELETE' }); setMsg({ ok: true, text: `${u.email} was removed.` }) }) }}>Remove</button></>}</td></tr>)}
      </tbody></table>
    </>
  )
}
