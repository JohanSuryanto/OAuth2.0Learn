# Contract: Account & Security API (backend `https://localhost:5001`, via the Vite proxy)

**Common rules**
- Every endpoint below requires a signed-in cookie; without one the response is `401` (no redirect).
- Every `POST` requires `X-Requested-With: fetch` (else `400`).
- Validation errors use `400 { "errors": { "<field>": "<code>" } }`; throttling uses `429 { "code": "too_many_attempts" }` (as in feature 002).
- `Cache-Control: no-store` on all `/api/*` responses (feature 003).

## Profile

### `POST /api/account/profile`

Request: `{ "displayName": string | null }`

| Case | Response |
|------|----------|
| OK (trimmed ≤ 100; empty/whitespace → removed) | `200` with the account summary (feature 003 shape); the current cookie is re-issued with the new `Name` |
| > 100 chars | `400 { errors: { displayName: "display_name_too_long" } }` |

## Password

### `POST /api/account/password/change`

Request: `{ "currentPassword": string, "newPassword": string }`

| Case | Response |
|------|----------|
| Account has no active password | `400 { errors: { currentPassword: "no_password" } }` |
| Throttled (same buckets as sign-in) | `429` |
| Wrong current password | `400 { errors: { currentPassword: "current_password_incorrect" } }` (counts as a sign-in failure) |
| New password breaks the 002 rules | `400 { errors: { newPassword: <002 code> } }` |
| OK | `204`: new active hash; pending cleared; other sessions revoked; `session_version++`; this cookie re-issued; `last_auth_at = now` |

### `POST /api/account/password/set`

Request: `{ "newPassword": string }`

| Case | Response |
|------|----------|
| Account already has an active password | `400 { errors: { newPassword: "password_already_set" } }` (use change instead) |
| Last authentication more than 10 min ago | `403 { code: "reauth_required", methods: ["google"] }` (`["password"]` never occurs here, since the account has no password) |
| New password breaks the 002 rules | `400 { errors: { newPassword: <code> } }` |
| OK | `204`: active hash set, pending cleared, email marked verified; other sessions are **not** ended |

## Re-authentication

### `POST /api/account/reauth/password`

Request: `{ "password": string }`

| Case | Response |
|------|----------|
| Throttled | `429` |
| Wrong password or no active password | `400 { errors: { password: "current_password_incorrect" } }` |
| OK | `204`: `last_auth_at = now` for this session |

### `GET /api/auth/reauth/google` (opened in the popup)

A `302` to Google with `prompt=select_account&max_age=0`. After Google, the popup lands on `/api/auth/popup-complete?result=…`:
- `reauth_ok`: the same Google account as linked, so this session's `last_auth_at = now`. **No new sign-in.**
- `reauth_mismatch`: a different Google account; nothing changes. UI message: "That Google account isn't the one linked to this account."
- `access_denied` / `signin_failed`: as in feature 001.

The popup result whitelist becomes `success | access_denied | signin_failed | reauth_ok | reauth_mismatch`.

## Sessions

### `GET /api/account/sessions`

`200`, with the current session first and the rest ordered by `lastSeenAt` descending:

```json
[
  { "id": "…", "device": "Chrome on Windows", "ipMasked": "127.0.0.x", "createdAt": "…", "lastSeenAt": "…", "current": true }
]
```

### `POST /api/account/sessions/{id}/revoke`

| Case | Response |
|------|----------|
| `id` is the current session | `400 { errors: { id: "use_logout" } }` |
| `id` is not this user's valid session | `404` |
| OK | `204` |

### `POST /api/account/sessions/revoke-others`

`204`: every other session is revoked, `session_version++`, and this cookie is re-issued.

## Session lifetime

### `POST /api/auth/session/extend` ("Stay signed in")

`200` with the `/api/auth/me` body (new timing). It re-issues the cookie with a new 60 min idle window; `auth_time` is unchanged, so the absolute limit doesn't move.

### `POST /api/auth/logout` (changed)

`204`. It now revokes **only the current session** (its row). A legacy cookie without a `sid` falls back to `session_version++` (the old behaviour).

## Changed read models

- `GET /api/auth/me`: `name` is the stored display name (research R8).
- `GET /api/account`: unchanged shape.

---

## UI (frontend)

| Element | Contract |
|---------|----------|
| Header menu button | label = `name ?? email`; `aria-haspopup="menu"`, `aria-expanded` |
| Menu content | email line (not focusable) → "Dashboard" (`/dashboard`) → "Account settings" (`/settings`) → "Log out" |
| `/settings` sections | **Profile** (Email read-only + "Your email can't be changed"; Display name + "Save" → "Profile updated."); **Password** ("Change password": current + new ×2 → "Password changed. Other devices have been signed out."; or "Set a password": new ×2 → "Password set. You can now sign in with your email and password."); **Sign-in methods** (read-only list: "Google – connected/not connected", "Password – set/not set"); **Where you're signed in** (rows: device, "This device" badge, ipMasked, "Signed in {local}", "Last active {local}", "Sign out" on others; "Sign out all other devices" when > 1) |
| Re-auth panel | "Confirm it's you" + "For your security, please confirm your identity to continue." + Password field & "Confirm", or "Continue with Google" |
| Expiry dialog | title "You'll be signed out in m:ss"; inactivity first → "Stay signed in" (primary) + "Sign out"; absolute first → text "Your session can't be extended any further." + "Sign out" + "OK" |
