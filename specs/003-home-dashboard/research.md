# Research: Home Page and Signed-in Dashboard

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-23

Builds on features 001 and 002. Frontend: React 19.2.8 + Vite 8, `AuthContext` (status plus `refresh()` on load and on focus), `Header` with `LoginModal`, and a pathname switch in `App.tsx` for `/verify-email` and `/reset-password`. Backend: cookie session (60 min sliding, `SessionValidator` enforcing session version and the 8 h cap from the `auth_time` claim), and `GET /api/auth/me` returning `{ email, name }`.

---

## R1. Routing: add `react-router`

- **Decision**: Add `react-router` 8.x (it needs React ≥ 19.2.7; 19.2.8 is installed) in declarative mode: `<BrowserRouter>`, `<Routes>`, `<Route>`, `<Navigate>`, `useNavigate`, plus `<MemoryRouter>` in tests.
  - **Routes**: `/` Home, `/dashboard` Dashboard (protected), `/verify-email`, `/reset-password`, and `*` NotFound.
  - Two small guard components, `RequireSignedIn` and `RedirectIfSignedIn`. While `status === 'loading'` they render a neutral loading placeholder (FR-006); otherwise they render the page or a `<Navigate replace>`.
- **Rationale**: There are now five destinations with auth-dependent redirects, client-side navigation (FR-009) and back/forward behaviour. Research R11 of feature 002 deferred a router until "more than two pages", and that point has come.
- **Alternatives considered**:
  - Extending the hand-written pathname switch: it would reimplement history, redirects and links.
  - TanStack Router: a heavier API with more concepts to learn.
- **`<Navigate replace>`** keeps redirects out of browser history, so Back after a redirect doesn't bounce the user around.

## R2. Where the Dashboard's data comes from

- **Decision**: Two sources.
  1. **Session timing** is added to `GET /api/auth/me`, which the app already calls on load, on focus and after sign-in. It's computed from the cookie ticket and claims with no DB query. That keeps the countdown correct after *every* request the app makes (see R3).
  2. **Account summary** comes from a new `GET /api/account`: email, display name, sign-in methods, created and last sign-in. It needs the database (credentials, Google identity). The Dashboard calls it once when it mounts and on "Try again".
- **Rationale**:
  - `/me` stays cheap: `SessionValidator` already reads the user's session version per request, but this doesn't add joins to every focus check.
  - Timing lives next to the sign-in status that already drives redirects.
  - The two concerns change for different reasons, so they're separate endpoints.
- **Alternatives considered**:
  - Everything in `/me`: extra DB work on every focus and load for data only the Dashboard needs.
  - Everything in `/api/account`: the countdown would go stale whenever a `/me` focus refresh renews the cookie (R3).

## R3. Computing "when will the server end this session"

- **How the sliding cookie really behaves** (ASP.NET Core `CookieAuthenticationHandler`):
  - A ticket has `IssuedUtc` and `ExpiresUtc = IssuedUtc + 60 min`.
  - On an authenticated request, the handler compares the time elapsed since `IssuedUtc` with the time left before `ExpiresUtc`. If more time has elapsed than remains (the second half of the window), it reissues the cookie with `IssuedUtc = now` and `ExpiresUtc = now + 60 min`. Otherwise it keeps the old expiry.
  - So the real idle deadline is **not** always "last request + 60 min". Right after a renewal it's a full 60 minutes; later it can be as little as about 30 minutes after the last request.
  - The spec's Assumptions already say to show the server's view, not a naive 60 minutes.
- **Decision**: `SessionTiming.Compute(properties, principal, now, cookieOptions, absoluteLifetime)` mirrors that rule:
  - `idleExpiresAt = ExpiresUtc`, but if `SlidingExpiration && (now − IssuedUtc) > (ExpiresUtc − now)`, then `idleExpiresAt = now + ExpireTimeSpan`, because this very request renews the cookie.
  - `absoluteExpiresAt = auth_time + Auth:AbsoluteSessionLifetime` (same config as `SessionValidator`).
  - The response carries seconds remaining (`idleSecondsLeft`, `absoluteSecondsLeft`) plus the ISO end times.
  - The frontend counts down from the **seconds** using `performance.now()` elapsed since the response. It never subtracts from `Date.now()`, so a wrong computer clock doesn't matter (FR-016).
  - End times are shown in local time via `new Date(expiresAt)`. They're only labels; the countdown drives the logic.
- **Test hosts**: `/me` via the `Test` auth scheme has no cookie ticket, so `session` is `null`, and the UI then hides the countdown section.

## R4. The countdown must never keep the session alive (FR-017)

- **Decision**:
  - The countdown ticks with a 1 s interval that only updates React state and **never calls the server**.
  - The Dashboard fetches `/api/account` once on mount and on "Try again", never on a timer.
  - At zero (whichever limit comes first) the page waits 2 s, then calls `refresh()` (`/me`) once.
    - A 401 means the session is gone: signed out, sent to Home with the message.
    - If the server still accepts the session (clock drift within the 1-minute tolerance), the new timing re-syncs the countdown. Because the server said the session ended at about that moment, the chance of this renewing it is negligible.
  - The existing focus-triggered `refresh()` (throttled to 30 s) stays. Focusing the window is a user interaction, and its response updates the timing, so the countdown stays accurate after the resulting cookie renewal.
- **Rationale**: SC-004 says leaving the Dashboard open longer than the inactivity limit must end the session. No background requests means nothing silently extends it.

## R5. "Your session has ended" vs a normal logout (FR-019)

- **Decision**:
  - `AuthState` `signedOut` gains `sessionEnded?: boolean`.
  - In `AuthProvider.refresh()`: if the previous state was `signedIn` and `/me` now returns 401, set `{ status: 'signedOut', sessionEnded: true }`.
  - `logout()` sets `signedOut` **without** the flag.
  - Home shows the notice "Your session has ended. Please sign in again." when the flag is set.
  - The flag clears when the sign-in dialog opens (the existing `clearError()` is extended) or on the next sign-in.
- **Why not also on network errors**: `fetchMe` throwing (backend down) already means signed out in feature 001, but it isn't proof the session ended. So `sessionEnded` is set only on an actual 401.

## R6. Opening the sign-in dialog from Home

- **Decision**: Move the dialog's open/close state out of `Header` into a tiny `LoginDialogProvider` with `useLoginDialog()`, exposing `{ open(), close(), isOpen }`. It renders `<LoginModal>` once. `Header`'s Login button and Home's "Get started" both call `open()`. Focus returns to the element that opened it.
- **Rationale**: One dialog instance and one source of truth, with no duplicate modals (FR-001: "opens exactly as it does from the header").

## R7. Redirects after sign-in and logout

- **Decision**:
  - **After sign-in**: `RedirectIfSignedIn` on `/` turns a signed-in state into `<Navigate to="/dashboard" replace>`, whichever method signed the user in (the dialog closes itself on `signedIn`, as in 002). `VerifyEmailPage` calls `navigate('/dashboard', { replace: true })` after `refresh()` succeeds, replacing the "You're signed in" message (FR-004).
  - **After logout**: `Header` calls `navigate('/')` after `logout()` resolves (FR-005). On pages other than the Dashboard this also takes the user Home, as the spec requires.
  - **Deep link while signed out**: `/dashboard` is sent to `/`, and after sign-in the Home redirect goes to `/dashboard`, so bookmarks work without a stored return address (spec Assumptions).

## R8. Back button, bfcache and caching

- **Decision**:
  - **Inside the SPA**, Back to `/dashboard` after logout re-runs `RequireSignedIn`, which redirects before any account data renders.
  - **Across sites**, the browser's back-forward cache can restore a frozen page. `AuthProvider` listens for `pageshow` with `event.persisted === true` and calls `refresh()`; until it resolves, the guard shows the loading state.
  - The backend sends `Cache-Control: no-store` on every `/api/*` response, via the existing security-headers middleware, so account data is never stored by the browser or intermediaries.
- **Rationale**: SC-001, SC-003, FR-013.

## R9. Sign-in methods

- **Decision**: `methods` is an ordered array: `"google"` if `users.google_subject IS NOT NULL`, and `"password"` if `password_credentials.active_hash IS NOT NULL`. A pending hash doesn't count (FR-012). The UI maps them to "Google" and "Password".

## R10. Testing

- **Backend**: xUnit, extending the existing `AccountFlowsTests` style. It uses real cookies via password sign-in and `factory.Time` (`FakeTimeProvider`) to check:
  - timing right after sign-in (60 min / 8 h);
  - timing after advancing 10 min, where there's no renewal yet, so idle equals the original expiry;
  - timing after advancing 40 min, where the request renews, so idle is 60 min from now;
  - the absolute cap bounds;
  - `methods` for Google-only, password-only, both, and pending-only;
  - `401` without a session;
  - `Cache-Control: no-store` on `/api/*`.
- **Frontend**: Vitest, React Testing Library and `MemoryRouter`:
  - guards (loading, redirect both ways);
  - Home "Get started" opens the dialog;
  - Dashboard rendering, method labels and the load error with retry;
  - the countdown with fake timers (ticks, "ending soon" under 5 min, refresh at zero, no extra fetches while ticking);
  - the session-ended notice vs a normal logout;
  - logout navigating Home;
  - NotFound.
