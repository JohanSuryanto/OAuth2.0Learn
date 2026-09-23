import { useId, useState, type FormEvent } from 'react'
import { signInWithPassword } from '../../auth/passwordAuthApi'
import { validateEmail } from '../../auth/passwordValidation'
import { Field } from './Field'
import { describedBy } from './fieldAria'
import { FORM_MESSAGES, failureMessage } from './formMessages'
import { PasswordInput } from './PasswordInput'

type EmailSignInFormProps = {
  onSignedIn: () => void | Promise<void>
  onForgotPassword: () => void
}

export function EmailSignInForm({ onSignedIn, onForgotPassword }: EmailSignInFormProps) {
  const id = useId()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({})
  const [formError, setFormError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return

    const next = {
      email: validateEmail(email),
      password: password ? undefined : 'Enter your password.',
    }
    setErrors(next)
    setFormError(undefined)
    if (next.email || next.password) return

    setSubmitting(true)
    try {
      const result = await signInWithPassword(email.trim(), password)
      if (result.ok) {
        await onSignedIn()
        return
      }
      setPassword('')
      if (result.reason === 'invalid_credentials') setFormError(FORM_MESSAGES.invalidCredentials)
      else if (result.reason === 'email_not_verified') setFormError(FORM_MESSAGES.emailNotVerified)
      else setFormError(failureMessage(result.reason))
    } finally {
      setSubmitting(false)
    }
  }

  const emailId = `${id}-email`
  const passwordId = `${id}-password`

  return (
    <form className="auth-form" onSubmit={(e) => void onSubmit(e)} noValidate aria-label="Sign in with email">
      {formError && (
        <p className="modal__error" role="alert">
          {formError}
        </p>
      )}

      <Field id={emailId} label="Email" error={errors.email}>
        <input
          {...describedBy(emailId, errors.email)}
          className="field__input"
          type="email"
          name="email"
          autoComplete="username"
          inputMode="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </Field>

      <Field id={passwordId} label="Password" error={errors.password}>
        <PasswordInput
          {...describedBy(passwordId, errors.password)}
          name="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
      </Field>

      <button type="button" className="link-button auth-form__forgot" onClick={onForgotPassword}>
        Forgot password?
      </button>

      <button type="submit" className="primary-button" disabled={submitting}>
        {submitting ? 'Signing in…' : 'Sign in'}
      </button>
    </form>
  )
}
