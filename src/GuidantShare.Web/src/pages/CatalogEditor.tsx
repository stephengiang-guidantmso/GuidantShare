import { useEffect, useState } from 'react'
import { api } from '../api'

type Form = {
  id?: number; planId: number; direction: string; label: string; shareFileLink: string; shareFilePath: string
  remoteKind: string; remoteHost: string; remotePort: string; remotePath: string; portalName: string; remoteNote: string; username: string
}
const blank = (planId: number): Form => ({ planId, direction: 'Download', label: '', shareFileLink: '', shareFilePath: '', remoteKind: 'Sftp', remoteHost: '', remotePort: '22', remotePath: '/', portalName: '', remoteNote: '', username: '' })

export default function CatalogEditor({ connected }: { connected: boolean }) {
  const [plans, setPlans] = useState<any[] | null>(null)
  const [err, setErr] = useState('')
  const [newPlan, setNewPlan] = useState('')
  const [form, setForm] = useState<Form | null>(null)
  const [renaming, setRenaming] = useState<{ id: number; name: string } | null>(null)
  const load = () => api('/api/catalog').then(setPlans).catch(x => setErr(x.message))
  useEffect(() => { load() }, [])
  const run = async (fn: () => Promise<any>) => { setErr(''); try { await fn(); await load() } catch (x: any) { setErr(x.message) } }

  const submit = (e: React.FormEvent) => {
    e.preventDefault(); if (!form) return
    const body = { ...form, remotePort: form.remoteKind === 'Sftp' && form.remotePort ? Number(form.remotePort) : null }
    run(async () => {
      if (form.id) await api(`/api/catalog/entries/${form.id}`, { method: 'PUT', body }); else await api('/api/catalog/entries', { method: 'POST', body })
      setForm(null)
    })
  }
  const edit = (planId: number, e: any) => setForm({
    id: e.entryId, planId, direction: e.direction, label: e.label, shareFileLink: e.shareFileLink ?? '', shareFilePath: e.shareFilePath ?? '',
    remoteKind: e.remoteKind, remoteHost: e.remoteHost ?? '', remotePort: e.remotePort ? String(e.remotePort) : '22', remotePath: e.remotePath ?? '',
    portalName: e.portalName ?? '', remoteNote: e.remoteNote ?? '', username: e.username ?? ''
  })
  const set = (k: keyof Form) => (ev: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm({ ...form!, [k]: ev.target.value })

  const planName = form ? plans?.find(p => p.planId === form.planId)?.name ?? '' : ''
  const defaultPath = form?.direction === 'Upload' ? `Shared Folders/SFTP Uploads/${planName}` : `Shared Folders/SFTP Drop ${planName}`
  if (!plans) return err ? <p className="error">{err}</p> : <p className="muted">Loading…</p>
  return (
    <>
      {err && <div className="banner warn">{err}</div>}
      {!connected && <div className="banner warn">Connect ShareFile first: every folder link is checked against ShareFile when you save.</div>}

      <form className="card form" onSubmit={e => { e.preventDefault(); run(async () => { await api('/api/catalog/plans', { method: 'POST', body: { name: newPlan } }); setNewPlan('') }) }}>
        <label>Add a health plan<input value={newPlan} onChange={e => setNewPlan(e.target.value)} placeholder="e.g. CAQH" maxLength={100} required /></label>
        <button className="primary">Add health plan</button>
      </form>

      {plans.map(p => (
        <div key={p.planId} className="card">
          <div className="row" style={{ justifyContent: 'space-between' }}>
            {renaming && renaming.id === p.planId
              ? <span className="row"><input value={renaming.name} onChange={e => setRenaming({ id: p.planId, name: e.target.value })} />
                <button className="primary" onClick={() => run(async () => { await api(`/api/catalog/plans/${p.planId}`, { method: 'PATCH', body: { name: renaming!.name } }); setRenaming(null) })}>Save</button>
                <button onClick={() => setRenaming(null)}>Cancel</button></span>
              : <h3 style={{ margin: 0 }}>{p.name}</h3>}
            <span className="row">
              <button onClick={() => setForm(blank(p.planId))} disabled={!connected}>Add folder</button>
              <button onClick={() => setRenaming({ id: p.planId, name: p.name })}>Rename</button>
              <button onClick={() => { if (confirm(`Remove “${p.name}” and all of its folders from the app? Nothing is deleted in ShareFile.`)) run(() => api(`/api/catalog/plans/${p.planId}`, { method: 'DELETE' })) }}>Remove</button>
            </span>
          </div>
          {p.entries.length === 0 ? <p className="muted">No folders yet.</p> : (
            <table className="matrix catalog">
              <colgroup><col style={{ width: '14%' }} /><col style={{ width: '12%' }} /><col style={{ width: '24%' }} /><col style={{ width: '16%' }} /><col style={{ width: '22%' }} /><col style={{ width: '12%' }} /></colgroup>
              <thead><tr><th>Flow</th><th>Label</th><th>SFTP / portal</th><th>Login username</th><th>ShareFile folder</th><th></th></tr></thead>
              <tbody>{p.entries.map((e: any) => (
                <tr key={e.entryId}>
                  <td className="flow">{e.direction === 'Download' ? 'SFTP ➜ ShareFile' : 'ShareFile ➜ SFTP'}</td>
                  <td>{e.label}</td>
                  <td><span className="tag tag-remote">{e.remoteKind === 'Portal' ? 'Portal' : 'SFTP'}</span> {e.remote}</td>
                  <td>{e.username || <span className="muted">—</span>}</td>
                  <td>{e.shareFileLink ? <a className="tag tag-sf" href={e.shareFileLink} target="_blank" rel="noreferrer">{e.shareFilePath}</a> : <span className="tag tag-off">{e.shareFilePath}</span>}</td>
                  <td className="actions"><span className="row">
                    <button onClick={() => edit(p.planId, e)} disabled={!connected}>Edit</button>
                    <button onClick={() => { if (confirm(`Remove “${e.label}” (${e.direction}) from ${p.name}?`)) run(() => api(`/api/catalog/entries/${e.entryId}`, { method: 'DELETE' })) }}>Remove</button>
                  </span></td>
                </tr>))}</tbody>
            </table>)}
        </div>))}

      {form && (
        <div className="modal" onClick={() => setForm(null)}>
          <form className="card form wide" onClick={e => e.stopPropagation()} onSubmit={submit}>
            <h3>{form.id ? 'Edit folder' : 'Add folder'} — {plans.find(p => p.planId === form.planId)?.name}</h3>
            <label>ShareFile folder link (required)
              <input value={form.shareFileLink} onChange={set('shareFileLink')} placeholder="https://yourco.sharefile.com/home/shared/fo…" required /></label>
            <p className="muted">Open the folder in ShareFile and copy the address. It is checked against ShareFile when you save.</p>
            <label>Shown to users as (optional — leave blank to use the default below)
              <input value={form.shareFilePath} onChange={set('shareFilePath')} placeholder={defaultPath} /></label>
            <div className="fields">
              <label>Direction<select value={form.direction} onChange={set('direction')}>
                <option value="Download">Download (partner ➜ ShareFile)</option><option value="Upload">Upload (ShareFile ➜ partner)</option></select></label>
              <label>Label (blank = health plan name)<input value={form.label} onChange={set('label')} placeholder={planName} maxLength={100} /></label>
            </div>
            <label>Connects through<select value={form.remoteKind} onChange={set('remoteKind')}><option value="Sftp">SFTP</option><option value="Portal">Web portal</option></select></label>
            {form.remoteKind === 'Sftp'
              ? <div className="fields hostport">
                <label>SFTP host<input value={form.remoteHost} onChange={set('remoteHost')} placeholder="sftp.example.com" required /></label>
                <label>Port<input value={form.remotePort} onChange={set('remotePort')} inputMode="numeric" /></label></div>
              : <label>Portal name<input value={form.portalName} onChange={set('portalName')} placeholder="Aetna SecureTransport (st1.aetna.com)" required /></label>}
            <label>Remote folder path<input value={form.remotePath} onChange={set('remotePath')} placeholder="/Incoming" /></label>
            <label>Login username (never the password)<input value={form.username} onChange={set('username')} autoComplete="off" name="entry-username" /></label>
            <label>Note shown to users (optional)<input value={form.remoteNote} onChange={set('remoteNote')} /></label>
            <div className="row"><button type="button" onClick={() => setForm(null)}>Cancel</button><button className="primary">Save folder</button></div>
          </form>
        </div>)}
    </>
  )
}
