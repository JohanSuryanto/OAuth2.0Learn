# OAuth2.0 Learn

A small learning project for **Google OAuth 2.0** (Authorization Code flow) and **email & password**
sign-in: a React + Vite dashboard (`http://localhost:5174`) with a Login/Logout button, backed by an
ASP.NET Core 9 API (`https://localhost:5001`) and PostgreSQL.

- Google sign-in: [`specs/001-google-oauth-login/`](specs/001-google-oauth-login/) (setup: its `quickstart.md`)
- Email & password: [`specs/002-email-password-auth/`](specs/002-email-password-auth/) (extra setup: its `quickstart.md`,
  including the `Auth:LookupHashKey` secret)
- Development mailbox (emails the app "sends"): **https://localhost:5001/dev/mailbox**

```text
backend/OAuthLearn.Api/         ASP.NET Core API (auth endpoints, EF Core + Npgsql)
backend/OAuthLearn.Api.Tests/   xUnit tests
frontend/                       React + Vite dashboard
```

## Pages

| Address | Signed out | Signed in |
|---------|------------|-----------|
| `/` | **Home**: what the app demonstrates + "Get started" (opens the sign-in dialog) | redirected to `/dashboard` |
| `/dashboard` | redirected to `/` (no account data is ever rendered) | **Dashboard**: account details, sign-in methods, live session countdowns |
| `/verify-email`, `/reset-password` | email links (feature 002); verifying signs you in and opens the Dashboard | same |
| anything else | "Page not found" | same |

While the app is still checking whether you're signed in it shows a neutral "Loading…", so neither page
flashes. Logout goes to Home; if the session ends on its own (inactivity, 8-hour cap, logout in another tab,
password reset) you land on Home with "Your session has ended. Please sign in again."
Spec: [`specs/003-home-dashboard/`](specs/003-home-dashboard/).

## Account settings

Signed in, the header shows a **menu button** with your name (or email): Dashboard · Account settings · Log out.
It works with the keyboard (Enter/Space/↓ to open, arrows, Home/End, Esc).

`/settings` (spec: [`specs/004-account-settings/`](specs/004-account-settings/)):

- **Profile**: change your display name; your email is shown but can't be changed.
- **Password**: *change* it (always needs your current password; your other devices are signed out, this one
  stays signed in), or *set* one if you only use Google.
- **Confirm it's you**: setting a password more than 10 minutes after you signed in first asks you to confirm —
  with your password, or with a fresh Google sign-in (the popup asks Google to re-prompt, `max_age=0`, and only
  the Google account linked to yours is accepted; it doesn't start a new session).
- **Sign-in methods**: read-only (Google connected? password set?).
- **Where you're signed in**: every device with browser/OS, a partial IP (`203.0.113.x`), signed-in and last-active
  times; sign out one device or all others.
- **"You'll be signed out in m:ss"**: 2 minutes before a limit, a dialog offers *Stay signed in* (renews the
  60-minute window; never past the 8-hour limit) or *Sign out*. The dialog itself never contacts the server.

## How the login works

```text
Dashboard (5174)          Popup window                 API (5001)                    Google
      |  click Login           |                            |                           |
      |--window.open---------->| GET /api/auth/login ------>|                           |
      |                        |<-- 302 accounts.google.com (client_id, state, PKCE) ---|
      |                        |----------------- user signs in + consents ------------>|
      |                        |<-- 302 /signin-google?code=...&state=... --------------|
      |                        | GET /signin-google ------->| check state, exchange code|
      |                        |                            |--- code + client_secret ->|
      |                        |                            |<-- tokens + profile ------|
      |                        |                            | verified email? upsert    |
      |                        |                            | users row, set HttpOnly   |
      |                        |<-- 302 /api/auth/popup-complete -> 302 5174/auth-complete.html
      |<-- BroadcastChannel "oauth-result" --| window.close()                            |
      | GET /api/auth/me (via Vite proxy) ---------------->|                            |
      |<-- 200 { email } -------------------------------- |                            |
      |  shows "Hi {email}" + Logout                        |                            |
```

Key ideas:

1. **The client secret never reaches the browser.** The backend is a *confidential client*: it
   swaps the one-time `code` for tokens server-to-server. ASP.NET Core's Google handler adds
   `state` (CSRF protection for the callback) and PKCE automatically.
2. **Google's tokens are not kept.** The backend only needs the identity (`sub`, email, name) and
   issues its **own** session cookie (`HttpOnly`, `Secure`, `SameSite=Lax`).
3. **Users are matched by Google `sub`,** never by email: emails can change or be reused.
4. **Why a popup + BroadcastChannel?** Google won't render its sign-in page inside an iframe/modal,
   and its pages may send `Cross-Origin-Opener-Policy`, which can cut `window.opener`. So the popup
   ends on a page of the dashboard's *own* origin, which reports the result via `BroadcastChannel`.
5. **Why the Vite proxy?** `http://localhost:5174` → `https://localhost:5001` is cross-*site*
   (different scheme). Proxying `/api` makes API calls same-origin, so the cookie is sent normally.

## How email & password works

```text
Create account ─▶ POST /api/auth/register ─▶ user (inactive) + pending password hash
                                          └─▶ mailbox: {origin}/verify-email#token=…
Open link      ─▶ page asks for the password you chose
               ─▶ POST /api/auth/verify-email {token, password} ─▶ password active, email verified,
                                                                  same session cookie as Google
Sign in        ─▶ POST /api/auth/password/sign-in ─▶ same session cookie as Google
Forgot         ─▶ POST /api/auth/forgot-password ─▶ mailbox: {origin}/reset-password#token=… (30 min)
               ─▶ POST /api/auth/reset-password ─▶ new password, all sessions end
```

Key ideas:

1. **Passwords are hashed, never stored.** ASP.NET Core's `PasswordHasher`: PBKDF2-HMAC-SHA512, random salt,
   210,000 iterations (OWASP guidance). Old hashes are upgraded automatically on the next sign-in.
2. **Same answer whether an email exists or not.** Registration always says "check your inbox", forgot-password
   always says "if an account exists…", sign-in failures always say "Email or password is incorrect.", and every
   sign-in runs exactly two hash checks so timing doesn't leak it either.
3. **Verification needs the link *and* the password.** Otherwise someone could register *your* email with *their*
   password first and have you activate it by clicking the link ("pre-registration hijack").
4. **Tokens live in the URL fragment** (`#token=…`), which browsers never send to servers or in `Referer`; the page
   removes it from the address bar. Only a SHA-256 of each token is stored.
5. **One account per email.** Google sign-in joins an existing password account with the same (Google-verified)
   email; a password nobody ever verified is thrown away at that moment.
6. **Guessing protection.** 5 failures per email or 20 per address in 15 minutes → refused (even with the right
   password) until 15 minutes after the last failure. Counters store only `HMAC(Auth:LookupHashKey, …)` — never
   plain emails or IPs.

## Session security

- **Verified email only:** sign-in is refused unless Google says the email is verified.
- **Idle timeout:** 60 minutes of inactivity (sliding).
- **Absolute cap:** 8 hours after sign-in, regardless of activity (`Auth:AbsoluteSessionLifetime`).
- **Server-side revocation:** every request checks the cookie against the database (its `user_sessions` row
  and the user's `session_version`), so a copied cookie stops working as soon as its session is ended. If the
  database can't be reached, the session is refused (fail closed).
- **The Dashboard countdown shows the server's real deadline.** The sliding cookie is only renewed by a request
  made in the second half of its 60-minute window, so the inactivity deadline is sometimes ~30 minutes after your
  last action, not 60. `/api/auth/me` mirrors that rule and reports seconds left; the page counts down from those
  (so a wrong computer clock doesn't matter) and makes **no** requests while ticking — leaving it open does not
  keep you signed in. `/api/*` responses are `Cache-Control: no-store`.
- **Per-device sessions.** Every sign-in creates a row in `user_sessions` and puts its id (`sid`) in the cookie;
  each request checks that row. **Log out ends only this device.** Changing your password or "Sign out all other
  devices" ends every other device (and bumps `session_version`, which also catches cookies from before this
  feature); a password reset by email ends every device. Only a masked IP and a short device description are stored.
- **Google no longer overwrites your name.** Google's name is used only when an account has none; the name you
  choose in settings is kept on later Google sign-ins.
- **Password sessions** are the same cookie as Google ones, so every rule above applies to them too. Password
  reset ends all sessions as well.
- **CSRF:** every `POST` (logout, register, sign-in, verify, forgot, reset) needs the `X-Requested-With: fetch`
  header, plus `SameSite=Lax`. This also blocks "login CSRF" (being signed in to someone else's account).
- **Headers:** `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  and a CSP on every API response; `auth-complete.html` has a strict CSP and no inline script.

## Not production-ready

This is tuned for learning on `localhost`. Deliberate dev-only shortcuts:

- The Vite proxy skips TLS verification of the dev certificate (`secure: false`).
- The frontend is served over plain HTTP; the `Secure` cookie only works because browsers treat
  `http://localhost` as a secure context (Chromium/Firefox; Safari may not).
- ASP.NET Core Data Protection keys (which encrypt the cookie) live in the default per-user folder.
- The developer exception page and automatic migrations run in `Development`.
- Emails go to the development mailbox (a database table + `/dev/mailbox` page) instead of being sent. The app
  refuses to start outside `Development`/`Testing` until a real email sender is registered.
- Throttling counts per client address as seen by the backend; behind the Vite proxy that is always loopback.

For production you would serve the frontend over HTTPS on the same site as the API (no proxy),
persist Data Protection keys, use a managed secret store, run migrations as a deploy step, and
consider a server-side session store if you need per-device logout.

## Secret hygiene

- Secrets live **only** in .NET User Secrets (`dotnet user-secrets set ...`), outside the repo.
- Never put secrets in `appsettings*.json`, `frontend/.env`, or any `VITE_*` variable —
  every `VITE_*` value is bundled into browser JavaScript.
- Never paste the client secret into chats, issues, or commits.
- Google client secrets start with `GOCSPX-`. If one is ever exposed, **rotate it** in
  Google Cloud Console → APIs & Services → Credentials.

## Quick commands

```powershell
# backend (https://localhost:5001)
dotnet run --project backend/OAuthLearn.Api --launch-profile https

# frontend (http://localhost:5174)
cd frontend; npm run dev

# tests
dotnet test OAuthLearn.sln      # DB tests need Docker or TEST_PG_CONNECTION
cd frontend; npm test
```
