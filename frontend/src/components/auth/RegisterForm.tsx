import { useId, useState, type FormEvent } from 'react'
import { register } from '../../auth/passwordAuthApi'
import {
  DISPLAY_NAME_MAX_LENGTH,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  validateDisplayName,
  validateEmail,
  validateNewPassword,
} from '../../auth/passwordValidation'
import { Field } from './Field'
import { describedBy } from './fieldAria'
import { failureMessage, fieldMessage } from './formMessages'
import { PasswordInput } from './PasswordInput'

type RegisterFormProps = {
  /** Registration accepted: the user must now open the link from their inbox. */
  onCheckInbox: (email: string) => void
}

type Errors = { displayName?: string; email?: string; password?: string; confirm?: string }

const SERVER_FIELDS: Record<string, keyof Errors> = { email: 'email', password: 'password', displayName: 'displayName' }

export function RegisterForm({ onCheckInbox }: RegisterFormProps) {
  const id = useId()
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [formError, setFormError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return

    const next: Errors = {
      displayName: validateDisplayName(displayName),
      email: validateEmail(email),
      password: validateNewPassword(password, email),
      confirm: confirm === password ? undefined : "Passwords don't match.",
    }
    setErrors(next)
    setFormError(undefined)
    if (Object.values(next).some(Boolean)) return

    setSubmitting(true)
    try {
      const result = await register({
        email: email.trim(),
        password,
        displayName: displayName.trim() || undefined,
      })
      if (result.ok) {
        onCheckInbox(email.trim())
        return
      }
      if (result.reason === 'validation') {
        const serverErrors: Errors = {}
        for (const [field, code] of Object.entries(result.errors)) {
          const target = SERVER_FIELDS[field]
          if (target) serverErrors[target] = fieldMessage(code)
        }
        if (Object.keys(serverErrors).length > 0) setErrors(serverErrors)
        else setFormError(failureMessage('unexpected'))
      } else {
        setFormError(failureMessage(result.reason))
      }
    } finally {
      setSubmitting(false)
    }
  }

  const nameId = `${id}-name`
  const emailId = `${id}-email`
  const passwordId = `${id}-password`
  const confirmId = `${id}-confirm`
  const passwordHint = `${PASSWORD_MIN_LENGTH}–${PASSWORD_MAX_LENGTH} characters. A long passphrase works well.`

  return (
    <form className="auth-form" onSubmit={(e) => void onSubmit(e)} noValidate aria-label="Create account">
      {formError && (
        <p className="modal__error" role="alert">
          {formError}
        </p>
      )}

      <Field id={nameId} label="Display name (optional)" error={errors.displayName}>
        <input
          {...describedBy(nameId, errors.displayName)}
          className="field__input"
          type="text"
          name="name"
          autoComplete="name"
          maxLength={DISPLAY_NAME_MAX_LENGTH}
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
        />
      </Field>

      <Field id={emailId} label="Email" error={errors.email}>
        <input
          {...describedBy(emailId, errors.email)}
          className="field__input"
          type="email"
          name="email"
          autoComplete="email"
          inputMode="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </Field>

      <Field id={passwordId} label="Password" error={errors.password} hint={passwordHint}>
        <PasswordInput
          {...describedBy(passwordId, errors.password, passwordHint)}
          name="new-password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
      </Field>

      <Field id={confirmId} label="Confirm password" error={errors.confirm}>
        <PasswordInput
          {...describedBy(confirmId, errors.confirm)}
          name="confirm-password"
          autoComplete="new-password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
        />
      </Field>

      <button type="submit" className="primary-button" disabled={submitting}>
        {submitting ? 'Creating account…' : 'Create account'}
      </button>
    </form>
  )
}
