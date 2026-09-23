import { useEffect, useState } from 'react'
import { fetchAccount, type AccountSummary } from '../auth/authApi'
import { useAuth } from '../auth/useAuth'
import { SessionCountdown } from '../components/SessionCountdown'
import { LoadingScreen } from '../routing/guards'

const METHOD_LABELS = { google: 'Google', password: 'Password' } as const

type Load = { status: 'loading' } | { status: 'error' } | { status: 'loaded'; account: AccountSummary }

/** Signed-in area (spec 003, FR-010–FR-015). Remounted per signed-in user, so another user's data never shows (FR-013). */
export function DashboardPage() {
  const { state } = useAuth()
  const email = state.status === 'signedIn' ? state.user.email : ''
  return <Dashboard key={email} />
}

/** Fetches the account once on mount and on "Try again"; never on a timer (FR-017). */
function Dashboard() {
  const { state } = useAuth()
  const session = state.status === 'signedIn' ? state.session : null
  const [load, setLoad] = useState<Load>({ status: 'loading' })

  // Bumped by "Try again" to re-run the fetch effect.
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let current = true
    fetchAccount()
      .then((account) => current && setLoad(account ? { status: 'loaded', account } : { status: 'error' }))
      .catch(() => current && setLoad({ status: 'error' }))
    return () => {
      current = false
    }
  }, [attempt])

  const retry = () => {
    setLoad({ status: 'loading' })
    setAttempt((n) => n + 1)
  }

  if (load.status === 'loading') return <LoadingScreen />

  if (load.status === 'error') {
    return (
      <section className="card page-card">
        <p className="modal__error" role="alert">
          Couldn't load your account details. Please try again.
        </p>
        <button type="button" className="primary-button" onClick={retry}>
          Try again
        </button>
      </section>
    )
  }

  const { account } = load
  return (
    <div className="dashboard-grid">
      <section className="card">
        <h1>Welcome, {account.displayName || account.email}</h1>
        <dl className="details">
          <dt>Email</dt>
          <dd>{account.email}</dd>
          <dt>Display name</dt>
          <dd>{account.displayName || 'Not set'}</dd>
          <dt>Sign-in methods</dt>
          <dd>{account.methods.map((m) => METHOD_LABELS[m]).join(' and ') || 'None'}</dd>
          <dt>Account created</dt>
          <dd>{new Date(account.createdAt).toLocaleString()}</dd>
          <dt>Last sign-in</dt>
          <dd>{new Date(account.lastSignInAt).toLocaleString()}</dd>
        </dl>
      </section>

      {session && (
        <section className="card" aria-label="Session">
          <h2>Your session</h2>
          <p className="muted">
            Ends after 60 minutes without activity, and at most 8 hours after you signed in — whichever comes first.
          </p>
          <SessionCountdown session={session} />
        </section>
      )}
    </div>
  )
}
