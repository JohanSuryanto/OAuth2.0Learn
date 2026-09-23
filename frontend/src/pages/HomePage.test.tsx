import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthContextValue, AuthState } from '../auth/useAuth'
import { HomePage } from './HomePage'

const auth: AuthContextValue = {
  state: { status: 'signedOut' },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}
const dialog = { isOpen: false, open: vi.fn(), close: vi.fn() }

vi.mock('../auth/useAuth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../auth/useAuth')>()),
  useAuth: () => auth,
}))
vi.mock('../auth/loginDialogContext', () => ({ useLoginDialog: () => dialog }))

function renderHome(state: AuthState = { status: 'signedOut' }) {
  auth.state = state
  return render(<HomePage />)
}

describe('HomePage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the welcome, explanation and Get started, without account details', () => {
    renderHome()

    expect(screen.getByRole('heading', { name: 'Learn OAuth 2.0 by signing in' })).toBeInTheDocument()
    expect(screen.getByText(/with your Google account \(OAuth 2\.0\) or with an email and password/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Get started' })).toBeInTheDocument()
    expect(screen.queryByText('Sign-in methods')).not.toBeInTheDocument()
  })

  it('opens the shared sign-in dialog from Get started', async () => {
    renderHome()

    await userEvent.click(screen.getByRole('button', { name: 'Get started' }))

    expect(dialog.open).toHaveBeenCalledTimes(1)
  })

  it('explains when the session has ended', () => {
    renderHome({ status: 'signedOut', sessionEnded: true })

    expect(screen.getByRole('status')).toHaveTextContent('Your session has ended. Please sign in again.')
  })

  it('shows no session-ended notice after a normal logout', () => {
    renderHome({ status: 'signedOut' })

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
