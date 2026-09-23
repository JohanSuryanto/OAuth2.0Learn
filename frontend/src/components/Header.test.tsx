import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthContextValue, AuthState } from '../auth/useAuth'
import { Header } from './Header'

const auth: AuthContextValue = {
  state: { status: 'signedOut' },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}
const dialog = { isOpen: false, open: vi.fn(), close: vi.fn() }

vi.mock('../auth/useAuth', () => ({ useAuth: () => auth }))
vi.mock('../auth/loginDialogContext', () => ({ useLoginDialog: () => dialog }))

function renderWith(state: AuthState, path = '/') {
  auth.state = state
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Header />
      <Routes>
        <Route path="/" element={<p>HOME</p>} />
        <Route path="/dashboard" element={<p>DASHBOARD</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

const signedIn = (extra: Partial<Extract<AuthState, { status: 'signedIn' }>> = {}): AuthState => ({
  status: 'signedIn',
  user: { email: 'a@b.com', name: null },
  session: null,
  ...extra,
})

describe('Header', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows a Login button and no greeting when signed out', () => {
    renderWith({ status: 'signedOut' })

    expect(screen.getByRole('button', { name: 'Login' })).toBeInTheDocument()
    expect(screen.queryByText(/^Hi /)).not.toBeInTheDocument()
  })

  it('opens the shared sign-in dialog instead of going straight to Google', async () => {
    renderWith({ status: 'signedOut' })

    await userEvent.click(screen.getByRole('button', { name: 'Login' }))

    expect(dialog.open).toHaveBeenCalledTimes(1)
    expect(auth.login).not.toHaveBeenCalled()
  })

  it('shows the user menu (name, else email) instead of a greeting and Logout button when signed in', () => {
    renderWith(signedIn({ user: { email: 'a@b.com', name: 'Alice' } }))

    expect(screen.getByRole('button', { name: /Alice/ })).toHaveAttribute('aria-haspopup', 'menu')
    expect(screen.queryByText('Hi a@b.com')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Logout' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Login' })).not.toBeInTheDocument()
  })

  it('logs out from the menu and goes Home', async () => {
    renderWith(signedIn(), '/dashboard')
    expect(screen.getByText('DASHBOARD')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /a@b\.com/ }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Log out' }))

    expect(auth.logout).toHaveBeenCalledTimes(1)
    expect(screen.getByText('HOME')).toBeInTheDocument()
  })

  it('shows a logout error next to the greeting', () => {
    renderWith(signedIn({ error: 'Logout failed. Please try again.' }))

    expect(screen.getByRole('alert')).toHaveTextContent('Logout failed. Please try again.')
  })

  it('shows a disabled placeholder instead of Login while loading', () => {
    renderWith({ status: 'loading' })

    expect(screen.queryByRole('button', { name: 'Login' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Checking sign-in status' })).toBeDisabled()
  })

  it('links the app title to Home', () => {
    renderWith({ status: 'signedOut' })

    expect(screen.getByRole('link', { name: 'OAuth2.0 Learn' })).toHaveAttribute('href', '/')
  })
})
