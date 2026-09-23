import { useEffect, useRef, useState } from 'react'
import { useAuth, type SessionTiming } from '../auth/useAuth'
import { formatDuration, secondsLeft } from './sessionTime'

const ENDING_SOON_SECONDS = 5 * 60
const CHECK_DELAY_MS = 2000

/**
 * Live countdowns for the two session limits (spec 003, FR-015–FR-018).
 * Counts from the server-reported seconds using performance.now(), so the computer clock doesn't matter,
 * and never talks to the server while ticking — only once, 2 s after reaching zero (research R4).
 */
export function SessionCountdown({ session }: { session: SessionTiming }) {
  const { refresh } = useAuth()
  const [now, setNow] = useState(() => performance.now())
  const checkedFor = useRef<number | null>(null)

  useEffect(() => {
    const tick = setInterval(() => setNow(performance.now()), 1000)
    return () => clearInterval(tick)
  }, [])

  const idle = secondsLeft(session.idleSecondsLeft, session.receivedAt, now)
  const absolute = secondsLeft(session.absoluteSecondsLeft, session.receivedAt, now)
  const first = Math.min(idle, absolute)

  useEffect(() => {
    if (first > 0 || checkedFor.current === session.receivedAt) return
    checkedFor.current = session.receivedAt
    const check = setTimeout(() => void refresh(), CHECK_DELAY_MS)
    return () => clearTimeout(check)
  }, [first, session.receivedAt, refresh])

  const rows = [
    { key: 'idle', label: 'Inactivity limit', left: idle, endsAt: session.idleExpiresAt, first: idle <= absolute },
    { key: 'absolute', label: 'Absolute limit', left: absolute, endsAt: session.absoluteExpiresAt, first: absolute < idle },
  ]

  return (
    <ul className="countdown">
      {rows.map((row) => (
        <li
          key={row.key}
          className={row.left < ENDING_SOON_SECONDS ? 'countdown__row ending-soon' : 'countdown__row'}
          data-testid={`countdown-${row.key}`}
        >
          <span className="countdown__label">
            {row.label}
            {row.first && <span className="badge">Ends first</span>}
          </span>
          <span className="countdown__time" aria-label={`${row.label}: ${formatDuration(row.left)} left`}>
            {formatDuration(row.left)} left
          </span>
          <span className="countdown__end">ends at {new Date(row.endsAt).toLocaleTimeString()}</span>
        </li>
      ))}
    </ul>
  )
}
