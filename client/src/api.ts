export type Me = {
  id: number
  displayName: string
  avatarUrl: string | null
  timeZone: string
  lastPolledAt: string | null
  needsReauth: boolean
  playCount: number
}
export type GroupSummary = { id: number; name: string; inviteCode: string; memberCount: number }
export type GroupDetail = {
  id: number
  name: string
  inviteCode: string
  members: { userId: number; displayName: string; avatarUrl: string | null }[]
}
export type Entry = {
  rank: number
  userId: number
  displayName: string
  avatarUrl: string | null
  value: number
  best: number | null
}
export type ArtistRow = { artist: string; minutes: number; listeners: number; topFan: Entry }
export type Metric = 'minutes' | 'diversity' | 'streak'
export type Period = 'week' | 'month' | 'year' | 'all'

export class Unauthorized extends Error {}

async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(path, init)
  if (res.status === 401) throw new Unauthorized()
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new Error(body?.detail ?? body?.title ?? `Request failed (${res.status})`)
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

const send = (method: string, body?: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: body === undefined ? undefined : JSON.stringify(body),
})

const q = (params: Record<string, string>) => new URLSearchParams(params).toString()

export const api = {
  me: () => call<Me>('/api/me'),
  setTimeZone: (timeZone: string) => call<void>('/api/me/timezone', send('PUT', { timeZone })),
  sync: () => call<{ added: number }>('/api/me/sync', { method: 'POST' }),
  importFiles: (files: File[]) => {
    const form = new FormData()
    files.forEach(f => form.append('files', f))
    return call<{ imported: number; skipped: number }>('/api/imports', { method: 'POST', body: form })
  },
  logout: () => call<void>('/auth/logout', { method: 'POST' }),

  groups: () => call<GroupSummary[]>('/api/groups'),
  createGroup: (name: string) => call<GroupSummary>('/api/groups', send('POST', { name })),
  joinGroup: (code: string) => call<{ id: number }>('/api/groups/join', send('POST', { code })),
  leaveGroup: (id: number) => call<void>(`/api/groups/${id}/members/me`, { method: 'DELETE' }),
  group: (id: number) => call<GroupDetail>(`/api/groups/${id}`),
  leaderboard: (id: number, metric: Metric, period: Period) =>
    call<Entry[]>(`/api/groups/${id}/leaderboard?${q({ metric, period })}`),
  artists: (id: number, period: Period) => call<ArtistRow[]>(`/api/groups/${id}/artists?${q({ period })}`),
  superfans: (id: number, artist: string, period: Period) =>
    call<Entry[]>(`/api/groups/${id}/superfans?${q({ artist, period })}`),
}
