import { useEffect, useId, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { formatMinutes, secondsLeft } from './sessionTime'

const WARN_AT_SECONDS = 120

/**
 * "You'll be signed out in m:ss" (spec 004, FR-017–FR-019). Ticks locally and never calls the server on its own;
 * only "Stay signed in" (or "Sign out") does. At zero, feature 003's session-ended handling takes over.
 */
export function SessionExpiryWarning() {
  const { state, extendSession, logout } = useAuth()
  const navigate = useNavigate()
  const [now, setNow] = useState(() => performance.now())
  const [dismissedFor, setDismissedFor] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const primary = useRef<HTMLButtonElement>(null)
  const titleId = useId()

  const session = state.status === 'signedIn' ? state.session : null

  useEffect(() => {
    if (!session) return
    const tick = setInterval(() => setNow(performance.now()), 1000)
    return () => clearInterval(tick)
  }, [session])

  const idle = session ? secondsLeft(session.idleSecondsLeft, session.receivedAt, now) : Infinity
  const absolute = session ? secondsLeft(session.absoluteSecondsLeft, session.receivedAt, now) : Infinity
  const left = Math.min(idle, absolute)
  const visible = !!session && left <= WARN_AT_SECONDS && left > 0 && dismissedFor !== session.receivedAt
  const canExtend = idle <= absolute && absolute > WARN_AT_SECONDS

  useEffect(() => {
    if (visible) primary.current?.focus()
  }, [visible])

  if (!visible || !session) return null

  const onStay = async () => {
    setBusy(true)
    try {
      await extendSession()
    } finally {
      setBusy(false)
    }
  }

  const onSignOut = async () => {
    setBusy(true)
    try {
      await logout()
      navigate('/')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop">
      <div className="modal" role="alertdialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 id={titleId} className="modal__title">
          You'll be signed out in {formatMinutes(left)}
        </h2>
        {canExtend ? (
          <p className="modal__subtitle">Do you want to stay signed in?</p>
        ) : (
          <p className="modal__subtitle">Your session can't be extended any further.</p>
        )}
        <div className="modal__actions">
          {canExtend ? (
            <>
              <button ref={primary} type="button" className="primary-button" onClick={() => void onStay()} disabled={busy}>
                Stay signed in
              </button>
              <button type="button" className="secondary-button" onClick={() => void onSignOut()} disabled={busy}>
                Sign out
              </button>
            </>
          ) : (
            <>
              <button type="button" className="secondary-button" onClick={() => void onSignOut()} disabled={busy}>
                Sign out
              </button>
              <button ref={primary} type="button" className="primary-button" onClick={() => setDismissedFor(session.receivedAt)}>
                OK
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
