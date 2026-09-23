# Contract: Auth API (backend `https://localhost:5001`)

The browser reaches every endpoint under `/api` through the Vite proxy at `http://localhost:5174/api/...`. The exceptions are `login` and `popup-complete`, which the popup window opens directly on `https://localhost:5001`.

## `GET /api/auth/login`

Starts the Google OAuth challenge. It's opened in the popup window.

| | |
|---|---|
| Auth | none |
| Response | `302` to `https://accounts.google.com/o/oauth2/v2/auth?...` (the challenge adds `client_id`, `redirect_uri=https://localhost:5001/signin-google`, `scope=openid profile email`, `state`, `code_challenge`) |
| Side effects | Sets the correlation/nonce cookies used by the Google handler |

The redirect target after success is fixed server-side to `/api/auth/popup-complete`. The endpoint doesn't accept a `returnUrl`, so it can't be used as an open redirect.

## `GET /signin-google` (handled by middleware)

This is the Google redirect URI, and the app has no code for it: the `Google` handler exchanges the code for tokens, `OnCreatingTicket` upserts the user, and the handler then signs in the cookie scheme.
- Success → `302 /api/auth/popup-complete`
- User cancelled / declined consent (Google `error=access_denied`, handled in `OnAccessDenied`) → `302 /api/auth/popup-complete?error=access_denied`
- Any other failure (a DB error, missing email, a bad state; handled in `OnRemoteFailure`) → `302 /api/auth/popup-complete?error=signin_failed`

## `GET /api/auth/popup-complete?error={code}`

Returns `302` to `{Frontend:Origin}/auth-complete.html?result={success|access_denied|signin_failed}`. The origin comes only from config. A missing `error` maps to `success`, and unknown values map to `signin_failed`.

## Frontend page `http://localhost:5174/auth-complete.html?result=...`

A static page on the **dashboard's own origin**. It sends

```js
{ type: "oauth-result", success: boolean, error?: "access_denied" | "signin_failed" }
```

through **`BroadcastChannel("oauth-login")`** (primary) and through `window.opener?.postMessage(msg, location.origin)` (secondary), then calls `window.close()`.

**Why BroadcastChannel**: Google's sign-in pages may set `Cross-Origin-Opener-Policy`. That cuts the link between the popup and the dashboard, so `window.opener` becomes `null` and `popup.closed` can read `true` early. BroadcastChannel only needs the two windows to share an origin, so it isn't affected.

**FE rules**:
- Accept `message` events only when `event.origin === window.location.origin` && `data.type === "oauth-result"`. BroadcastChannel is same-origin by design.
- `popup.closed` is only a hint that triggers a `/me` refresh. It never ends the flow.

## `GET /api/auth/me`

| Case | Response |
|------|----------|
| Signed in | `200 application/json` `{ "email": "a@b.com", "name": "Alice" \| null }` |
| Not signed in / expired / revoked (old `session_version`) / older than 8 h / DB unreachable | `401` (empty body; no redirect) |

## `POST /api/auth/logout`

| | |
|---|---|
| Auth | cookie (if absent, it still returns 204, so the call is idempotent) |
| Required header | `X-Requested-With: fetch` (without it: `400`). This is a CSRF guard on top of `SameSite=Lax` |
| Response | `204 No Content`. The auth cookie is deleted (`Set-Cookie` expired) |
| Effect | If signed in, increments `users.session_version`, which revokes **all** of this user's sessions, including copies of the cookie (FR-019). Then deletes the cookie. The user isn't signed out of Google, and the `users` row is kept (FR-009, FR-016) |

## CORS

The backend allows origin `http://localhost:5174` only, with credentials, methods `GET, POST`, and header `X-Requested-With`. No wildcard.

## Error codes (popup)

| `error` | Meaning | FE message |
|---------|---------|------------|
| `access_denied` | User cancelled or declined on Google | "Sign-in was cancelled." |
| `signin_failed` | Any other failure (DB down, no email, unverified email, invalid state) | "Sign-in failed. Please try again." |
| *(FE-local)* `popup_blocked` | `window.open` returned null | "Please allow popups for this site to sign in." |
| *(FE-local)* `timeout` | No result within 5 min (e.g., the user closed the popup) | none. Re-check `/me` silently |

## Security headers (every backend response)

`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`.

`auth-complete.html` (served by the frontend) declares `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'self'">` and loads `/auth-complete.js`. It contains no inline script.

## Challenge behavior

`DefaultChallengeScheme` is **Cookie**, so every protected endpoint returns `401` (with no `Location` header) when the user isn't signed in. Only `/api/auth/login` challenges Google, and it names the scheme explicitly.
