import { useId, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { verifyEmail } from '../auth/passwordAuthApi'
import { useAuth } from '../auth/useAuth'
import { Field } from '../components/auth/Field'
import { describedBy } from '../components/auth/fieldAria'
import { FORM_MESSAGES, failureMessage, fieldMessage } from '../components/auth/formMessages'
import { PasswordInput } from '../components/auth/PasswordInput'
import { takeTokenFromFragment } from './fragmentToken'

type Status = 'form' | 'invalidLink'

/** Opened from the verification email: token (from #fragment) + the chosen password → signed in (research R4). */
export function VerifyEmailPage() {
  const { refresh } = useAuth()
  const navigate = useNavigate()
  const id = useId()
  const [token] = useState(takeTokenFromFragment)
  const [status, setStatus] = useState<Status>(token ? 'form' : 'invalidLink')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string>()
  const [formError, setFormError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return
    setFormError(undefined)
    if (!password) {
      setError('Enter the password you chose.')
      return
    }

    setSubmitting(true)
    try {
      const result = await verifyEmail(token, password)
      if (result.ok) {
        await refresh()
        // Signed in: land on the Dashboard (spec 003, FR-004).
        navigate('/dashboard', { replace: true })
        return
      } else if (result.reason === 'validation' && result.errors.token) {
        setStatus('invalidLink')
      } else if (result.reason === 'validation' && result.errors.password) {
        setError(fieldMessage(result.errors.password))
        setPassword('')
      } else {
        setFormError(failureMessage(result.reason))
      }
    } finally {
      setSubmitting(false)
    }
  }

  const passwordId = `${id}-password`
  return (
    <section className="card page-card">
      <h1>Verify your email</h1>

      {status === 'invalidLink' && (
        <>
          <p className="modal__error" role="alert">
            {FORM_MESSAGES.verifyLinkInvalid}
          </p>
          <Link className="primary-link" to="/">
            Go to Home
          </Link>
        </>
      )}

      {status === 'form' && (
        <form className="auth-form" onSubmit={(e) => void onSubmit(e)} noValidate aria-label="Verify your email">
          <p>Enter the password you chose when you created your account to finish.</p>
          {formError && (
            <p className="modal__error" role="alert">
              {formError}
            </p>
          )}
          <Field id={passwordId} label="Password you chose" error={error}>
            <PasswordInput
              {...describedBy(passwordId, error)}
              name="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>
          <button type="submit" className="primary-button" disabled={submitting}>
            {submitting ? 'Verifying…' : 'Verify and sign in'}
          </button>
        </form>
      )}
    </section>
  )
}
