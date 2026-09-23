# Contract: API Additions and App Routes

## API (backend `https://localhost:5001`, called via the Vite proxy)

### `GET /api/auth/me` (extended, backward compatible)

`200`:

```json
{
  "email": "a@b.com",
  "name": "Alice",
  "session": {
    "idleSecondsLeft": 3598,
    "idleExpiresAt": "2026-09-23T09:00:00+00:00",
    "absoluteSecondsLeft": 28798,
    "absoluteExpiresAt": "2026-09-23T16:00:00+00:00"
  }
}
```

- `session` is `null` when the request has no cookie ticket (test scheme only).
- `idleExpiresAt` accounts for this request renewing the sliding cookie ([research R3](../research.md#r3-computing-when-will-the-server-end-this-session)).
- `401` is unchanged.

### `GET /api/account` (new)

| Case | Response |
|------|----------|
| Signed in | `200 { "email", "displayName": string\|null, "methods": ["google"?, "password"?], "createdAt", "lastSignInAt" }`, with ISO 8601 UTC times |
| Not signed in, expired, or revoked | `401` (no redirect) |

Read-only and `RequireAuthorization()`, with no CSRF filter because it's a GET with no side effects.

### Headers

Every response under `/api/` also carries `Cache-Control: no-store` (research R8).

---

## App routes (frontend `http://localhost:5174`)

| Path | Signed out | Signed in | Loading |
|------|------------|-----------|---------|
| `/` | Home | → `/dashboard` (replace) | loading placeholder |
| `/dashboard` | → `/` (replace) | Dashboard | loading placeholder |
| `/verify-email` | page (unchanged); on success → `/dashboard` | page | page |
| `/reset-password` | page (unchanged) | page | page |
| anything else | "Page not found" + link to Home | same | same |

**Navigation events**
- Logout (header) → `navigate('/')`.
- `/me` returns 401 while signed in → `signedOut { sessionEnded: true }` → the Dashboard guard redirects to `/` → Home shows "Your session has ended. Please sign in again."
- Countdown reaching zero → wait 2 s → `refresh()` once.

## UI texts

| Place | Text |
|-------|------|
| Home heading | "Learn OAuth 2.0 by signing in" |
| Home body | "This app shows how signing in works: with your Google account (OAuth 2.0) or with an email and password. Sign in to see your account and session details." |
| Home button | "Get started" |
| Session ended notice | "Your session has ended. Please sign in again." |
| Dashboard heading | "Welcome, {displayName \|\| email}" |
| Account labels | Email · Display name ("Not set") · Sign-in methods ("Google", "Password", joined with " and ") · Account created · Last sign-in |
| Session labels | "Inactivity limit" / "Absolute limit", each with `H:MM:SS left`, "ends at {local time}", and "Ends first" on the earlier one; "Ending soon" styling when < 5 min |
| Load error | "Couldn't load your account details. Please try again." + "Try again" button |
| Not found | "Page not found" + "Go to Home" link |
