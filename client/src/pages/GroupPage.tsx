import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type ArtistRow, type Entry, type GroupDetail, type Me, type Metric, type Period } from '../api'
import { Avatar, minutes, RankTable } from '../components'

type Tab = Metric | 'superfans'

const tabs: { id: Tab; label: string }[] = [
  { id: 'minutes', label: 'Minutes' },
  { id: 'diversity', label: 'Artist diversity' },
  { id: 'streak', label: 'Streaks' },
  { id: 'superfans', label: 'Superfans' },
]
const periods: { id: Period; label: string }[] = [
  { id: 'week', label: '7 days' },
  { id: 'month', label: '30 days' },
  { id: 'year', label: '12 months' },
  { id: 'all', label: 'All time' },
]

export default function GroupPage({ me }: { me: Me }) {
  const id = Number(useParams().id)
  const navigate = useNavigate()
  const [group, setGroup] = useState<GroupDetail | null>(null)
  const [tab, setTab] = useState<Tab>('minutes')
  const [period, setPeriod] = useState<Period>('week')
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => { api.group(id).then(setGroup).catch(e => setError(e.message)) }, [id])

  if (error) return <p className="error">{error}</p>
  if (!group) return <p className="muted">Loading…</p>

  const copy = async () => {
    await navigator.clipboard.writeText(group.inviteCode)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }
  const leave = async () => {
    if (!confirm(`Leave ${group.name}?`)) return
    await api.leaveGroup(id)
    navigate('/')
  }

  return (
    <>
      <div className="group-head">
        <div>
          <h1>{group.name}</h1>
          <div className="members">
            {group.members.map(m => <Avatar key={m.userId} name={m.displayName} url={m.avatarUrl} />)}
          </div>
        </div>
        <div className="invite">
          <span className="muted small">Invite code</span>
          <button className="btn code" onClick={copy} title="Copy">{copied ? 'Copied!' : group.inviteCode}</button>
          <button className="btn ghost small" onClick={leave}>Leave</button>
        </div>
      </div>

      <div className="toolbar">
        <div className="tabs" role="tablist">
          {tabs.map(t => (
            <button key={t.id} role="tab" aria-selected={tab === t.id} onClick={() => setTab(t.id)}>{t.label}</button>
          ))}
        </div>
        {tab !== 'streak' && (
          <select value={period} onChange={e => setPeriod(e.target.value as Period)} aria-label="Period">
            {periods.map(p => <option key={p.id} value={p.id}>{p.label}</option>)}
          </select>
        )}
      </div>

      {tab === 'superfans'
        ? <Superfans groupId={id} period={period} meId={me.id} />
        : <Leaderboard groupId={id} metric={tab} period={period} meId={me.id} />}
    </>
  )
}

const formats: Record<Metric, (e: Entry) => string> = {
  minutes: e => minutes(e.value),
  diversity: e => `${e.value} ${e.value === 1 ? 'artist' : 'artists'}`,
  streak: e => `${e.value} ${e.value === 1 ? 'day' : 'days'} · best ${e.best}`,
}

function Leaderboard({ groupId, metric, period, meId }: { groupId: number; metric: Metric; period: Period; meId: number }) {
  const [entries, setEntries] = useState<Entry[] | Error | null>(null)
  useEffect(() => {
    setEntries(null)
    api.leaderboard(groupId, metric, period).then(setEntries, setEntries)
  }, [groupId, metric, period])

  if (!entries) return <p className="muted">Loading…</p>
  if (entries instanceof Error) return <p className="error">{entries.message}</p>
  return (
    <>
      {metric === 'streak' && <p className="muted small">Consecutive days with at least one play, in each person's own time zone.</p>}
      <RankTable entries={entries} meId={meId} format={formats[metric]} empty="No plays in this period yet." />
    </>
  )
}

function Superfans({ groupId, period, meId }: { groupId: number; period: Period; meId: number }) {
  const [artists, setArtists] = useState<ArtistRow[] | Error | null>(null)
  const [open, setOpen] = useState<string | null>(null)
  const [fans, setFans] = useState<Entry[] | Error | null>(null)

  useEffect(() => {
    setArtists(null)
    setOpen(null)
    api.artists(groupId, period).then(setArtists, setArtists)
  }, [groupId, period])

  useEffect(() => {
    setFans(null)
    if (open) api.superfans(groupId, open, period).then(setFans, setFans)
  }, [groupId, open, period])

  if (!artists) return <p className="muted">Loading…</p>
  if (artists instanceof Error) return <p className="error">{artists.message}</p>
  if (artists.length === 0) return <p className="muted empty">No plays in this period yet.</p>

  return (
    <ul className="artists">
      {artists.map(a => (
        <li key={a.artist}>
          <button className="artist" onClick={() => setOpen(open === a.artist ? null : a.artist)} aria-expanded={open === a.artist}>
            <span className="name">{a.artist}</span>
            <span className="muted small">{minutes(a.minutes)} · {a.listeners} {a.listeners === 1 ? 'listener' : 'listeners'}</span>
            <span className="fan">
              <span className="crown" aria-label="Top fan">♛</span>
              <Avatar name={a.topFan.displayName} url={a.topFan.avatarUrl} />
              {a.topFan.displayName}
            </span>
          </button>
          {open === a.artist && (
            <div className="fans">
              {!fans ? <p className="muted">Loading…</p>
                : fans instanceof Error ? <p className="error">{fans.message}</p>
                : <RankTable entries={fans} meId={meId} format={e => minutes(e.value)} empty="" />}
            </div>
          )}
        </li>
      ))}
    </ul>
  )
}
