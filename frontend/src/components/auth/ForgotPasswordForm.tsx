import { useId, useState, type FormEvent } from 'react'
import { forgotPassword } from '../../auth/passwordAuthApi'
import { validateEmail } from '../../auth/passwordValidation'
import { Field } from './Field'
import { describedBy } from './fieldAria'
import { FORM_MESSAGES, failureMessage, fieldMessage } from './formMessages'

type ForgotPasswordFormProps = {
  onBack: () => void
}

export function ForgotPasswordForm({ onBack }: ForgotPasswordFormProps) {
  const id = useId()
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string>()
  const [formError, setFormError] = useState<string>()
  const [sentTo, setSentTo] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return

    const emailError = validateEmail(email)
    setError(emailError)
    setFormError(undefined)
    if (emailError) return

    setSubmitting(true)
    try {
      const result = await forgotPassword(email.trim())
      if (result.ok) setSentTo(email.trim())
      else if (result.reason === 'validation') setError(fieldMessage(result.errors.email ?? ''))
      else setFormError(failureMessage(result.reason))
    } finally {
      setSubmitting(false)
    }
  }

  const back = (
    <button type="button" className="link-button" onClick={onBack}>
      Back to sign in
    </button>
  )

  if (sentTo) {
    return (
      <div className="auth-form">
        <p className="notice" role="status">
          {FORM_MESSAGES.resetSent(sentTo)}
        </p>
        {back}
      </div>
    )
  }

  const emailId = `${id}-email`
  return (
    <form className="auth-form" onSubmit={(e) => void onSubmit(e)} noValidate aria-label="Reset password">
      {formError && (
        <p className="modal__error" role="alert">
          {formError}
        </p>
      )}

      <Field id={emailId} label="Email" error={error}>
        <input
          {...describedBy(emailId, error)}
          className="field__input"
          type="email"
          name="email"
          autoComplete="username"
          inputMode="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </Field>

      <button type="submit" className="primary-button" disabled={submitting}>
        {submitting ? 'Sending…' : 'Send reset link'}
      </button>
      {back}
    </form>
  )
}
