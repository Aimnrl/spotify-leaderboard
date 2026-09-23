import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, type GroupSummary } from '../api'

export default function Groups() {
  const [groups, setGroups] = useState<GroupSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()

  useEffect(() => { api.groups().then(setGroups).catch(e => setError(e.message)) }, [])

  const submit = (action: () => Promise<{ id: number }>) => async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    setError(null)
    try {
      navigate(`/groups/${(await action()).id}`)
    } catch (err) {
      setError((err as Error).message)
    }
  }
  const field = (e: FormEvent<HTMLFormElement>, name: string) =>
    (e.currentTarget.elements.namedItem(name) as HTMLInputElement).value

  return (
    <>
      <h1>Your groups</h1>
      {error && <p className="error">{error}</p>}
      {groups === null ? <p className="muted">Loading…</p> : groups.length === 0 ? (
        <p className="muted">You're not in any groups yet. Create one and share the invite code, or join a friend's.</p>
      ) : (
        <div className="cards">
          {groups.map(g => (
            <Link key={g.id} to={`/groups/${g.id}`} className="card">
              <strong>{g.name}</strong>
              <span className="muted small">{g.memberCount} {g.memberCount === 1 ? 'member' : 'members'}</span>
            </Link>
          ))}
        </div>
      )}

      <div className="forms">
        <form onSubmit={e => submit(() => api.createGroup(field(e, 'name')))(e)}>
          <h2>Create a group</h2>
          <input name="name" placeholder="Group name" maxLength={50} required />
          <button className="btn primary">Create</button>
        </form>
        <form onSubmit={e => submit(() => api.joinGroup(field(e, 'code')))(e)}>
          <h2>Join with a code</h2>
          <input name="code" placeholder="e.g. K7QM2XPA" maxLength={8} required className="code" />
          <button className="btn primary">Join</button>
        </form>
      </div>
    </>
  )
}
