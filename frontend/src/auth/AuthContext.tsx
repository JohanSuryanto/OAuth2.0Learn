import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import * as accountApi from './accountApi'
import * as authApi from './authApi'
import { openLoginPopup } from './loginPopup'
import { AuthContext, MESSAGES, type AuthState } from './useAuth'

const FOCUS_REFRESH_THROTTLE_MS = 30_000

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })
  // Every state change bumps this, so a slow /me response can't overwrite a newer state.
  const version = useRef(0)
  const lastFocusRefresh = useRef(0)

  const update = useCallback((next: AuthState) => {
    version.current++
    setState(next)
  }, [])

  const refresh = useCallback(async () => {
    const requestVersion = ++version.current
    let next: AuthState
    let sessionGone = false
    try {
      const me = await authApi.fetchMe()
      if (me) {
        next = {
          status: 'signedIn',
          user: me.user,
          session: me.session ? { ...me.session, receivedAt: performance.now() } : null,
        }
      } else {
        next = { status: 'signedOut' }
        sessionGone = true
      }
    } catch {
      // Backend unreachable: show the signed-out state rather than breaking (spec edge case).
      // Not proof the session ended, so no "session ended" notice.
      next = { status: 'signedOut' }
    }
    if (requestVersion !== version.current) return
    // A 401 while we thought we were signed in means the session ended (expiry, logout elsewhere, reset).
    setState((current) => (sessionGone && current.status === 'signedIn' ? { status: 'signedOut', sessionEnded: true } : next))
  }, [])

  const login = useCallback(async () => {
    const result = await openLoginPopup(() => void refresh())
    if (result.success) return refresh()
    switch (result.error) {
      case 'access_denied':
        return update({ status: 'signedOut', error: MESSAGES.cancelled })
      case 'signin_failed':
        return update({ status: 'signedOut', error: MESSAGES.failed })
      case 'popup_blocked':
        return update({ status: 'signedOut', error: MESSAGES.popupBlocked })
      default:
        return refresh()
    }
  }, [refresh, update])

  // "Stay signed in" (spec 004, research R7): the only request the expiry warning may make.
  const extendSession = useCallback(async () => {
    const me = await accountApi.extendSession()
    if (!me) {
      await refresh()
      return false
    }
    update({
      status: 'signedIn',
      user: me.user,
      session: me.session ? { ...me.session, receivedAt: performance.now() } : null,
    })
    return true
  }, [refresh, update])

  const logout = useCallback(async () => {
    try {
      await authApi.logout()
      update({ status: 'signedOut' })
    } catch {
      await refresh()
      setState((current) => (current.status === 'signedIn' ? { ...current, error: MESSAGES.logoutFailed } : current))
    }
  }, [refresh, update])

  const clearError = useCallback(() => {
    setState((current) => {
      if (current.status === 'signedOut' && (current.error || current.sessionEnded)) return { status: 'signedOut' }
      if (current.status === 'signedIn' && current.error) return { ...current, error: undefined }
      return current
    })
  }, [])

  // Detect an existing session on page load (FR-010, FR-011).
  useEffect(() => {
    void refresh()
  }, [refresh])

  // Pick up expiry, revocation, or a logout in another tab when the window regains focus.
  useEffect(() => {
    const onFocus = () => {
      const now = Date.now()
      if (now - lastFocusRefresh.current < FOCUS_REFRESH_THROTTLE_MS) return
      lastFocusRefresh.current = now
      void refresh()
    }
    window.addEventListener('focus', onFocus)
    return () => window.removeEventListener('focus', onFocus)
  }, [refresh])

  // A page restored from the back/forward cache shows a frozen UI; re-check who is signed in (spec 003, R8).
  useEffect(() => {
    const onPageShow = (event: PageTransitionEvent) => {
      if (event.persisted) void refresh()
    }
    window.addEventListener('pageshow', onPageShow)
    return () => window.removeEventListener('pageshow', onPageShow)
  }, [refresh])

  const value = useMemo(
    () => ({ state, login, logout, refresh, clearError, extendSession }),
    [state, login, logout, refresh, clearError, extendSession],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
