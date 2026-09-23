import { Link } from 'react-router'
import { useLoginDialog } from '../auth/loginDialogContext'
import { useAuth } from '../auth/useAuth'
import { UserMenu } from './UserMenu'

export function Header() {
  const { state } = useAuth()
  const { open: openLogin } = useLoginDialog()

  // Sign-in errors are shown inside the dialog; the header only shows logout errors.
  const error = state.status === 'signedIn' ? state.error : undefined

  return (
    <header className="header">
      <Link to="/" className="header__title">
        OAuth2.0 Learn
      </Link>
      <div className="header__auth">
        {error && (
          <span className="header__error" role="alert">
            {error}
          </span>
        )}
        {state.status === 'loading' && (
          <button type="button" className="header__button" disabled aria-label="Checking sign-in status">
            …
          </button>
        )}
        {state.status === 'signedOut' && (
          <button type="button" className="header__button" aria-haspopup="dialog" onClick={openLogin}>
            Login
          </button>
        )}
        {state.status === 'signedIn' && <UserMenu />}
      </div>
    </header>
  )
}
