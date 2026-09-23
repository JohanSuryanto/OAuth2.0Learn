import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthContextValue, SessionTiming } from '../auth/useAuth'
import { SessionExpiryWarning } from './SessionExpiryWarning'

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

let clock = 0
const advance = (ms: number) =>
  act(() => {
    clock += ms
    vi.advanceTimersByTime(ms)
  })

function signedInWith(idle: number, absolute: number) {
  const session: SessionTiming = {
    idleSecondsLeft: idle,
    idleExpiresAt: '2026-09-23T09:00:00Z',
    absoluteSecondsLeft: absolute,
    absoluteExpiresAt: '2026-09-23T16:00:00Z',
    receivedAt: clock,
  }
  auth.state = { status: 'signedIn', user: { email: 'a@b.com', name: null }, session }
}

const renderWarning = () =>
  render(
    <MemoryRouter>
      <SessionExpiryWarning />
    </MemoryRouter>,
  )

describe('SessionExpiryWarning', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: false })
    vi.clearAllMocks()
    clock = 1_000
    vi.spyOn(performance, 'now').mockImplementation(() => clock)
    fetchSpy = vi.spyOn(globalThis, 'fetch')
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('stays hidden with more than 2 minutes left and appears at 2:00', () => {
    signedInWith(180, 28800)
    renderWarning()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()

    advance(60_000)
    expect(screen.getByRole('alertdialog', { name: "You'll be signed out in 2:00" })).toBeInTheDocument()

    advance(1000)
    expect(screen.getByRole('alertdialog', { name: "You'll be signed out in 1:59" })).toBeInTheDocument()
  })

  it('offers Stay signed in, which extends the session', async () => {
    vi.useRealTimers()
    signedInWith(100, 28800)
    renderWarning()

    await userEvent.click(screen.getByRole('button', { name: 'Stay signed in' }))

    expect(auth.extendSession).toHaveBeenCalledTimes(1)
    expect(auth.logout).not.toHaveBeenCalled()
  })

  it('offers Sign out, which logs out', async () => {
    vi.useRealTimers()
    signedInWith(100, 28800)
    renderWarning()

    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(auth.logout).toHaveBeenCalledTimes(1)
  })

  it('cannot extend when the 8-hour limit comes first', async () => {
    vi.useRealTimers()
    signedInWith(3000, 90)
    renderWarning()

    expect(screen.getByText("Your session can't be extended any further.")).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stay signed in' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'OK' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('never talks to the server while showing', () => {
    signedInWith(119, 28800)
    renderWarning()

    advance(100_000)

    expect(fetchSpy).not.toHaveBeenCalled()
    expect(auth.extendSession).not.toHaveBeenCalled()
    expect(auth.refresh).not.toHaveBeenCalled()
  })

  it('shows nothing when signed out', () => {
    auth.state = { status: 'signedOut' }
    renderWarning()

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })
})
