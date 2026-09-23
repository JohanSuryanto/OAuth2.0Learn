export type AuthUser = { email: string; name: string | null }

/** When the server will end the session (spec 003 contract). Seconds are relative to the response. */
export type SessionTimingPayload = {
  idleSecondsLeft: number
  idleExpiresAt: string
  absoluteSecondsLeft: number
  absoluteExpiresAt: string
}

export type MeResult = { user: AuthUser; session: SessionTimingPayload | null }

export type AccountSummary = {
  email: string
  displayName: string | null
  methods: ('google' | 'password')[]
  createdAt: string
  lastSignInAt: string
}

/** Returns the signed-in user and session timing, or null when not signed in (401). Throws on network/other errors. */
export async function fetchMe(): Promise<MeResult | null> {
  const response = await fetch('/api/auth/me', { credentials: 'include' })
  if (response.status === 401) return null
  if (!response.ok) throw new Error(`GET /api/auth/me failed with ${response.status}`)
  const body = (await response.json()) as AuthUser & { session?: SessionTimingPayload | null }
  return { user: { email: body.email, name: body.name }, session: body.session ?? null }
}

/** The Dashboard's account summary, or null when not signed in (401). Throws on network/other errors. */
export async function fetchAccount(): Promise<AccountSummary | null> {
  const response = await fetch('/api/account', { credentials: 'include' })
  if (response.status === 401) return null
  if (!response.ok) throw new Error(`GET /api/account failed with ${response.status}`)
  return (await response.json()) as AccountSummary
}

/** Ends the session server-side (revokes all of the user's sessions). Throws unless the server returns 204. */
export async function logout(): Promise<void> {
  const response = await fetch('/api/auth/logout', {
    method: 'POST',
    credentials: 'include',
    headers: { 'X-Requested-With': 'fetch' },
  })
  if (response.status !== 204) throw new Error(`POST /api/auth/logout failed with ${response.status}`)
}
