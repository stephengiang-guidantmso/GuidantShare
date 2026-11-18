import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'

export default function Home() {
  const [plans, setPlans] = useState<any[] | null>(null)
  useEffect(() => { api('/api/plans').then(setPlans) }, [])
  if (!plans) return <p className="muted">Loading…</p>
  return (
    <div className="home">
      <h1>Health plans</h1>
      <div className="grid">{plans.map(p => <Link key={p.id} to={`/plans/${p.id}`} className="card plan">{p.name}</Link>)}</div>
    </div>
  )
}
