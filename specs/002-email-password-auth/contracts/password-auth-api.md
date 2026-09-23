# Contract: Email & Password Auth API (backend `https://localhost:5001`)

The browser calls these through the Vite proxy (`http://localhost:5174/api/...`), as in [feature 001](../../001-google-oauth-login/contracts/auth-api.md).

**Common to every endpoint below**
- Method `POST`, body `application/json`.
- The header **`X-Requested-With: fetch` is required**. Without it the response is `400` with an empty body (CSRF guard, FR-017). The same filter now also protects `/api/auth/logout`.
- Security headers and CORS as in 001.
- Error body shapes:
  - Validation: `400 { "errors": { "<field>": "<code>" } }`
  - Throttled: `429 { "code": "too_many_attempts" }`
  - Unexpected: `500` with an empty body. The UI shows "Something went wrong. Please try again."
- Passwords never appear in responses or logs.

---

## `POST /api/auth/register`

Request: `{ "email": string, "password": string, "displayName"?: string }`

| Case | Response | Side effects |
|------|----------|--------------|
| Input invalid | `400 { errors }`, codes: `email_invalid`, `password_too_short`, `password_too_long`, `password_is_email`, `password_common`, `display_name_too_long` | none |
| More than 5 registrations from this IP in the last hour | `429 { code: "too_many_attempts" }` | none |
| New email | `202 {}` | creates user (`created_via=password`, unverified) and a pending password; `verify_email` link sent |
| Email exists, account has **no active password** (Google-only, or pending) | `202 {}` | pending password replaced; older verify links invalidated; new `verify_email` link sent |
| Email exists, account **has an active password** | `202 {}` | nothing changed; "someone tried to register" notice sent |

The three `202` cases are indistinguishable: same body, and a password hash is computed in every case (SC-008). The UI shows "Check your inbox — we've sent a verification link to {email}."

Verification link: `http://localhost:5174/verify-email#token=<43-char base64url>`

## `POST /api/auth/verify-email`

Request: `{ "token": string, "password": string }`

| Case | Response | Side effects |
|------|----------|--------------|
| Token valid **and** password matches the account's pending password | `204`, plus `Set-Cookie: oauthlearn.auth=…` (signed in) | pending becomes active; `email_verified_at` set; token used; failure counters for the email cleared |
| Token unknown, expired, used, or replaced | `400 { errors: { token: "token_invalid" } }` | none |
| Token valid, password doesn't match | `400 { errors: { password: "password_mismatch" } }` | none; the token stays usable. Counts as a `signin_fail_email` failure, so it's subject to the same throttle |
| Throttled (email or IP) | `429 { code: "too_many_attempts" }` | none |

## `POST /api/auth/password/sign-in`

Request: `{ "email": string, "password": string }`

| Case | Response | Side effects |
|------|----------|--------------|
| Throttled for this email or IP (see [research R7](../research.md#r7-throttling-database-backed-event-counts)) | `429 { code: "too_many_attempts" }` | not counted |
| Password matches the **active** hash, account active | `204`, plus a new `Set-Cookie` | failure counter for the email cleared; `last_login_at` updated; rehash if needed |
| Password matches the **pending** hash | `403 { code: "email_not_verified" }` | new `verify_email` link sent (replaces the old one); not counted as a failure |
| Anything else (unknown email, wrong password, Google-only account) | `401 { code: "invalid_credentials" }` | one failure recorded for the email and one for the IP |

Always exactly two hash verifications per request (research R2).

## `POST /api/auth/forgot-password`

Request: `{ "email": string }`

| Case | Response | Side effects |
|------|----------|--------------|
| Invalid email format | `400 { errors: { email: "email_invalid" } }` | none |
| More than 3 per email or more than 10 per IP in the last hour | `202 {}` (same as success, FR-016) | none |
| No account for the email | `202 {}` | none (the event is still recorded for the rate limit) |
| Account exists | `202 {}` | `reset_password` link sent (30 min; replaces the older one) |

Reset link: `http://localhost:5174/reset-password#token=<token>`

## `POST /api/auth/reset-password`

Request: `{ "token": string, "newPassword": string }`

| Case | Response | Side effects |
|------|----------|--------------|
| Token unknown, expired, used, or replaced | `400 { errors: { token: "token_invalid" } }` | none |
| New password invalid (FR-005 against the account's email) | `400 { errors: { newPassword: <code> } }` | none; the token stays usable |
| Valid | `204` (**not** signed in) | active hash replaced; pending cleared; `email_verified_at` set if null; `session_version++`; all the user's tokens deleted; failure counters for the email cleared |

## `GET /dev/mailbox` (Development only)

`200 text/html`: the latest 50 `mailbox_messages`, newest first, with links clickable. The page's CSP is `default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'`. In any other environment the route doesn't exist (404).

---

## UI messages (frontend mapping)

| Result | Message |
|--------|---------|
| register `202` | "Check your inbox — we've sent a verification link to {email}." |
| `invalid_credentials` | "Email or password is incorrect." |
| `email_not_verified` | "Please verify your email first. We've sent you a new link." |
| `too_many_attempts` | "Too many attempts. Please try again later." |
| forgot `202` | "If an account exists for {email}, we've sent a reset link." |
| verify `204` | page shows "Your email is verified. You're signed in." and a link to the dashboard |
| reset `204` | "Your password has been changed. You can now sign in." |
| `token_invalid` | verify page: "This link is no longer valid. Sign in to get a new one."; reset page: "This link is no longer valid. Request a new one." |
| `password_mismatch` | "That's not the password you chose when registering." |
| `password_common` | "This password is too common. Choose a less predictable one." |
