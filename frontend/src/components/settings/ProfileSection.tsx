import { useId, useState, type FormEvent } from 'react'
import { updateProfile } from '../../auth/accountApi'
import type { AccountSummary } from '../../auth/authApi'
import { DISPLAY_NAME_MAX_LENGTH, validateDisplayName } from '../../auth/passwordValidation'
import { useAuth } from '../../auth/useAuth'
import { Field } from '../auth/Field'
import { describedBy } from '../auth/fieldAria'
import { failureMessage, fieldMessage } from '../auth/formMessages'

/** Email read-only, display name editable (spec 004, FR-005). */
export function ProfileSection({ account, onSaved }: { account: AccountSummary; onSaved: (a: AccountSummary) => void }) {
  const { refresh } = useAuth()
  const id = useId()
  const [name, setName] = useState(account.displayName ?? '')
  const [error, setError] = useState<string>()
  const [formError, setFormError] = useState<string>()
  const [saved, setSaved] = useState(false)
  const [saving, setSaving] = useState(false)

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (saving) return
    setSaved(false)
    setFormError(undefined)
    const clientError = validateDisplayName(name)
    setError(clientError)
    if (clientError) return

    setSaving(true)
    try {
      const result = await updateProfile(name.trim() || null)
      if (result.ok) {
        setSaved(true)
        onSaved(result.account)
        await refresh() // header shows the new name (SC-003)
      } else if (result.reason === 'validation' && result.errors.displayName) {
        setError(fieldMessage(result.errors.displayName))
      } else {
        setFormError(failureMessage(result.reason))
      }
    } finally {
      setSaving(false)
    }
  }

  const nameId = `${id}-name`
  return (
    <section className="card" aria-labelledby={`${id}-title`}>
      <h2 id={`${id}-title`}>Profile</h2>
      <form className="auth-form" onSubmit={(e) => void onSubmit(e)} noValidate aria-label="Profile">
        <div className="field">
          <span className="field__label">Email</span>
          <span className="readonly-value">{account.email}</span>
          <p className="field__hint">Your email can't be changed.</p>
        </div>

        <Field id={nameId} label="Display name" error={error} hint="Shown in the header and on your Dashboard.">
          <input
            {...describedBy(nameId, error, 'Shown in the header and on your Dashboard.')}
            className="field__input"
            type="text"
            name="name"
            autoComplete="name"
            maxLength={DISPLAY_NAME_MAX_LENGTH + 1}
            value={name}
            onChange={(e) => {
              setName(e.target.value)
              setSaved(false)
            }}
          />
        </Field>

        {formError && (
          <p className="modal__error" role="alert">
            {formError}
          </p>
        )}
        {saved && (
          <p className="notice" role="status">
            Profile updated.
          </p>
        )}

        <button type="submit" className="primary-button settings__submit" disabled={saving}>
          {saving ? 'Saving…' : 'Save'}
        </button>
      </form>
    </section>
  )
}
