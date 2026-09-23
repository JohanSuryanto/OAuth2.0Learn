import { createContext, useContext } from 'react'
import type { AuthUser, SessionTimingPayload } from './authApi'

/** Server-reported timing plus when it arrived (performance.now()), so countdowns ignore the computer clock. */
export type SessionTiming = SessionTimingPayload & { receivedAt: number }

export type AuthState =
  | { status: 'loading' }
  | { status: 'signedOut'; error?: string; sessionEnded?: boolean }
  | { status: 'signedIn'; user: AuthUser; session: SessionTiming | null; error?: string }

export type AuthContextValue = {
  state: AuthState
  login: () => Promise<void>
  logout: () => Promise<void>
  refresh: () => Promise<void>
  /** Clears a sign-in error message and the "session ended" notice, e.g. when the sign-in dialog opens. */
  clearError: () => void
  /** "Stay signed in": renews the inactivity limit (spec 004). Resolves false if it failed. */
  extendSession: () => Promise<boolean>
}

export const MESSAGES = {
  cancelled: 'Sign-in was cancelled.',
  failed: 'Sign-in failed. Please try again.',
  popupBlocked: 'Please allow popups for this site to sign in.',
  logoutFailed: 'Logout failed. Please try again.',
  sessionEnded: 'Your session has ended. Please sign in again.',
} as const

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>')
  return context
}
