import { useId, useState, type FormEvent } from 'react'
import { reauthWithGoogle, reauthWithPassword, type ReauthMethod } from '../../auth/accountApi'
import { Field } from '../auth/Field'
import { describedBy } from '../auth/fieldAria'
import { failureMessage, fieldMessage } from '../auth/formMessages'
import { PasswordInput } from '../auth/PasswordInput'

const GOOGLE_MESSAGES = {
  mismatch: "That Google account isn't the one linked to this account.",
  cancelled: 'Confirmation was cancelled.',
  blocked: 'Please allow popups for this site to confirm with Google.',
  failed: "Couldn't confirm with Google. Please try again.",
} as const

/** "Confirm it's you" before a sensitive action (spec 004, FR-009, FR-012). */
export function ReauthPanel({ methods, onConfirmed }: { methods: ReauthMethod[]; onConfirmed: () => void | Promise<void> }) {
  const id = useId()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string>()
  const [formError, setFormError] = useState<string>()
  const [busy, setBusy] = useState(false)

  const onPassword = async (event: FormEvent) => {
    event.preventDefault()
    if (busy) return
    setFormError(undefined)
    if (!password) {
      setError('Enter your current password.')
      return
    }

    setBusy(true)
    try {
      const result = await reauthWithPassword(password)
      if (result.ok) return await onConfirmed()
      setPassword('')
      if (result.reason === 'validation' && result.errors.password) setError(fieldMessage(result.errors.password))
      else setFormError(failureMessage(result.reason))
    } finally {
      setBusy(false)
    }
  }

  const onGoogle = async () => {
    if (busy) return
    setFormError(undefined)
    setBusy(true)
    try {
      const result = await reauthWithGoogle()
      if (result.ok) return await onConfirmed()
      setFormError(GOOGLE_MESSAGES[result.reason])
    } finally {
      setBusy(false)
    }
  }

  const passwordId = `${id}-password`
  return (
    <div className="reauth" role="group" aria-labelledby={`${id}-title`}>
      <h3 id={`${id}-title`}>Confirm it's you</h3>
      <p className="muted">For your security, please confirm your identity to continue.</p>
      {formError && (
        <p className="modal__error" role="alert">
          {formError}
        </p>
      )}

      {methods.includes('password') && (
        <form className="auth-form" onSubmit={(e) => void onPassword(e)} noValidate aria-label="Confirm with password">
          <Field id={passwordId} label="Current password" error={error}>
            <PasswordInput
              {...describedBy(passwordId, error)}
              name="current-password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>
          <button type="submit" className="primary-button settings__submit" disabled={busy}>
            Confirm
          </button>
        </form>
      )}

      {methods.includes('google') && (
        <button type="button" className="provider-button" onClick={() => void onGoogle()} disabled={busy}>
          Continue with Google
        </button>
      )}
    </div>
  )
}
