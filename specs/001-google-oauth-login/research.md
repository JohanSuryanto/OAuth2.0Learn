# Research: Google OAuth Login Dashboard

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-23

Local environment found: .NET SDK 9.0.101 and Node.js v22.19.0. No `psql` client on PATH, so PostgreSQL is assumed to run as a Windows service or in Docker.

---

## R1. OAuth flow shape: server-side Authorization Code flow

- **Decision**: The backend runs the whole OAuth 2.0 Authorization Code flow with ASP.NET Core's built-in Google handler (`Microsoft.AspNetCore.Authentication.Google`). Google redirects to `https://localhost:5001/signin-google` (the handler's default `CallbackPath`). The handler exchanges the code for tokens server-side with the client secret, then issues the app's own **cookie session**.
- **Rationale**: This matches the redirect URI the user already registered. The client secret never leaves the server (FR-005). It's also the "classic" confidential-client flow, which is the best one to learn first. The handler takes care of `state`, PKCE (enabled by default for Google in .NET 7+), and correlation cookies.
- **Alternatives considered**:
  - *Google Identity Services (GIS) button / One Tap in React, then send the ID token to the backend*: skips the redirect URI and hides the OAuth mechanics. Not what the user wants to learn.
  - *SPA-only PKCE flow (public client)*: would need a redirect URI on the FE origin, and gives no server session.
  - *Hand-rolled flow (manual `/authorize` URL + token POST)*: good for learning but error-prone. Could be a later exercise.

## R2. Popup instead of full-page redirect

- **Decision**: FE calls `window.open("https://localhost:5001/api/auth/login", "google-login", "width=500,height=650")`. The backend challenges Google inside the popup. After the callback, the backend's `/api/auth/popup-complete` **redirects the popup back to the dashboard's origin**, at `http://localhost:5174/auth-complete.html?result=...`. That static page:
  - posts `{ type: "oauth-result", success, error }` on `BroadcastChannel("oauth-login")`;
  - also calls `window.opener?.postMessage(msg, location.origin)` as a secondary path;
  - then calls `window.close()`.
- **FE handling**:
  - Listens on the channel, and on `message` events whose origin is its own.
  - On success, refetches `/api/auth/me`.
  - `popup.closed` is only a *hint*: the first time it reads true, the FE refetches `/me` but keeps listening, and gives up silently after 5 minutes.
  - If `window.open` returns `null`, the popup was blocked, and the FE shows a "please allow popups" message.
  - A second click while the popup is open focuses the existing window.
- **Rationale**: Google forbids its sign-in page in iframes (`X-Frame-Options`), so a real popup window is the only way to get "popup/modal" behavior. Google's sign-in pages may also send `Cross-Origin-Opener-Policy`. Navigating the popup through them can then cut the link to the dashboard, which has three effects:
  - `window.opener` becomes `null` in the popup;
  - `popup.closed` can read `true` early in the opener;
  - `window.close()` may be ignored.
  
  Returning to the dashboard's own origin and using `BroadcastChannel` (which is same-origin and doesn't need the opener) avoids all of this.
- **Alternatives considered**: sending `postMessage` straight from the backend page to the opener (breaks when COOP cuts the link, see above); polling `/me` while the popup is open (works, but is slower and noisier).
- **Alternatives considered**: a full-page redirect (simpler, but the user asked for a popup); an iframe modal (blocked by Google).

## R3. How the SPA talks to the API (cookies across http:5174 ↔ https:5001)

- **Decision**: The Vite dev server **proxies `/api` → `https://localhost:5001`** (`secure: false`, `changeOrigin: false`). The React app calls only relative URLs (`/api/auth/me`, `/api/auth/logout`), so these are same-origin requests from the browser's point of view. Only the popup navigates directly to `https://localhost:5001`.
- **Cookie settings**: `HttpOnly`, `Secure`, `SameSite=Lax`, host `localhost` (no Domain attribute).
- **Rationale**:
  - Cookies are scoped by host, not port, so a cookie set by `https://localhost:5001` during the popup flow is also sent to `http://localhost:5174`. Chromium and Firefox treat `http://localhost` as a secure context, so the `Secure` cookie is still sent. Vite forwards it to the backend.
  - Without the proxy, the calls would be `http` → `https`, which is cross-*site* under schemeful same-site rules. That would force `SameSite=None` and depend on third-party-cookie policy, which is fragile.
  - `SameSite=Lax` also blocks cross-site POSTs to `/logout` (CSRF defence).
- **Also**: CORS on the backend allows only `http://localhost:5174` with credentials (FR-014). This is a guard for anyone calling the API directly without the proxy.
- **Alternatives considered**:
  - Direct CORS + `SameSite=None`: works in Chrome today but breaks under third-party-cookie blocking (Safari/Firefox strict).
  - JWT in `localStorage`: exposes the token to XSS and doesn't teach cookie sessions.
  - Serving the FE over HTTPS: the user fixed the FE at `http://localhost:5174`.

## R4. Session lifetime

- **Decision**: Cookie auth with `ExpireTimeSpan = 60 min` and `SlidingExpiration = true`, plus an **absolute 8 h cap** and **server-side revocation** (both in R5c). The cookie is a non-persistent session cookie (`IsPersistent = false`), so closing the browser ends it. API endpoints return `401`/`403` instead of redirecting (`OnRedirectToLogin` / `OnRedirectToAccessDenied` overridden).
- **Rationale**: Matches the spec assumption (about 1 hour of inactivity, or until the browser closes). No server-side session store is needed (spec: sessions aren't stored as DB rows).
- **Alternatives considered**: a DB-backed ticket store (`ITicketStore`), which is excluded by the spec; persistent 14-day cookies, which are longer than the spec intends.

## R5. Where the user upsert happens

- **Decision**: In `GoogleOptions.Events.OnCreatingTicket`, resolve `UserService` from `context.HttpContext.RequestServices`, then upsert by Google `sub` (the `ClaimTypes.NameIdentifier` claim). The upsert sets email and name, sets `LastLoginAt = now`, and sets `CreatedAt` on insert. Then add an `app_user_id` claim to the identity.
- **Error handling**:
  - Exceptions there are caught by `RemoteAuthenticationHandler` and routed to `OnRemoteFailure`. That handler redirects the popup to `/api/auth/popup-complete?error=signin_failed` and calls `HandleResponse()`, which covers FR-017.
  - When the user declines consent, Google returns `error=access_denied`. ASP.NET Core sends that to the separate **`OnAccessDenied`** event, *not* `OnRemoteFailure`, whose default failure message doesn't contain "access_denied" anyway. `OnAccessDenied` redirects to `?error=access_denied` and calls `HandleResponse()`.

## R5b. Default challenge scheme

- **Decision**: `DefaultScheme = DefaultChallengeScheme = Cookie`. `/api/auth/login` challenges `GoogleDefaults.AuthenticationScheme` explicitly.
- **Rationale**: If Google were the default challenge scheme, an unauthenticated `GET /api/auth/me` would answer with a 302 to accounts.google.com instead of a 401. `fetch` would then follow that redirect and fail on CORS, which would break the "check session on load" flow (FR-010).
- **Rationale**: This is the single place where the Google identity is known and the session hasn't been issued yet. If the DB write fails, no cookie is issued.
- **Missing email**: if the `email` claim is absent, throw from `OnCreatingTicket`, which gives the same failure path.

## R5c. Security hardening (from the security review)

- **S2 Verified email**: Google's userinfo JSON (`context.User` in `OnCreatingTicket`) carries `email_verified` (v3 endpoint) or `verified_email` (v2 endpoint), depending on the handler version. Accept only when one of them is present and `true`. If the property is missing or `false`, throw, which leads to `signin_failed`. Failing closed means an endpoint change can't silently skip the check.
- **S1 Server-side revocation**:
  - `users.session_version int NOT NULL DEFAULT 0`. On sign-in, the cookie gets the claim `session_version`.
  - Cookie `Events.OnValidatePrincipal` runs on every authenticated request. It loads the user by `app_user_id` and compares versions. On a mismatch, a missing user, or a DB error it calls `context.RejectPrincipal()` and `SignOutAsync(Cookie)`. This fails closed.
  - Logout increments `session_version` for that user, which revokes every copy of every session ("log out everywhere").
  - Cost: one indexed PK lookup per request, which is fine here.
  - *Alternative*: a server-side `ITicketStore` (a sessions table). It allows per-device logout, but adds a table and cleanup. Rejected to keep it simple.
- **S4 Absolute lifetime**: on sign-in, add the claim `auth_time` (Unix seconds, UTC). In `OnValidatePrincipal`, reject if `now - auth_time > 8 h`. `IssuedUtc` can't be used for this because sliding expiration resets it.
- **S5 Headers**:
  - Backend middleware sets `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` on every response. The API only returns JSON, 204s and redirects, so nothing needs more than that.
  - `auth-complete.html` loads its logic from `/auth-complete.js` (no inline script) and declares `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'self'">`.
  - The Vite dev server adds `X-Frame-Options: DENY` and `X-Content-Type-Options: nosniff`. It gets no CSP, because Vite's HMR needs inline scripts in dev.
- **S3 Local Postgres**: bind Docker to `127.0.0.1` only, give the `postgres` superuser a strong password, and have the app use a dedicated `oauthlearn2` role that owns only the `oauthlearn2` database (enough for migrations, no superuser; `CREATEDB` only so tests can create throwaway databases).
- **S6 Dev-only shortcuts**: the Vite proxy doesn't verify the backend's certificate (`secure: false`), `Secure` cookies travel over `http://localhost`, the developer exception page is on in Development, and Data Protection keys sit in the default per-user folder. All of this is acceptable locally and documented in the README as "not production-ready".
- **S7 Secret hygiene**: Google client secrets start with `GOCSPX-`. The security check (T053) greps the repo and `frontend/dist` for `GOCSPX-`. `.gitignore` excludes `appsettings.*.local.json` and `.env*.local`. The README warns never to put secrets in `VITE_*` variables (they are bundled into the browser JS) and to rotate the secret if it is ever exposed.

## R6. Data access: EF Core + Npgsql

- **Decision**: `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x with `EFCore.NamingConventions` (snake_case). Schema is managed with EF Core migrations (`dotnet ef`). Migrations are applied at startup in Development only.
- **Rationale**: This is the standard .NET + Postgres stack. Migrations keep the schema in source control.
- **Alternatives considered**: Dapper with hand-written SQL (more code for no gain here); `EnsureCreated()` (can't evolve the schema).

## R7. Secrets & configuration

- **Decision**: .NET **User Secrets** hold `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, and `ConnectionStrings:Default`. `appsettings.json` holds only non-secret values (`Frontend:Origin = http://localhost:5174`). The FE needs no secrets and no client ID.
- **Rationale**: FR-005 and FR-013. User Secrets live outside the repo (`%APPDATA%\Microsoft\UserSecrets`).

## R8. HTTPS on 5001

- **Decision**: In `launchSettings.json`, the `https` profile uses `applicationUrl: https://localhost:5001`. The dev certificate is trusted with `dotnet dev-certs https --trust`.
- **Rationale**: This is the URL the user registered with Google, and the Google handler requires HTTPS for its correlation cookies.

## R9. Testing

- **Decision**:
  - **Backend**: xUnit with `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`). A test auth scheme simulates a signed-in user. It is plugged in as the default *authenticate* scheme only, so the challenge stays Cookie and 401s are still tested for real. The Postgres test DB comes from `TEST_PG_CONNECTION` if that is set, otherwise from **Testcontainers**. Covers `/me` (200/401), `/logout`, the popup-complete HTML (postMessage origin), and `UserService` upsert (insert, update, and one row per `sub`, which covers SC-007).
  - **Frontend**: Vitest with React Testing Library. Covers the header states (Login vs "Hi {email}" + Logout) and the popup helper (blocked popup, message origin filtering).
  - **End-to-end**: a manual check with a real Google account, following [quickstart.md](./quickstart.md). Automating Google's login screen is impractical and against Google's terms.
- **Alternatives considered**: Playwright E2E with a mocked OAuth provider. Useful later, but overkill for a learning project.

## R10. Frontend stack details

- **Decision**: Vite 6 + React 19 + TypeScript, with `vite.config.ts` setting `server.port = 5174` and `strictPort: true`. Plain CSS, no UI library. Auth state lives in a small `AuthContext` (`status: "loading" | "signedOut" | "signedIn"`, `user`, `login()`, `logout()`, `error`).
- **Rationale**: Minimal dependencies keep the focus on OAuth.
