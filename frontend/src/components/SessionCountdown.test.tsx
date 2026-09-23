import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthContextValue, SessionTiming } from '../auth/useAuth'
import { SessionCountdown } from './SessionCountdown'

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

// A controllable monotonic clock for performance.now().
let clock = 0
const advance = (ms: number) =>
  act(() => {
    clock += ms
    vi.advanceTimersByTime(ms)
  })

function timing(idle: number, absolute: number): SessionTiming {
  return {
    idleSecondsLeft: idle,
    idleExpiresAt: '2026-09-23T09:00:00Z',
    absoluteSecondsLeft: absolute,
    absoluteExpiresAt: '2026-09-23T16:00:00Z',
    receivedAt: clock,
  }
}

const row = (key: 'idle' | 'absolute') => screen.getByTestId(`countdown-${key}`)

describe('SessionCountdown', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
    clock = 1_000
    vi.spyOn(performance, 'now').mockImplementation(() => clock)
    fetchSpy = vi.spyOn(globalThis, 'fetch')
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('starts at the server-reported time left and ticks every second', () => {
    render(<SessionCountdown session={timing(3600, 28800)} />)

    expect(row('idle')).toHaveTextContent('1:00:00 left')
    expect(row('absolute')).toHaveTextContent('8:00:00 left')

    advance(1000)

    expect(row('idle')).toHaveTextContent('0:59:59 left')
    expect(row('absolute')).toHaveTextContent('7:59:59 left')
  })

  it('marks the limit that ends first', () => {
    render(<SessionCountdown session={timing(3600, 28800)} />)

    expect(row('idle')).toHaveTextContent('Ends first')
    expect(row('absolute')).not.toHaveTextContent('Ends first')
  })

  it('marks the absolute limit when it comes first', () => {
    render(<SessionCountdown session={timing(3600, 600)} />)

    expect(row('absolute')).toHaveTextContent('Ends first')
    expect(row('idle')).not.toHaveTextContent('Ends first')
  })

  it('highlights a countdown with less than 5 minutes left', () => {
    render(<SessionCountdown session={timing(301, 28800)} />)
    expect(row('idle')).not.toHaveClass('ending-soon')

    advance(2000)

    expect(row('idle')).toHaveClass('ending-soon')
    expect(row('absolute')).not.toHaveClass('ending-soon')
  })

  it('checks with the server exactly once, 2 seconds after reaching zero', () => {
    render(<SessionCountdown session={timing(3, 28800)} />)

    advance(3000)
    expect(row('idle')).toHaveTextContent('0:00:00 left')
    expect(auth.refresh).not.toHaveBeenCalled()

    advance(2000)
    expect(auth.refresh).toHaveBeenCalledTimes(1)

    advance(10_000)
    expect(auth.refresh).toHaveBeenCalledTimes(1)
  })

  it('never talks to the server while ticking', () => {
    render(<SessionCountdown session={timing(3600, 28800)} />)

    advance(10 * 60 * 1000)

    expect(fetchSpy).not.toHaveBeenCalled()
    expect(auth.refresh).not.toHaveBeenCalled()
  })

  it('re-syncs when new timing arrives', () => {
    const { rerender } = render(<SessionCountdown session={timing(3600, 28800)} />)
    advance(60_000)
    expect(row('idle')).toHaveTextContent('0:59:00 left')

    rerender(<SessionCountdown session={timing(3600, 28740)} />)

    expect(row('idle')).toHaveTextContent('1:00:00 left')
  })

  it('shows the end time in local time', () => {
    render(<SessionCountdown session={timing(3600, 28800)} />)

    expect(row('idle')).toHaveTextContent(`ends at ${new Date('2026-09-23T09:00:00Z').toLocaleTimeString()}`)
  })
})
