import type { Entry } from './api'

export function Avatar({ name, url }: { name: string; url: string | null }) {
  return url
    ? <img className="avatar" src={url} alt="" />
    : <span className="avatar">{name.charAt(0).toUpperCase()}</span>
}

/** Ranked rows with a bar scaled to the leader. */
export function RankTable({ entries, meId, format, empty }: {
  entries: Entry[]
  meId: number
  format: (e: Entry) => string
  empty: string
}) {
  if (entries.length === 0 || entries.every(e => e.value === 0)) return <p className="muted empty">{empty}</p>
  const max = Math.max(...entries.map(e => e.value), 1)
  return (
    <ol className="ranks">
      {entries.map(e => (
        <li key={e.userId} className={e.userId === meId ? 'mine' : undefined}>
          <span className={`rank r${e.rank}`}>{e.rank}</span>
          <Avatar name={e.displayName} url={e.avatarUrl} />
          <span className="name">{e.displayName}</span>
          <span className="bar"><span style={{ width: `${(e.value / max) * 100}%` }} /></span>
          <span className="value">{format(e)}</span>
        </li>
      ))}
    </ol>
  )
}

export const minutes = (v: number) =>
  v >= 600 ? `${(v / 60).toLocaleString(undefined, { maximumFractionDigits: 0 })} h` : `${Math.round(v).toLocaleString()} min`
