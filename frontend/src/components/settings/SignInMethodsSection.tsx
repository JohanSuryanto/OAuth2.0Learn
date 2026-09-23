import { useId } from 'react'
import type { AccountSummary } from '../../auth/authApi'

/** Read-only list of how this account can sign in (spec 004, FR-020). */
export function SignInMethodsSection({ account }: { account: AccountSummary }) {
  const id = useId()
  const google = account.methods.includes('google')
  const password = account.methods.includes('password')

  return (
    <section className="card" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>Sign-in methods</h2>
      <dl className="details">
        <dt>Google</dt>
        <dd>{google ? 'Connected' : 'Not connected'}</dd>
        <dt>Password</dt>
        <dd>{password ? 'Set' : 'Not set'}</dd>
      </dl>
    </section>
  )
}
