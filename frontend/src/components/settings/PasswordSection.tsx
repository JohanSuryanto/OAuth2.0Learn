import { useId, useState, type FormEvent } from 'react'
import { changePassword, setPassword, type ReauthMethod } from '../../auth/accountApi'
import type { AccountSummary } from '../../auth/authApi'
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, validateNewPassword } from '../../auth/passwordValidation'
import { Field } from '../auth/Field'
import { describedBy } from '../auth/fieldAria'
import { failureMessage, fieldMessage } from '../auth/formMessages'
import { PasswordInput } from '../auth/PasswordInput'
import { ReauthPanel } from './ReauthPanel'

const HINT = `${PASSWORD_MIN_LENGTH}–${PASSWORD_MAX_LENGTH} characters. A long passphrase works well.`

type Errors = { current?: string; next?: string; confirm?: string }

/** Change password (current required) or set one for Google-only accounts (spec 004, US3). */
export function PasswordSection({ account, onChanged }: { account: AccountSummary; onChanged: () => void }) {
  const id = useId()
  const hasPassword = account.methods.includes('password')
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [formError, setFormError] = useState<string>()
  const [success, setSuccess] = useState<string>()
  const [reauth, setReauth] = useState<ReauthMethod[] | null>(null)
  const [busy, setBusy] = useState(false)

  const reset = () => {
    setCurrent('')
    setNext('')
    setConfirm('')
  }

  const validate = (): boolean => {
    const found: Errors = {
      current: hasPassword && !current ? 'Enter your current password.' : undefined,
      next: validateNewPassword(next, account.email),
      confirm: confirm === next ? undefined : "Passwords don't match.",
    }
    setErrors(found)
    return !Object.values(found).some(Boolean)
  }

  const submitSet = async () => {
    const result = await setPassword(next)
    if (result.ok) {
      setReauth(null)
      reset()
      setSuccess('Password set. You can now sign in with your email and password.')
      onChanged()
    } else if (result.reason === 'reauth_required') {
      setReauth(result.methods)
    } else if (result.reason === 'validation') {
      setErrors({ next: fieldMessage(result.errors.newPassword ?? '') })
    } else {
      setFormError(failureMessage(result.reason))
    }
  }

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy) return
    setSuccess(undefined)
    setFormError(undefined)
    if (!validate()) return

    setBusy(true)
    try {
      if (!hasPassword) return await submitSet()

      const result = await changePassword(current, next)
      if (result.ok) {
        reset()
        setSuccess('Password changed. Other devices have been signed out.')
        onChanged()
      } else if (result.reason === 'validation') {
        setCurrent('')
        setErrors({
          current: result.errors.currentPassword ? fieldMessage(result.errors.currentPassword) : undefined,
          next: result.errors.newPassword ? fieldMessage(result.errors.newPassword) : undefined,
        })
      } else {
        setFormError(failureMessage(result.reason))
      }
    } finally {
      setBusy(false)
    }
  }

  const ids = { current: `${id}-current`, next: `${id}-next`, confirm: `${id}-confirm` }
  return (
    <section className="card" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>{hasPassword ? 'Change password' : 'Set a password'}</h2>
      {!hasPassword && <p className="muted">Add a password so you can also sign in with your email.</p>}

      {success && (
        <p className="notice" role="status">
          {success}
        </p>
      )}

      {reauth ? (
        <ReauthPanel methods={reauth} onConfirmed={submitSet} />
      ) : (
        <form
          className="auth-form"
          onSubmit={(e) => void onSubmit(e)}
          noValidate
          aria-label={hasPassword ? 'Change password' : 'Set a password'}
        >
          {formError && (
            <p className="modal__error" role="alert">
              {formError}
            </p>
          )}
          {hasPassword && (
            <Field id={ids.current} label="Current password" error={errors.current}>
              <PasswordInput
                {...describedBy(ids.current, errors.current)}
                name="current-password"
                autoComplete="current-password"
                value={current}
                onChange={(e) => setCurrent(e.target.value)}
              />
            </Field>
          )}
          <Field id={ids.next} label="New password" error={errors.next} hint={HINT}>
            <PasswordInput
              {...describedBy(ids.next, errors.next, HINT)}
              name="new-password"
              autoComplete="new-password"
              value={next}
              onChange={(e) => setNext(e.target.value)}
            />
          </Field>
          <Field id={ids.confirm} label="Confirm new password" error={errors.confirm}>
            <PasswordInput
              {...describedBy(ids.confirm, errors.confirm)}
              name="confirm-password"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
          </Field>
          <button type="submit" className="primary-button settings__submit" disabled={busy}>
            {hasPassword ? 'Change password' : 'Set password'}
          </button>
        </form>
      )}
    </section>
  )
}
