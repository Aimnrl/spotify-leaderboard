import { useCallback, useEffect, useState } from 'react'
import { BrowserRouter, Link, Route, Routes } from 'react-router-dom'
import { api, Unauthorized, type Me } from './api'
import { Avatar } from './components'
import Groups from './pages/Groups'
import GroupPage from './pages/GroupPage'
import Import from './pages/Import'

export default function App() {
  // undefined = still loading, null = signed out
  const [me, setMe] = useState<Me | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)

  const loadMe = useCallback(() => {
    api.me().then(setMe).catch(e => {
      if (e instanceof Unauthorized) setMe(null)
      else setError(e.message)
    })
  }, [])
  useEffect(loadMe, [loadMe])

  // Streaks are counted in the user's own days, so keep the server's copy of their time zone current.
  useEffect(() => {
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone
    if (me && tz && tz !== me.timeZone) api.setTimeZone(tz).catch(() => {})
  }, [me])

  if (error) return <div className="center error">{error}</div>
  if (me === undefined) return <div className="center muted">Loading…</div>
  if (me === null) return <Login />

  return (
    <BrowserRouter>
      <Header me={me} onChange={loadMe} />
      {me.needsReauth && (
        <div className="banner">
          Spotify access expired, so your plays aren't being tracked. <a href="/auth/login">Sign in again</a>
        </div>
      )}
      <main>
        <Routes>
          <Route path="/" element={<Groups />} />
          <Route path="/groups/:id" element={<GroupPage me={me} />} />
          <Route path="/import" element={<Import onImported={loadMe} />} />
        </Routes>
      </main>
    </BrowserRouter>
  )
}

function Login() {
  const failed = new URLSearchParams(location.search).has('login')
  return (
    <div className="login">
      <h1>Leaderboard</h1>
      <p className="muted">See who in your crew listens the most, finds the most artists, and never misses a day.</p>
      <a className="btn primary big" href="/auth/login">Continue with Spotify</a>
      {failed && <p className="error">Sign-in didn't complete. Try again.</p>}
    </div>
  )
}

function Header({ me, onChange }: { me: Me; onChange: () => void }) {
  const [status, setStatus] = useState<string | null>(null)

  const sync = async () => {
    setStatus('Syncing…')
    try {
      const { added } = await api.sync()
      setStatus(added ? `+${added} new plays` : 'Up to date')
      onChange()
    } catch (e) {
      setStatus((e as Error).message)
    }
  }

  const logout = async () => {
    await api.logout()
    location.href = '/'
  }

  return (
    <header>
      <Link to="/" className="brand">Leaderboard</Link>
      <nav>
        <span className="muted small" title={me.lastPolledAt ? `Last synced ${new Date(me.lastPolledAt).toLocaleString()}` : undefined}>
          {status ?? `${me.playCount.toLocaleString()} plays`}
        </span>
        <button className="btn" onClick={sync} disabled={status === 'Syncing…'}>Sync now</button>
        <Link className="btn" to="/import">Import history</Link>
        <span className="me"><Avatar name={me.displayName} url={me.avatarUrl} />{me.displayName}</span>
        <button className="btn ghost" onClick={logout}>Sign out</button>
      </nav>
    </header>
  )
}
