import { useEffect, useId, useRef, useState } from 'react'
import { useAuth } from '../auth/useAuth'
import { EmailSignInForm } from './auth/EmailSignInForm'
import { ForgotPasswordForm } from './auth/ForgotPasswordForm'
import { FORM_MESSAGES } from './auth/formMessages'
import { RegisterForm } from './auth/RegisterForm'

type LoginModalProps = {
  onClose: () => void
}

type Mode = 'signIn' | 'register' | 'checkInbox' | 'forgot'

const TEXT: Record<Mode, { title: string; subtitle: string }> = {
  signIn: { title: 'Sign in', subtitle: 'Sign in to OAuth2.0 Learn.' },
  register: { title: 'Create account', subtitle: 'Create an OAuth2.0 Learn account.' },
  checkInbox: { title: 'Check your inbox', subtitle: 'One more step to finish creating your account.' },
  forgot: { title: 'Reset your password', subtitle: "We'll email you a link to choose a new password." },
}

/**
 * Sign-in dialog opened by the header's Login button: "Continue with Google", then an
 * email/password form, with a switch between "Sign in" and "Create account" (spec 002, FR-001).
 */
export function LoginModal({ onClose }: LoginModalProps) {
  const { state, login, refresh, clearError } = useAuth()
  const [mode, setMode] = useState<Mode>('signIn')
  const [inboxEmail, setInboxEmail] = useState('')
  const [waitingForGoogle, setWaitingForGoogle] = useState(false)
  const googleButton = useRef<HTMLButtonElement>(null)
  const titleId = useId()

  // The parent mounts this only while open, so each opening starts fresh:
  // no stale error, focus on the first sign-in option.
  useEffect(() => {
    clearError()
    googleButton.current?.focus()
  }, [clearError])

  // Signed in (via this dialog or another tab): nothing left to do here.
  useEffect(() => {
    if (state.status === 'signedIn') onClose()
  }, [state.status, onClose])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const onContinueWithGoogle = async () => {
    // window.open runs synchronously inside this click handler, so browsers don't block the popup.
    setWaitingForGoogle(true)
    try {
      await login()
    } finally {
      setWaitingForGoogle(false)
    }
  }

  const switchMode = (next: Mode) => {
    clearError()
    setMode(next)
  }

  const showCheckInbox = (email: string) => {
    setInboxEmail(email)
    setMode('checkInbox')
  }

  const googleError = state.status === 'signedOut' ? state.error : undefined
  const offersGoogle = mode === 'signIn' || mode === 'register'

  return (
    <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <button type="button" className="modal__close" aria-label="Close" onClick={onClose}>
          ×
        </button>

        <h2 id={titleId} className="modal__title">
          {TEXT[mode].title}
        </h2>
        <p className="modal__subtitle">{TEXT[mode].subtitle}</p>

        {googleError && (
          <p className="modal__error" role="alert">
            {googleError}
          </p>
        )}

        {offersGoogle && (
          <div className="modal__methods">
            <button
              ref={googleButton}
              type="button"
              className="provider-button"
              onClick={() => void onContinueWithGoogle()}
            >
              <GoogleLogo />
              <span>Continue with Google</span>
            </button>
            {waitingForGoogle && (
              <p className="modal__hint" role="status">
                Finish signing in in the Google window…
              </p>
            )}

            <div className="divider" role="separator">
              <span>or</span>
            </div>

            {/* key: remount on switch so each form starts empty */}
            {mode === 'signIn' ? (
              <EmailSignInForm key="signIn" onSignedIn={refresh} onForgotPassword={() => switchMode('forgot')} />
            ) : (
              <RegisterForm key="register" onCheckInbox={showCheckInbox} />
            )}
          </div>
        )}

        {mode === 'checkInbox' && (
          <div className="auth-form">
            <p className="notice" role="status">
              {FORM_MESSAGES.checkInbox(inboxEmail)}
            </p>
            <button type="button" className="link-button" onClick={() => switchMode('signIn')}>
              Back to sign in
            </button>
          </div>
        )}

        {mode === 'forgot' && <ForgotPasswordForm onBack={() => switchMode('signIn')} />}

        {offersGoogle && (
          <p className="modal__switch">
            {mode === 'signIn' ? (
              <>
                Don’t have an account?{' '}
                <button type="button" className="link-button" onClick={() => switchMode('register')}>
                  Create one
                </button>
              </>
            ) : (
              <>
                Already have an account?{' '}
                <button type="button" className="link-button" onClick={() => switchMode('signIn')}>
                  Sign in
                </button>
              </>
            )}
          </p>
        )}
      </div>
    </div>
  )
}

function GoogleLogo() {
  return (
    <svg className="provider-button__icon" viewBox="0 0 48 48" aria-hidden="true">
      <path
        fill="#EA4335"
        d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
      />
      <path
        fill="#4285F4"
        d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
      />
      <path
        fill="#FBBC05"
        d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
      />
      <path
        fill="#34A853"
        d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
      />
    </svg>
  )
}
