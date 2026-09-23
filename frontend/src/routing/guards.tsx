import type { ReactNode } from 'react'
import { Navigate } from 'react-router'
import { useAuth } from '../auth/useAuth'

/** Shown while the sign-in status is unknown, so no page (or account data) flashes (FR-006). */
export function LoadingScreen() {
  return (
    <section className="card" aria-busy="true">
      <p>Loading…</p>
    </section>
  )
}

/** Protected pages: signed-out visitors go Home without the page ever rendering (FR-003, SC-001). */
export function RequireSignedIn({ children }: { children: ReactNode }) {
  const { state } = useAuth()
  if (state.status === 'loading') return <LoadingScreen />
  if (state.status === 'signedOut') return <Navigate to="/" replace />
  return children
}

/** Public-only pages (Home): signed-in users go to the Dashboard (FR-003, FR-004). */
export function RedirectIfSignedIn({ children }: { children: ReactNode }) {
  const { state } = useAuth()
  if (state.status === 'loading') return <LoadingScreen />
  if (state.status === 'signedIn') return <Navigate to="/dashboard" replace />
  return children
}
