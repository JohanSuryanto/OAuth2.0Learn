import { act, render, screen, waitFor } from '@testing-library/react'
import { useEffect } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as authApi from './authApi'
import { AuthProvider } from './AuthContext'
import * as loginPopup from './loginPopup'
import { MESSAGES, useAuth, type AuthContextValue } from './useAuth'

vi.mock('./authApi', () => ({ fetchMe: vi.fn(), logout: vi.fn() }))
vi.mock('./loginPopup', () => ({ openLoginPopup: vi.fn() }))

const fetchMe = vi.mocked(authApi.fetchMe)
const logout = vi.mocked(authApi.logout)
const openLoginPopup = vi.mocked(loginPopup.openLoginPopup)

const probe: { auth?: AuthContextValue } = {}

const me = (name: string | null = null, session: authApi.SessionTimingPayload | null = null) => ({
  user: { email: 'a@b.com', name },
  session,
})

function Probe() {
  const auth = useAuth()
  useEffect(() => {
    probe.auth = auth
  })
  const { state } = auth
  return (
    <div>
      <span data-testid="status">{state.status}</span>
      {state.status === 'signedIn' && <span data-testid="email">{state.user.email}</span>}
      {state.status !== 'loading' && state.error && <span data-testid="error">{state.error}</span>}
      {state.status === 'signedOut' && state.sessionEnded && <span data-testid="ended">ended</span>}
    </div>
  )
}

function renderProvider() {
  return render(
    <AuthProvider>
      <Probe />
    </AuthProvider>,
  )
}

describe('AuthProvider', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('starts loading and becomes signedIn when a session exists', async () => {
    fetchMe.mockResolvedValue(me())
    renderProvider()

    expect(screen.getByTestId('status')).toHaveTextContent('loading')
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))
    expect(screen.getByTestId('email')).toHaveTextContent('a@b.com')
  })

  it('becomes signedOut when there is no session', async () => {
    fetchMe.mockResolvedValue(null)
    renderProvider()

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'))
  })

  it('becomes signedOut without throwing when the backend is unreachable', async () => {
    fetchMe.mockRejectedValue(new TypeError('Failed to fetch'))
    renderProvider()

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'))
  })

  it('refreshes after a successful popup login', async () => {
    fetchMe.mockResolvedValueOnce(null).mockResolvedValue(me('A'))
    openLoginPopup.mockResolvedValue({ success: true })
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'))

    await act(() => probe.auth!.login())

    expect(screen.getByTestId('status')).toHaveTextContent('signedIn')
  })

  it.each([
    ['access_denied', MESSAGES.cancelled],
    ['signin_failed', MESSAGES.failed],
    ['popup_blocked', MESSAGES.popupBlocked],
  ] as const)('shows a friendly message for %s', async (error, message) => {
    fetchMe.mockResolvedValue(null)
    openLoginPopup.mockResolvedValue({ success: false, error })
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'))

    await act(() => probe.auth!.login())

    expect(screen.getByTestId('status')).toHaveTextContent('signedOut')
    expect(screen.getByTestId('error')).toHaveTextContent(message)
  })

  it('signs out after a successful logout', async () => {
    fetchMe.mockResolvedValue(me())
    logout.mockResolvedValue()
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))

    await act(() => probe.auth!.logout())

    expect(screen.getByTestId('status')).toHaveTextContent('signedOut')
  })

  it('stays signed in with an error when logout fails', async () => {
    fetchMe.mockResolvedValue(me())
    logout.mockRejectedValue(new Error('503'))
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))

    await act(() => probe.auth!.logout())

    expect(screen.getByTestId('status')).toHaveTextContent('signedIn')
    expect(screen.getByTestId('error')).toHaveTextContent(MESSAGES.logoutFailed)
  })
  it('flags sessionEnded when a signed-in session gets a 401', async () => {
    fetchMe.mockResolvedValueOnce(me()).mockResolvedValue(null)
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))

    await act(() => probe.auth!.refresh())

    expect(screen.getByTestId('status')).toHaveTextContent('signedOut')
    expect(screen.getByTestId('ended')).toBeInTheDocument()

    act(() => probe.auth!.clearError())
    expect(screen.queryByTestId('ended')).not.toBeInTheDocument()
  })

  it('does not flag sessionEnded after a normal logout', async () => {
    fetchMe.mockResolvedValue(me())
    logout.mockResolvedValue()
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))

    await act(() => probe.auth!.logout())

    expect(screen.getByTestId('status')).toHaveTextContent('signedOut')
    expect(screen.queryByTestId('ended')).not.toBeInTheDocument()
  })

  it('does not flag sessionEnded when the backend is unreachable', async () => {
    fetchMe.mockResolvedValueOnce(me()).mockRejectedValue(new TypeError('Failed to fetch'))
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))

    await act(() => probe.auth!.refresh())

    expect(screen.getByTestId('status')).toHaveTextContent('signedOut')
    expect(screen.queryByTestId('ended')).not.toBeInTheDocument()
  })

  it('does not flag sessionEnded when the first check finds no session', async () => {
    fetchMe.mockResolvedValue(null)
    renderProvider()

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedOut'))
    expect(screen.queryByTestId('ended')).not.toBeInTheDocument()
  })

  it('stores session timing with the time it arrived', async () => {
    const session = { idleSecondsLeft: 3600, idleExpiresAt: 'x', absoluteSecondsLeft: 28800, absoluteExpiresAt: 'y' }
    fetchMe.mockResolvedValue(me(null, session))
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))

    const state = probe.auth!.state
    expect(state.status === 'signedIn' && state.session).toMatchObject({ ...session, receivedAt: expect.any(Number) })
  })

  it('re-checks the session when a page is restored from the back/forward cache', async () => {
    fetchMe.mockResolvedValue(me())
    renderProvider()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('signedIn'))
    fetchMe.mockClear()

    const event = new Event('pageshow') as PageTransitionEvent
    Object.defineProperty(event, 'persisted', { value: true })
    await act(async () => {
      window.dispatchEvent(event)
    })

    expect(fetchMe).toHaveBeenCalledTimes(1)
  })
})