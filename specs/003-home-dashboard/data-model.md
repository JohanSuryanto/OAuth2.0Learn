# Data Model: Home Page and Signed-in Dashboard

**Feature**: [spec.md](./spec.md) | **Date**: 2026-09-23

**No database changes.** Everything is read from existing data: the `users` and `password_credentials` tables (features 001/002) and the auth cookie ticket and claims.

## Account Summary (read model, `GET /api/account`)

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| `email` | string | `users.email` | |
| `displayName` | string \| null | `users.display_name` | UI shows "Not set" when null |
| `methods` | `("google" \| "password")[]` | `google_subject IS NOT NULL` → `"google"`; `password_credentials.active_hash IS NOT NULL` → `"password"` | order: google, password; a pending hash is **not** listed (FR-012) |
| `createdAt` | ISO 8601 UTC | `users.created_at` | shown in local time |
| `lastSignInAt` | ISO 8601 UTC | `users.last_login_at` | shown in local time |

Looked up by the `app_user_id` claim of the current session. If the user is missing, the endpoint returns `401` (`SessionValidator` would normally have rejected the session already).

## Session Timing (read model, added to `GET /api/auth/me`)

| Field | Type | Source |
|-------|------|--------|
| `idleSecondsLeft` | integer ≥ 0 | `idleExpiresAt − now` |
| `idleExpiresAt` | ISO 8601 UTC | cookie `ExpiresUtc`, or `now + 60 min` if this request renews the cookie (research R3) |
| `absoluteSecondsLeft` | integer ≥ 0 | `absoluteExpiresAt − now` |
| `absoluteExpiresAt` | ISO 8601 UTC | `auth_time` claim + `Auth:AbsoluteSessionLifetime` (8 h) |

`now` comes from the injected `TimeProvider`. `session` is `null` when there is no cookie ticket (for example, with the test auth scheme).

## Frontend state

```ts
type SessionTiming = {
  idleSecondsLeft: number
  idleExpiresAt: string
  absoluteSecondsLeft: number
  absoluteExpiresAt: string
  receivedAt: number // performance.now() when the /me response arrived; countdowns count from here
}

type AuthState =
  | { status: 'loading' }
  | { status: 'signedOut'; error?: string; sessionEnded?: boolean }       // + sessionEnded (research R5)
  | { status: 'signedIn'; user: AuthUser; session: SessionTiming | null; error?: string }  // + session

type AccountSummary = {
  email: string
  displayName: string | null
  methods: ('google' | 'password')[]
  createdAt: string
  lastSignInAt: string
}
```

### Transitions added by this feature

```text
signedIn ──/me 401──────────────────▶ signedOut { sessionEnded: true }  → Home shows "Your session has ended…"
signedIn ──logout()─────────────────▶ signedOut                          → navigate('/')
signedOut{sessionEnded} ──open dialog / sign in──▶ flag cleared
```
