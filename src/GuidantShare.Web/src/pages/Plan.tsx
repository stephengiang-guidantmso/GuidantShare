import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'

type Folder = { key: string; label: string; direction: 'Download' | 'Upload'; shareFilePath: string }

export default function Plan() {
  const { planKey, direction } = useParams()
  const nav = useNavigate()
  const [plan, setPlan] = useState<{ name: string; folders: Folder[] } | null>(null)
  const [err, setErr] = useState('')
  useEffect(() => { api(`/api/plans/${planKey}`).then(setPlan).catch(x => setErr(x.message)) }, [planKey])

  const dir = direction === 'upload' ? 'Upload' : direction === 'download' ? 'Download' : null
  const inDirection = plan?.folders.filter(f => f.direction === dir) ?? []
  useEffect(() => {
    // One folder in this direction: go straight to its files
    if (dir && inDirection.length === 1) nav(`/plans/${planKey}/folders/${inDirection[0].key}`, { replace: true })
  }, [dir, inDirection.length, planKey])

  if (err) return <><p><Link to="/">← All plans</Link></p><p className="error">{err}</p></>
  if (!plan) return <p className="muted">Loading…</p>
  const hasDownload = plan.folders.some(f => f.direction === 'Download')
  const hasUpload = plan.folders.some(f => f.direction === 'Upload')
  return (
    <>
      <p><Link to="/">← All plans</Link></p>
      <h1>{plan.name}</h1>
      {!dir ? (
        <div className="grid two">
          {hasDownload
            ? <Link to={`/plans/${planKey}/download`} className="card big">⬇ Download<small>Files received from {plan.name}</small></Link>
            : <div className="card big disabled" title="No download available for this plan">⬇ Download<small>No download available for this plan</small></div>}
          {hasUpload
            ? <Link to={`/plans/${planKey}/upload`} className="card big">⬆ Upload<small>Send files to {plan.name}</small></Link>
            : <div className="card big disabled" title="No upload available for this plan">⬆ Upload<small>No upload available for this plan</small></div>}
        </div>
      ) : (
        <>
          <h2>{dir === 'Upload' ? 'Where do you want to upload?' : 'Which folder?'}</h2>
          {inDirection.length === 0 && <p className="muted">Nothing is available in this direction.</p>}
          <div className="grid">{inDirection.map(f => <Link key={f.key} className="card plan" to={`/plans/${planKey}/folders/${f.key}`}>{f.label}</Link>)}</div>
        </>
      )}
    </>
  )
}
