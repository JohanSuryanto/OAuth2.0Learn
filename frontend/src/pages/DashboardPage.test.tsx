import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as authApi from '../auth/authApi'
import type { AuthContextValue, AuthState } from '../auth/useAuth'
import { DashboardPage } from './DashboardPage'

const auth: AuthContextValue = {
  state: { status: 'signedOut' },
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
vi.mock('../auth/authApi', () => ({ fetchAccount: vi.fn() }))

const fetchAccount = vi.mocked(authApi.fetchAccount)

const account = (overrides: Partial<authApi.AccountSummary> = {}): authApi.AccountSummary => ({
  email: 'alice@example.com',
  displayName: 'Alice',
  methods: ['google'],
  createdAt: '2026-09-01T10:00:00Z',
  lastSignInAt: '2026-09-23T08:00:00Z',
  ...overrides,
})

const signedIn = (session: Extract<AuthState, { status: 'signedIn' }>['session'] = null): AuthState => ({
  status: 'signedIn',
  user: { email: 'alice@example.com', name: 'Alice' },
  session,
})

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    auth.state = signedIn()
  })

  afterEach(() => vi.useRealTimers())

  it('welcomes the user by display name and lists the account details', async () => {
    fetchAccount.mockResolvedValue(account())
    render(<DashboardPage />)

    expect(await screen.findByRole('heading', { name: 'Welcome, Alice' })).toBeInTheDocument()
    expect(screen.getByText('alice@example.com')).toBeInTheDocument()
    expect(screen.getByText('Google')).toBeInTheDocument()
    expect(screen.getByText(new Date('2026-09-01T10:00:00Z').toLocaleString())).toBeInTheDocument()
    expect(screen.getByText(new Date('2026-09-23T08:00:00Z').toLocaleString())).toBeInTheDocument()
  })

  it('falls back to the email and "Not set" without a display name', async () => {
    fetchAccount.mockResolvedValue(account({ displayName: null }))
    render(<DashboardPage />)

    expect(await screen.findByRole('heading', { name: 'Welcome, alice@example.com' })).toBeInTheDocument()
    expect(screen.getByText('Not set')).toBeInTheDocument()
  })

  it('lists both sign-in methods', async () => {
    fetchAccount.mockResolvedValue(account({ methods: ['google', 'password'] }))
    render(<DashboardPage />)

    expect(await screen.findByText('Google and Password')).toBeInTheDocument()
  })

  it('shows an error without partial data and retries on request', async () => {
    fetchAccount.mockRejectedValueOnce(new Error('offline')).mockResolvedValue(account())
    render(<DashboardPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't load your account details. Please try again.")
    expect(screen.queryByText('Sign-in methods')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { name: 'Welcome, Alice' })).toBeInTheDocument()
  })

  it('fetches the account once and never on a timer', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    fetchAccount.mockResolvedValue(account())
    render(<DashboardPage />)
    await screen.findByRole('heading', { name: 'Welcome, Alice' })

    await act(async () => {
      vi.advanceTimersByTime(120_000)
    })

    expect(fetchAccount).toHaveBeenCalledTimes(1)
  })

  it('shows the session section only when timing is available', async () => {
    fetchAccount.mockResolvedValue(account())
    auth.state = signedIn({
      idleSecondsLeft: 3600,
      idleExpiresAt: '2026-09-23T09:00:00Z',
      absoluteSecondsLeft: 28800,
      absoluteExpiresAt: '2026-09-23T16:00:00Z',
      receivedAt: performance.now(),
    })
    render(<DashboardPage />)

    expect(await screen.findByRole('region', { name: 'Session' })).toBeInTheDocument()
  })
})
