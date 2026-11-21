import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError, fmtDate, fmtSize } from '../api'

type Up = { file: File; state: 'queued' | 'uploading' | 'done' | 'error' | 'exists'; link?: string; error?: string }

export default function Files() {
  const { planKey, folderKey } = useParams()
  const base = `/api/plans/${planKey}/folders/${folderKey}`
  const [data, setData] = useState<any>(null); const [err, setErr] = useState('')
  const [trail, setTrail] = useState<{ id: string; name: string }[]>([])
  const [search, setSearch] = useState(''); const [q, setQ] = useState(''); const [page, setPage] = useState(1)
  const [ups, setUps] = useState<Up[]>([]); const [confirming, setConfirming] = useState(false); const [drag, setDrag] = useState(false)
  const input = useRef<HTMLInputElement>(null)

  // The same component serves every folder route, so start clean when the plan or folder in the address changes
  useEffect(() => { setTrail([]); setPage(1); setSearch(''); setQ(''); setData(null); setUps([]); setErr('') }, [planKey, folderKey])
  const folder = trail.length ? trail[trail.length - 1].id : ''
  const load = useCallback((refresh = false) => {
    setErr('')
    api(`${base}/files?search=${encodeURIComponent(q)}&page=${page}&folder=${folder}${refresh ? '&refresh=true' : ''}`).then(setData).catch(x => setErr(x.message))
  }, [base, q, page, folder])
  useEffect(() => { load() }, [load])
  useEffect(() => { const t = setTimeout(() => { setQ(search); setPage(1) }, 300); return () => clearTimeout(t) }, [search])

  if (!data) return <><p><Link to={`/plans/${planKey}`}>← Back</Link></p><p className={err ? 'error' : 'muted'}>{err || 'Loading…'}</p></>
  const upload = data.direction === 'Upload'
  const loc = data.location.shareFilePath
  const set = (i: number, p: Partial<Up>) => setUps(u => u.map((x, k) => k === i ? { ...x, ...p } : x))

  const add = (files: FileList | File[]) => {
    const list = Array.from(files).map(f => ({ file: f, state: 'queued' as const }))
    const bad = list.filter(x => x.file.size === 0)
    if (bad.length) setErr('Empty files were skipped.')
    setUps(list.filter(x => x.file.size > 0)); if (list.length > bad.length) setConfirming(true)
  }

  const send = async () => {
    setConfirming(false)
    for (let i = 0; i < ups.length; i++) {
      let overwrite = false
      try {
        const ex = await api(`${base}/exists?name=${encodeURIComponent(ups[i].file.name)}&folder=${folder}`)
        if (ex.exists) {
          if (!confirm(`"${ups[i].file.name}" already exists in this folder. Overwrite it?`)) { set(i, { state: 'error', error: 'Skipped (file exists)' }); continue }
          overwrite = true
        }
        set(i, { state: 'uploading' })
        const form = new FormData(); form.append('file', ups[i].file); form.append('overwrite', String(overwrite)); form.append('folder', folder)
        const r = await api(`${base}/uploads`, { method: 'POST', form })
        set(i, { state: 'done', link: r.link })
      } catch (x: any) { set(i, { state: 'error', error: x instanceof ApiError ? x.message : 'Upload failed' }) }
    }
    load(true)
  }

  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <>
      <p><Link to={`/plans/${planKey}`}>← {data.plan}</Link></p>
      <h1>{data.plan}{data.label !== data.plan ? ` — ${data.label}` : ''}: {upload ? 'Upload' : 'Download'}</h1>
      {data.stale && <div className="banner warn">The list of health plans may be out of date right now.</div>}
      <div className="card banner-info">
        <div><b>ShareFile folder:</b> {loc}{data.folderLink && <> · <a href={data.folderLink} target="_blank" rel="noreferrer">Open in ShareFile</a></>}</div>
      </div>

      {upload && (
        <div className={`drop ${drag ? 'over' : ''}`} onDragOver={ev => { ev.preventDefault(); setDrag(true) }} onDragLeave={() => setDrag(false)}
          onDrop={ev => { ev.preventDefault(); setDrag(false); add(ev.dataTransfer.files) }}>
          Drag and drop files here, or <button className="link" onClick={() => input.current?.click()}>browse</button>
          <input ref={input} type="file" multiple hidden onChange={ev => { if (ev.target.files) add(ev.target.files); ev.target.value = '' }} />
        </div>
      )}
      {ups.length > 0 && (
        <ul className="uplist">{ups.map((u, i) => (
          <li key={i}>{u.file.name} <span className="muted">({fmtSize(u.file.size)})</span> — {u.state === 'done' ? <>saved to ShareFile · <a href={u.link} target="_blank" rel="noreferrer">open</a></> : u.state === 'error' ? <span className="error">{u.error} <button className="link" onClick={() => { setConfirming(false); setUps([u]); setTimeout(send, 0) }}>Retry</button></span> : u.state}</li>
        ))}</ul>
      )}
      {confirming && (
        <div className="modal"><div className="card">
          <h2>Confirm upload</h2>
          <p>These {ups.length} file{ups.length > 1 ? 's' : ''} will be saved to ShareFile:<br /><b>{loc}</b></p>
          <p>and will then be sent on to <b>{data.plan}</b>.</p>
          <ul>{ups.map((u, i) => <li key={i}>{u.file.name} ({fmtSize(u.file.size)})</li>)}</ul>
          <div className="row"><button onClick={() => { setConfirming(false); setUps([]) }}>Cancel</button><button className="primary" onClick={send}>Upload</button></div>
        </div></div>
      )}

      <div className="row bar">
        <input placeholder="Search file name…" value={search} onChange={e => setSearch(e.target.value)} />
        <button onClick={() => load(true)}>Refresh</button>
        <span className="muted">Last refreshed {fmtDate(data.refreshedUtc)}</span>
      </div>
      {trail.length > 0 && <p className="crumbs"><button className="link" onClick={() => { setTrail([]); setPage(1) }}>{data.label}</button>{trail.map((t, i) => <span key={t.id}> / <button className="link" onClick={() => { setTrail(trail.slice(0, i + 1)); setPage(1) }}>{t.name}</button></span>)}</p>}
      {err && <p className="error">{err}</p>}
      <table><thead><tr><th>Name</th><th>Size</th><th>Modified</th></tr></thead>
        <tbody>{data.folders?.map((f: any) => <tr key={f.id}><td colSpan={3}>📁 <button className="link" onClick={() => { setTrail([...trail, { id: f.id, name: f.name }]); setPage(1) }}>{f.name}</button></td></tr>)}{data.items.map((i: any) => <tr key={i.id}><td>{i.name}</td><td>{fmtSize(i.size)}</td><td>{fmtDate(i.modifiedUtc)}</td></tr>)}
          {data.items.length === 0 && !data.folders?.length && <tr><td colSpan={3} className="muted">No files found.</td></tr>}</tbody></table>
      <div className="row"><button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page} of {pages} · {data.total} files</span><button disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button></div>
    </>
  )
}
