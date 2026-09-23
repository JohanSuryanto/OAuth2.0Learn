import { useEffect, useId, useState } from 'react'
import { listSessions, revokeOtherSessions, revokeSession, type SessionInfo } from '../../auth/accountApi'

const ERROR = "Couldn't update your sessions. Please try again."

/** "Where you're signed in" (spec 004, US4, FR-013–FR-014). */
export function SessionsSection() {
  const id = useId()
  const [sessions, setSessions] = useState<SessionInfo[] | null>(null)
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let current = true
    listSessions()
      .then((list) => {
        if (!current) return
        if (list) setSessions(list)
        else setError(ERROR)
      })
      .catch(() => current && setError(ERROR))
    return () => {
      current = false
    }
  }, [reload])

  const onRevoke = async (sessionId: string) => {
    setBusy(true)
    setError(undefined)
    try {
      if (await revokeSession(sessionId)) setSessions((list) => list?.filter((s) => s.id !== sessionId) ?? null)
      else setError(ERROR)
    } finally {
      setBusy(false)
    }
  }

  const onRevokeOthers = async () => {
    setBusy(true)
    setError(undefined)
    try {
      if (await revokeOtherSessions()) setReload((n) => n + 1)
      else setError(ERROR)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>Where you're signed in</h2>
      {error && (
        <p className="modal__error" role="alert">
          {error}
        </p>
      )}
      {!sessions && !error && <p aria-busy="true">Loading…</p>}

      {sessions && (
        <ul className="sessions">
          {sessions.map((s) => (
            <li key={s.id} className="sessions__row" data-testid="session-row">
              <div className="sessions__main">
                <span className="sessions__device">{s.device ?? 'Unknown device'}</span>
                {s.current && <span className="badge">This device</span>}
              </div>
              <div className="sessions__meta">
                <span>{s.ipMasked ?? 'Unknown'}</span>
                <span>Signed in {new Date(s.createdAt).toLocaleString()}</span>
                <span>Last active {new Date(s.lastSeenAt).toLocaleString()}</span>
              </div>
              {!s.current && (
                <button
                  type="button"
                  className="link-button"
                  onClick={() => void onRevoke(s.id)}
                  disabled={busy}
                  aria-label={`Sign out ${s.device ?? 'Unknown device'}`}
                >
                  Sign out
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {sessions && sessions.length > 1 && (
        <button type="button" className="primary-button settings__submit" onClick={() => void onRevokeOthers()} disabled={busy}>
          Sign out all other devices
        </button>
      )}
    </section>
  )
}
