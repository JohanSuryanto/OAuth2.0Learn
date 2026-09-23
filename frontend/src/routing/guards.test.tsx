import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../App'
import * as authApi from '../auth/authApi'
import type { AuthContextValue, AuthState } from '../auth/useAuth'

const auth: AuthContextValue = {
  state: { status: 'loading' },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}

vi.mock('../auth/useAuth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../auth/useAuth')>()),
  useAuth: () => auth,
}))
vi.mock('../auth/loginDialogContext', () => ({ useLoginDialog: () => ({ isOpen: false, open: vi.fn(), close: vi.fn() }) }))
vi.mock('../auth/authApi', () => ({ fetchAccount: vi.fn() }))

const signedIn: AuthState = { status: 'signedIn', user: { email: 'a@b.com', name: null }, session: null }

function renderAt(path: string, state: AuthState) {
  auth.state = state
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  )
}

describe('routing guards', () => {
  beforeEach(() => {
    vi.mocked(authApi.fetchAccount).mockResolvedValue({
      email: 'a@b.com',
      displayName: null,
      methods: ['password'],
      createdAt: '2026-09-23T08:00:00Z',
      lastSignInAt: '2026-09-23T08:00:00Z',
    })
  })

  it.each(['/', '/dashboard'])('shows only a loading state at %s while the status is unknown', (path) => {
    const { container } = renderAt(path, { status: 'loading' })

    expect(container.querySelector('[aria-busy="true"]')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /Learn OAuth/ })).not.toBeInTheDocument()
    expect(screen.queryByText('Sign-in methods')).not.toBeInTheDocument()
  })

  it('sends signed-out visitors from /dashboard to Home without showing account details', () => {
    renderAt('/dashboard', { status: 'signedOut' })

    expect(screen.getByRole('heading', { name: 'Learn OAuth 2.0 by signing in' })).toBeInTheDocument()
    expect(screen.queryByText('Sign-in methods')).not.toBeInTheDocument()
    expect(authApi.fetchAccount).not.toHaveBeenCalled()
  })

  it('sends signed-in users from / to the Dashboard', async () => {
    renderAt('/', signedIn)

    expect(await screen.findByText('Sign-in methods')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Learn OAuth 2.0 by signing in' })).not.toBeInTheDocument()
  })

  it('sends signed-out visitors from /settings to Home', () => {
    renderAt('/settings', { status: 'signedOut' })

    expect(screen.getByRole('heading', { name: 'Learn OAuth 2.0 by signing in' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Account settings' })).not.toBeInTheDocument()
  })

  it('shows Page not found for unknown paths', () => {
    renderAt('/nope', { status: 'signedOut' })

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to Home' })).toHaveAttribute('href', '/')
  })

  it('keeps the verify-email and reset-password pages reachable', () => {
    renderAt('/reset-password', { status: 'signedOut' })

    expect(screen.getByRole('heading', { name: 'Choose a new password' })).toBeInTheDocument()
  })
})
