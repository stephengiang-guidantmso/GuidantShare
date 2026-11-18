import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, fmtDate } from '../api'
import CatalogEditor from './CatalogEditor'

export default function ShareFileAdmin() {
  const [sp] = useSearchParams()
  const [st, setSt] = useState<any>(null); const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [f, setF] = useState({ subdomain: '', clientId: '', clientSecret: '', redirectUri: '' })
  const [code, setCode] = useState('')
  const load = () => api('/api/sharefile/status').then(s => { window.dispatchEvent(new Event('sf-status-changed')); setSt(s); setF(x => ({ ...x, subdomain: s.subdomain ?? '', clientId: s.clientId ?? '', redirectUri: s.redirectUri ?? `${location.origin}/api/sharefile/callback` })) })
  useEffect(() => { load() }, [])
  useEffect(() => {
    const r = sp.get('result'); if (r) setMsg(r === 'connected' ? { ok: true, text: 'ShareFile connected for the whole team.' } : { ok: false, text: 'ShareFile connection failed. Try again.' })
  }, [sp])
  const run = async (fn: () => Promise<any>) => { setMsg(null); try { await fn() } catch (x: any) { setMsg({ ok: false, text: x.message }) } load() }
  if (!st) return <p className="muted">Loading…</p>
  const ownCallback = (st.redirectUri ?? '').endsWith('/api/sharefile/callback')

  return (
    <>
      <h1>Settings</h1>
      <h2>ShareFile connection</h2>
      <div className={`card status ${st.connected ? 'good' : 'bad'}`}>
        {st.connected ? <>✅ Connected as the owner's ShareFile account on <b>{st.subdomain}.sharefile.com</b>. Connected by {st.connectedBy} on {fmtDate(st.connectedUtc)}. This connection is shared by everyone on the team.</>
          : <>⚠️ Not connected. {st.configured ? 'Click “Connect to ShareFile” and sign in with the owner account.' : 'Save the ShareFile app settings below first.'}</>}
        {st.error && <p className="error">{st.error}</p>}
      </div>
      {msg && <div className={`banner ${msg.ok ? 'ok' : 'warn'}`}>{msg.text}</div>}

      <h2>1. ShareFile app settings</h2>
      <form className="card form" onSubmit={e => { e.preventDefault(); run(async () => { await api('/api/sharefile/settings', { method: 'POST', body: f }); setF(x => ({ ...x, clientSecret: '' })); setMsg({ ok: true, text: 'Settings saved (encrypted).' }) }) }}>
        <label>Subdomain (the part before .sharefile.com)<input name="sf-subdomain" autoComplete="off" value={f.subdomain} onChange={e => setF({ ...f, subdomain: e.target.value })} required /></label>
        <label>Client ID<input name="sf-client-id" autoComplete="off" value={f.clientId} onChange={e => setF({ ...f, clientId: e.target.value })} required /></label>
        <label>Client secret {st.configured && <span className="muted">(saved — leave blank to keep)</span>}<input type="password" name="sf-client-secret" autoComplete="new-password" value={f.clientSecret} onChange={e => setF({ ...f, clientSecret: e.target.value })} /></label>
        <label>Redirect URI registered on the ShareFile app<input value={f.redirectUri} onChange={e => setF({ ...f, redirectUri: e.target.value })} /></label>
        <p className="muted">Use <code>{location.origin}/api/sharefile/callback</code> for one-click connect, or <code>https://secure.sharefile.com/oauth/oauthcomplete.aspx</code> and paste the code below.</p>
        <button className="primary">Save settings</button>
      </form>

      <h2>2. Connect</h2>
      <div className="card form">
        <div className="row">
          <button className="primary" disabled={!st.configured} onClick={() => run(async () => { const w = window.open('about:blank', '_blank'); try { const r = await api('/api/sharefile/connect', { method: 'POST' }); if (w) w.location.href = r.url; else window.open(r.url, '_blank') } catch (x) { w?.close(); throw x } })}>
            {st.connected ? 'Reconnect to ShareFile' : 'Connect to ShareFile'}</button>
          {st.connected && <button onClick={() => run(() => api('/api/sharefile/disconnect', { method: 'POST' }))}>Disconnect</button>}
        </div>
        {st.configured && !ownCallback && <div className="connect-paste">
          <p className="muted">After approving, ShareFile shows a page with a code (or an address containing <code>code=</code>). Paste it here:</p>
          <div className="row">
            <input placeholder="Paste code or full address" value={code} onChange={e => setCode(e.target.value)} />
            <button onClick={() => run(async () => { await api('/api/sharefile/exchange', { method: 'POST', body: { code } }); setCode(''); setMsg({ ok: true, text: 'ShareFile connected for the whole team.' }) })}>Finish connecting</button>
          </div></div>}
      </div>

      <h2>3. Health plans and folders</h2>
      <p className="muted">Everything users see on the home page comes from here. Each folder needs its ShareFile link. Usernames are login names only; passwords are never stored or shown.</p>
      <CatalogEditor connected={st.connected} />
    </>
  )
}
