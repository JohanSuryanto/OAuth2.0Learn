import { useLoginDialog } from '../auth/loginDialogContext'
import { MESSAGES, useAuth } from '../auth/useAuth'

/** Public landing page for signed-out visitors (spec 003, FR-001, FR-019). */
export function HomePage() {
  const { state } = useAuth()
  const { open } = useLoginDialog()
  const sessionEnded = state.status === 'signedOut' && state.sessionEnded

  return (
    <section className="card home">
      {sessionEnded && (
        <p className="notice" role="status">
          {MESSAGES.sessionEnded}
        </p>
      )}
      <h1>Learn OAuth 2.0 by signing in</h1>
      <p>
        This app shows how signing in works: with your Google account (OAuth 2.0) or with an email and password.
        Sign in to see your account and session details.
      </p>
      <button type="button" className="primary-button home__cta" aria-haspopup="dialog" onClick={open}>
        Get started
      </button>
    </section>
  )
}
