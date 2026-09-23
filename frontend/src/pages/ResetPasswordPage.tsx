import { useId, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { resetPassword } from '../auth/passwordAuthApi'
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, validateNewPassword } from '../auth/passwordValidation'
import { Field } from '../components/auth/Field'
import { describedBy } from '../components/auth/fieldAria'
import { FORM_MESSAGES, failureMessage, fieldMessage } from '../components/auth/formMessages'
import { PasswordInput } from '../components/auth/PasswordInput'
import { takeTokenFromFragment } from './fragmentToken'

type Status = 'form' | 'changed' | 'invalidLink'

/** Opened from the reset email: token (from #fragment) + new password twice (spec 002, FR-016). */
export function ResetPasswordPage() {
  const id = useId()
  const [token] = useState(takeTokenFromFragment)
  const [status, setStatus] = useState<Status>(token ? 'form' : 'invalidLink')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<{ password?: string; confirm?: string }>({})
  const [formError, setFormError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return

    // The account's email isn't known here; the server also rejects a password equal to it.
    const next = {
      password: validateNewPassword(password, ''),
      confirm: confirm === password ? undefined : "Passwords don't match.",
    }
    setErrors(next)
    setFormError(undefined)
    if (next.password || next.confirm) return

    setSubmitting(true)
    try {
      const result = await resetPassword(token, password)
      if (result.ok) setStatus('changed')
      else if (result.reason === 'validation' && result.errors.token) setStatus('invalidLink')
      else if (result.reason === 'validation' && result.errors.newPassword) {
        setErrors({ password: fieldMessage(result.errors.newPassword) })
      } else setFormError(failureMessage(result.reason))
    } finally {
      setSubmitting(false)
    }
  }

  const passwordId = `${id}-password`
  const confirmId = `${id}-confirm`
  const hint = `${PASSWORD_MIN_LENGTH}–${PASSWORD_MAX_LENGTH} characters. A long passphrase works well.`

  return (
    <section className="card page-card">
      <h1>Choose a new password</h1>

      {status === 'changed' && (
        <>
          <p className="notice" role="status">
            {FORM_MESSAGES.passwordChanged}
          </p>
          <Link className="primary-link" to="/">
            Go to Home
          </Link>
        </>
      )}

      {status === 'invalidLink' && (
        <>
          <p className="modal__error" role="alert">
            {FORM_MESSAGES.resetLinkInvalid}
          </p>
          <Link className="primary-link" to="/">
            Go to Home
          </Link>
        </>
      )}

      {status === 'form' && (
        <form className="auth-form" onSubmit={(e) => void onSubmit(e)} noValidate aria-label="Choose a new password">
          {formError && (
            <p className="modal__error" role="alert">
              {formError}
            </p>
          )}
          <Field id={passwordId} label="New password" error={errors.password} hint={hint}>
            <PasswordInput
              {...describedBy(passwordId, errors.password, hint)}
              name="new-password"
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>
          <Field id={confirmId} label="Confirm new password" error={errors.confirm}>
            <PasswordInput
              {...describedBy(confirmId, errors.confirm)}
              name="confirm-password"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
          </Field>
          <button type="submit" className="primary-button" disabled={submitting}>
            {submitting ? 'Changing password…' : 'Change password'}
          </button>
        </form>
      )}
    </section>
  )
}
