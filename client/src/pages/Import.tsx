import { useState, type FormEvent } from 'react'
import { api } from '../api'

export default function Import({ onImported }: { onImported: () => void }) {
  const [files, setFiles] = useState<File[]>([])
  const [status, setStatus] = useState<{ busy?: boolean; ok?: string; error?: string }>({})

  const upload = async (e: FormEvent) => {
    e.preventDefault()
    setStatus({ busy: true })
    try {
      const r = await api.importFiles(files)
      setStatus({ ok: `Imported ${r.imported.toLocaleString()} plays (${r.skipped.toLocaleString()} skipped: short plays, podcasts or already imported).` })
      onImported()
    } catch (err) {
      setStatus({ error: (err as Error).message })
    }
  }

  return (
    <div className="narrow">
      <h1>Import your history</h1>
      <p className="muted">
        Spotify's API only shows your last 50 plays, so this app starts counting from when you sign up.
        To backfill older listening, upload your Spotify data export.
      </p>
      <ol className="steps">
        <li>Go to <a href="https://www.spotify.com/account/privacy/" target="_blank" rel="noreferrer">spotify.com/account/privacy</a>.</li>
        <li>Request <strong>Extended streaming history</strong> for your whole history (takes up to 30 days), or <strong>Account data</strong> for the last year (about 5 days).</li>
        <li>When the email arrives, upload the .zip, or the <code>Streaming_History_Audio_*.json</code> / <code>StreamingHistory_music_*.json</code> files inside it.</li>
      </ol>
      <form onSubmit={upload} className="upload">
        <input type="file" accept=".zip,.json" multiple onChange={e => setFiles([...(e.target.files ?? [])])} />
        <button className="btn primary" disabled={files.length === 0 || status.busy}>
          {status.busy ? 'Importing…' : 'Upload'}
        </button>
      </form>
      {status.ok && <p className="ok">{status.ok}</p>}
      {status.error && <p className="error">{status.error}</p>}
      <p className="muted small">Plays shorter than 30 seconds don't count. Importing the same file twice won't double-count anything.</p>
    </div>
  )
}
