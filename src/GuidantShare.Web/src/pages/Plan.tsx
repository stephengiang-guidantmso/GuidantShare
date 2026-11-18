import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'

export default function Plan() {
  const { planId, direction } = useParams()
  const nav = useNavigate()
  const [plans, setPlans] = useState<any[]>([]); const [entries, setEntries] = useState<any[] | null>(null)
  useEffect(() => { api('/api/plans').then(setPlans) }, [])
  useEffect(() => {
    if (!direction) { setEntries(null); return }
    api(`/api/plans/${planId}/entries?direction=${direction}`).then(list => { if (list.length === 1) nav(`/entries/${list[0].id}`, { replace: true }); else setEntries(list) })
  }, [planId, direction])
  const plan = plans.find(p => String(p.id) === planId)
  if (!plan) return <p className="muted">Loading…</p>
  return (
    <>
      <p><Link to="/">← All plans</Link></p>
      <h1>{plan.name}</h1>
      {!direction ? (
        <div className="grid two">
          <Link to={`/plans/${planId}/Download`} className="card big">⬇ Download<small>Files received from {plan.name}</small></Link>
          {plan.hasUpload
            ? <Link to={`/plans/${planId}/Upload`} className="card big">⬆ Upload<small>Send files to {plan.name}</small></Link>
            : <div className="card big disabled" title="No upload available for this plan">⬆ Upload<small>No upload available for this plan</small></div>}
        </div>
      ) : (
        <>
          <h2>{direction === 'Upload' ? 'Where do you want to upload?' : 'Which folder?'}</h2>
          <div className="grid">{entries?.map(e => <Link key={e.id} className="card plan" to={`/entries/${e.id}`}>{e.label}</Link>)}</div>
        </>
      )}
    </>
  )
}
