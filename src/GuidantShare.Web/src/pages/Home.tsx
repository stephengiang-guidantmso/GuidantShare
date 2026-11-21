import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'

export default function Home() {
  const [data, setData] = useState<{ plans: any[]; stale: boolean } | null>(null)
  const [err, setErr] = useState('')
  useEffect(() => { api('/api/plans').then(setData).catch(x => setErr(x.message)) }, [])
  if (err) return <p className="error">{err}</p>
  if (!data) return <p className="muted">Loading…</p>
  return (
    <div className="home">
      <h1>Health plans</h1>
      {data.stale && <div className="banner warn">The list of health plans may be out of date right now. Files in ShareFile are still available.</div>}
      {data.plans.length === 0 && <p className="muted">No health plans are available yet.</p>}
      <div className="grid">{data.plans.map(p => <Link key={p.key} to={`/plans/${p.key}`} className="card plan">{p.name}</Link>)}</div>
    </div>
  )
}
