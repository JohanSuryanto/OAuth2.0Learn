import { useEffect, useState } from 'react'
import { fetchAccount, type AccountSummary } from '../auth/authApi'
import { useAuth } from '../auth/useAuth'
import { PasswordSection } from '../components/settings/PasswordSection'
import { ProfileSection } from '../components/settings/ProfileSection'
import { SessionsSection } from '../components/settings/SessionsSection'
import { SignInMethodsSection } from '../components/settings/SignInMethodsSection'
import { LoadingScreen } from '../routing/guards'

type Load = { status: 'loading' } | { status: 'error' } | { status: 'loaded'; account: AccountSummary }

/** Account settings (spec 004, FR-004). Remounted per signed-in user, like the Dashboard. */
export function SettingsPage() {
  const { state } = useAuth()
  const email = state.status === 'signedIn' ? state.user.email : ''
  return <Settings key={email} />
}

function Settings() {
  const [load, setLoad] = useState<Load>({ status: 'loading' })
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

  if (load.status === 'loading') return <LoadingScreen />

  if (load.status === 'error') {
    return (
      <section className="card page-card">
        <p className="modal__error" role="alert">
          Couldn't load your account details. Please try again.
        </p>
        <button type="button" className="primary-button" onClick={() => setAttempt((n) => n + 1)}>
          Try again
        </button>
      </section>
    )
  }

  const { account } = load
  const setAccount = (next: AccountSummary) => setLoad({ status: 'loaded', account: next })

  return (
    <div className="settings">
      <h1>Account settings</h1>
      <ProfileSection account={account} onSaved={setAccount} />
      <PasswordSection account={account} onChanged={() => setAttempt((n) => n + 1)} />
      <SignInMethodsSection account={account} />
      <SessionsSection />
    </div>
  )
}
