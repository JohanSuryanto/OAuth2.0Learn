---

description: "Task list for Home Page and Signed-in Dashboard"
---

# Tasks: Home Page and Signed-in Dashboard

**Input**: Design documents from `/specs/003-home-dashboard/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-and-routes.md, quickstart.md

**Tests**: Included. plan.md (Testing) and research R10 commit to extending the existing xUnit and Vitest suites, as in features 001 and 002. Backend DB tests need `TEST_PG_CONNECTION`.

**Organization**: Grouped by user story: US1 Home, US2 Dashboard account details, US3 logout/expiry back to Home, US4 session countdown.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US4 from spec.md

## Path Conventions

- Backend: `backend/OAuthLearn.Api/`; tests `backend/OAuthLearn.Api.Tests/`
- Frontend: `frontend/src/`
- Routes, API shapes and all UI texts: `specs/003-home-dashboard/contracts/api-and-routes.md` (below: **the contract**)

---

## Phase 1: Setup

- [X] T001 In `frontend/`, run `npm install react-router@8`. Confirm `frontend/package.json` lists `react-router` under `dependencies` and that `npx tsc -b` still passes (research R1)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] In `backend/OAuthLearn.Api/Auth/SecurityHeaders.cs`, add `Cache-Control: no-store` to every response whose path starts with `/api/`, inside the same `OnStarting` callback. Extend `backend/OAuthLearn.Api.Tests/AuthEndpointsTests.cs` `Responses_carry_security_headers` to assert `no-store` on `/api/auth/me` (research R8)
- [X] T003 [P] Create `frontend/src/auth/LoginDialog.tsx` exporting `LoginDialogProvider` and `useLoginDialog()` (`{ open(): void; close(): void; isOpen: boolean }`). The provider:
  - renders `{isOpen && <LoginModal onClose={close} />}` once;
  - remembers `document.activeElement` when `open()` is called and restores focus to it on `close()`.
  
  Put the hook in `frontend/src/auth/loginDialogContext.ts` (context plus `useLoginDialog`) so the component file only exports components (Fast Refresh lint rule). Update `frontend/src/components/Header.tsx`:
  - remove its local `loginOpen` state and `<LoginModal>`;
  - its Login button calls `useLoginDialog().open()`.
  
  Keep `aria-haspopup="dialog"` (research R6)
- [X] T004 Create `frontend/src/routing/guards.tsx` with three components:
  - `LoadingScreen`: a `<section className="card" aria-busy="true">` with "Loading…";
  - `RequireSignedIn({ children })`: `loading` → `LoadingScreen`; `signedOut` → `<Navigate to="/" replace />`; `signedIn` → `children`;
  - `RedirectIfSignedIn({ children })`: `loading` → `LoadingScreen`; `signedIn` → `<Navigate to="/dashboard" replace />`; otherwise `children`.
  
  Both guards use `useAuth()` (research R1, FR-003, FR-006)
- [X] T005 Rewire the app shell:
  - `frontend/src/main.tsx`: wrap in `<BrowserRouter>` (from `react-router`), then `<AuthProvider>`, then `<LoginDialogProvider>`, then `<App />`.
  - `frontend/src/App.tsx`: keep the `.app` shell with `<Header />` and `<main className="dashboard">`, and render `<Routes>`:
    - `/` → `<RedirectIfSignedIn>` with a temporary placeholder until US1;
    - `/dashboard` → `<RequireSignedIn>` with a temporary placeholder until US2;
    - `/verify-email` → `<VerifyEmailPage/>`;
    - `/reset-password` → `<ResetPasswordPage/>`;
    - `*` → `<NotFoundPage/>`.
  - Create `frontend/src/pages/NotFoundPage.tsx` ("Page not found" heading and a `<Link to="/">Go to Home</Link>`, per the contract).
  
  Remove the old pathname switch. Depends on T003, T004 (FR-002, FR-007, FR-008, FR-009)
- [X] T006 Update the existing frontend tests for the router and dialog provider:
  - `frontend/src/components/Header.test.tsx`: render inside `<MemoryRouter>`, mock `../auth/loginDialogContext` (`open`), and assert that Login calls `open()` instead of rendering the dialog. Remove the dialog-focus test, which moves to T007.
  - `frontend/src/pages/pages.test.tsx`: wrap the renders in `<MemoryRouter>`.
  - Create `frontend/src/routing/guards.test.tsx` (mock `useAuth`; `MemoryRouter` with `initialEntries`) covering:
    - `loading` shows `aria-busy` and neither page;
    - signed out at `/dashboard` → the Home placeholder;
    - signed in at `/` → the Dashboard placeholder;
    - an unknown path → "Page not found".
- [X] T007 [P] Create `frontend/src/auth/LoginDialog.test.tsx` (mock `useAuth` and `passwordAuthApi` as in `LoginModal.test.tsx`). Check that:
  - `open()` renders the dialog;
  - × closes it;
  - focus returns to the opener button;
  - calling `open()` twice still renders a single dialog.

**Checkpoint**: The app builds, existing flows still work, `/nope` shows "Page not found", and the old feature 001/002 tests pass (after T006).

---

## Phase 3: User Story 1 - Signed-out visitors see a Home page (Priority: P1) 🎯 MVP

**Goal**: A public Home at `/` with a "Get started" button that opens the shared dialog. Signed-in users are redirected.

**Independent Test**: Quickstart 1, 2 and 9.

- [X] T008 [US1] Create `frontend/src/pages/HomePage.tsx` using the contract texts:
  - heading "Learn OAuth 2.0 by signing in";
  - body "This app shows how signing in works: with your Google account (OAuth 2.0) or with an email and password. Sign in to see your account and session details.";
  - a primary button "Get started" calling `useLoginDialog().open()`.
  
  Style it in `frontend/src/App.css` (`.home` card, reusing `.card` and `.primary-button` with an auto width). In `frontend/src/App.tsx`, route `/` → `<RedirectIfSignedIn><HomePage/></RedirectIfSignedIn>` (FR-001)
- [X] T009 [P] [US1] Create `frontend/src/pages/HomePage.test.tsx` (mock `useAuth` and `loginDialogContext`):
  - it renders the heading, body and button;
  - "Get started" calls `open()`;
  - it contains no account-detail labels ("Sign-in methods").
  
  Add to `frontend/src/routing/guards.test.tsx`: signed in at `/` with the real `HomePage` route → redirected to `/dashboard` (US1-3).

**Checkpoint**: Quickstart 1, 2 and 9 pass.

---

## Phase 4: User Story 2 - Dashboard with account details (Priority: P1)

**Goal**: Signed-in users land on `/dashboard`, which shows the account summary from `GET /api/account`.

**Independent Test**: Quickstart 3, 6, 7 and 8.

- [X] T010 [P] [US2] Create `backend/OAuthLearn.Api/Auth/AccountEndpoints.cs` with `MapAccountEndpoints()`:
  - `GET /api/account`, `.RequireAuthorization()`;
  - reads the `app_user_id` claim and loads the user with `PasswordCredential` (`AsNoTracking`);
  - if the claim or user is missing → `Results.Unauthorized()`;
  - returns `AccountSummaryResponse(string Email, string? DisplayName, string[] Methods, DateTimeOffset CreatedAt, DateTimeOffset LastSignInAt)`;
  - `Methods`: `"google"` when "`google_subject IS NOT NULL`", then `"password"` when "`password_credentials.active_hash IS NOT NULL`". A pending hash is **not** listed.
  
  Call `app.MapAccountEndpoints()` in `backend/OAuthLearn.Api/Program.cs` (research R2, R9, FR-011–FR-013)
- [X] T011 [P] [US2] Create `backend/OAuthLearn.Api.Tests/AccountEndpointTests.cs` (`[Collection(PostgresCollection.Name)]`, a new `TestAppFactory` per test, real cookies via `TestHelpers`):
  - no session → 401 with no `Location`;
  - password-only account (register + verify) → `methods == ["password"]`, email, displayName and times are set;
  - Google-only (create via `UserService.UpsertFromGoogleAsync`, then call with `X-Test-User` + `X-Test-User-Id`) → `["google"]`;
  - both (Google account + password registered and verified) → `["google","password"]`;
  - Google account with only a **pending** password → `["google"]`;
  - the response has `Cache-Control: no-store`.
- [X] T012 [P] [US2] Add `fetchAccount(): Promise<AccountSummary | null>` to `frontend/src/auth/authApi.ts` (`GET /api/account` with `credentials: 'include'`; 200 → JSON; 401 → null; otherwise throw) and export the `AccountSummary` type from data-model.md (`methods: ('google' | 'password')[]`, ISO strings for times)
- [X] T013 [US2] Create `frontend/src/pages/DashboardPage.tsx`:
  - On mount (and when `state.user.email` changes), call `fetchAccount()`. Show `LoadingScreen` while loading.
  - On error or `null`, show "Couldn't load your account details. Please try again." with a "Try again" button that refetches, and **no** partial data (FR-014).
  - On success render:
    - `<h1>Welcome, {displayName || email}</h1>`;
    - a `<dl>` with Email, Display name ("Not set" when null), Sign-in methods ("Google" / "Password" joined with " and "), Account created, Last sign-in. Times use `new Date(iso).toLocaleString()`.
  - Leave a `<section aria-label="Session">` placeholder for US4.
  - Never fetch on a timer.
  
  Route `/dashboard` → `<RequireSignedIn><DashboardPage/></RequireSignedIn>` in `frontend/src/App.tsx`. Styles (`.dashboard-grid`, `.details dt/dd`, long values wrap) go in `frontend/src/App.css`. Depends on T012 (FR-010, FR-011)
- [X] T014 [US2] In `frontend/src/pages/VerifyEmailPage.tsx`, on successful verification, `await refresh()` then `navigate('/dashboard', { replace: true })` with `useNavigate`, instead of showing the "You're signed in" message (FR-004, US2-2). Update the matching test in `frontend/src/pages/pages.test.tsx` to assert navigation to `/dashboard` (MemoryRouter + a `/dashboard` test route rendering "DASHBOARD")
- [X] T015 [P] [US2] Create `frontend/src/pages/DashboardPage.test.tsx` (mock `authApi.fetchAccount` and `useAuth` as signed in):
  - it renders the welcome with the display name, falling back to the email when null ("Not set" shown);
  - methods `['google','password']` → "Google and Password";
  - the error state shows the message and "Try again" refetches and then renders;
  - `fetchAccount` is called once on mount and not again after `vi.advanceTimersByTime(120_000)` (fake timers).
  
  Add to `frontend/src/routing/guards.test.tsx`: signed out at `/dashboard` → the Home heading, and the "Sign-in methods" label is never in the document (SC-001).

**Checkpoint**: Quickstart 3, 6, 7 and 8 pass.

---

## Phase 5: User Story 3 - Logout and expiry return the user to Home (Priority: P1)

**Goal**: Logout goes to Home; a session that ends while signed in goes to Home with "Your session has ended. Please sign in again."

**Independent Test**: Quickstart 4, 5, 13 and 15.

- [X] T016 [US3] In `frontend/src/components/Header.tsx`, after `await logout()` resolves successfully, call `navigate('/')` (`useNavigate`). If logout failed (state still signed in), stay on the page (FR-005)
- [X] T017 [US3] Implement `sessionEnded` (research R5):
  - `frontend/src/auth/useAuth.ts`: the `signedOut` variant gains `sessionEnded?: boolean`.
  - `frontend/src/auth/AuthContext.tsx` `refresh()`: when `fetchMe()` resolves `null` **and** the current state is `signedIn`, set `{ status: 'signedOut', sessionEnded: true }`. A network error keeps today's behaviour (plain `signedOut`, no flag). `logout()` keeps setting a plain `signedOut`.
  - Extend `clearError()` so it also clears `sessionEnded`.
  - Add a `window` `pageshow` listener: if `event.persisted`, call `refresh()` (research R8).
- [X] T018 [US3] In `frontend/src/pages/HomePage.tsx`, when `state.status === 'signedOut' && state.sessionEnded`, show `<p className="notice" role="status">Your session has ended. Please sign in again.</p>` above the heading. Opening the dialog ("Get started" or Login) calls `clearError()` through the existing `LoginModal` mount effect, which hides it (FR-019)
- [X] T019 [P] [US3] Tests:
  - `frontend/src/auth/AuthContext.test.tsx`: signed in, then `refresh()` gets `null` → `sessionEnded: true`; logout → no flag; network error while signed in → no flag; `clearError()` clears it; a `pageshow` event with `persisted: true` triggers `fetchMe`.
  - `frontend/src/components/Header.test.tsx`: after clicking Logout, the location is `/` (MemoryRouter at `/dashboard` with a `/` route rendering "HOME").
  - `frontend/src/pages/HomePage.test.tsx`: the notice appears only with `sessionEnded`.

**Checkpoint**: Quickstart 4, 5, 13 and 15 pass.

---

## Phase 6: User Story 4 - See when my session will end (Priority: P2)

**Goal**: Two live countdowns (inactivity and 8 h), based on the server's timing, that never keep the session alive.

**Independent Test**: Quickstart 10, 11, 12 and 16.

- [X] T020 [P] [US4] Create `backend/OAuthLearn.Api/Auth/SessionTiming.cs`:
  - `public record SessionTimingDto(int IdleSecondsLeft, DateTimeOffset IdleExpiresAt, int AbsoluteSecondsLeft, DateTimeOffset AbsoluteExpiresAt)`;
  - `static SessionTimingDto? Compute(AuthenticationProperties? props, ClaimsPrincipal user, DateTimeOffset now, CookieAuthenticationOptions cookie, TimeSpan absoluteLifetime)`:
    - return null when `props?.ExpiresUtc` or `IssuedUtc` is missing, or the `auth_time` claim is missing;
    - `idleExpiresAt = ExpiresUtc`, but if `cookie.SlidingExpiration && (now − IssuedUtc) > (ExpiresUtc − now)`, then `idleExpiresAt = now + cookie.ExpireTimeSpan`;
    - `absoluteExpiresAt = FromUnixTimeSeconds(auth_time) + absoluteLifetime`;
    - seconds left are clamped at ≥ 0 and rounded down.
  
  Read `absoluteLifetime` the same way `SessionValidator` does (`Auth:AbsoluteSessionLifetime`, default 8 h); extract a shared `SessionValidator.AbsoluteLifetime(IConfiguration)` helper to avoid duplication (research R3)
- [X] T021 [US4] In `backend/OAuthLearn.Api/Auth/AuthEndpoints.cs`, change `MeResponse` to `MeResponse(string Email, string? Name, SessionTimingDto? Session)`. In `/me`:
  - get `var auth = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)`;
  - resolve `IOptionsMonitor<CookieAuthenticationOptions>` for the cookie scheme and `TimeProvider`;
  - fill `Session = SessionTiming.Compute(auth.Properties, ctx.User, now, options, lifetime)`.
  
  Update `Me_with_user_returns_email_and_name` in `backend/OAuthLearn.Api.Tests/AuthEndpointsTests.cs` to expect `Session == null` for the Test scheme. Depends on T020
- [X] T022 [P] [US4] Create `backend/OAuthLearn.Api.Tests/SessionTimingTests.cs`.
  - HTTP tests, using a password account signed in with a real cookie and `factory.Time`:
    - right after sign-in → idle about 3600 s and absolute about 28800 s (±2 s);
    - `Time.Advance(10 min)` → idle about 3000 s, no renewal yet;
    - `Time.Advance(40 min)` in total → idle about 3600 s, because this request renews the cookie. The next `/me` shows about 3600 s again, not about 3000 s, which proves the renewal happened;
    - `Time.Advance(7 h 50 min)` in total, with requests in between keeping it alive → absolute about 600 s.
  - Unit tests of `SessionTiming.Compute` with hand-made `AuthenticationProperties`: missing values → null; non-sliding → never renews; clamped at 0.
- [X] T023 [P] [US4] Frontend timing plumbing:
  - `frontend/src/auth/authApi.ts`: `fetchMe()` returns `{ user: AuthUser; session: SessionTimingPayload | null } | null`, parsing the `session` block from the contract.
  - `frontend/src/auth/useAuth.ts`: the `signedIn` state gains `session: SessionTiming | null` (payload + `receivedAt: number`).
  - `frontend/src/auth/AuthContext.tsx` `refresh()`: store `session` with `receivedAt: performance.now()`.
  
  Update the existing `AuthContext.test.tsx`/`Header` mocks for the new `fetchMe` shape (data-model.md)
- [X] T024 [US4] Create `frontend/src/components/SessionCountdown.tsx` (props `session: SessionTiming`).
  - A 1 s `setInterval` updates only local state; it **never** calls the server (FR-017).
  - `left(x) = max(0, x.secondsLeft − floor((performance.now() − receivedAt) / 1000))`.
  - Renders two rows, "Inactivity limit" and "Absolute limit", each with `H:MM:SS left`, "ends at {new Date(expiresAt).toLocaleTimeString()}", and an "Ends first" badge on the smaller one.
  - Adds the `ending-soon` class when under 300 s (FR-018).
  - When the smaller value reaches 0, schedules one `setTimeout(refresh, 2000)` from `useAuth()`, never repeating for the same `receivedAt` (research R4).
  
  Render it in `DashboardPage`'s Session section when `state.session` is not null. Styles go in `frontend/src/App.css`. Depends on T023 (FR-015–FR-018)
- [X] T025 [P] [US4] Create `frontend/src/components/SessionCountdown.test.tsx` with fake timers, mocking `performance.now` through `vi.spyOn(performance, 'now')` driven by the fake clock:
  - it starts at `1:00:00` and `8:00:00` and ticks to `0:59:59` after 1 s;
  - "Ends first" is on the inactivity row;
  - `ending-soon` appears under 5 min;
  - at 0 plus 2 s, `refresh` is called exactly once;
  - `fetch` is never called while ticking (spy on `globalThis.fetch`);
  - a new `session` prop (a new `receivedAt`) resets the display.

**Checkpoint**: Quickstart 10, 11, 12 and 16 pass.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T026 [P] Update `README.md`:
  - a "Pages" section (Home, Dashboard, verify, reset, not found; who sees what);
  - a note in "Session security" on how the Dashboard countdown mirrors the sliding-cookie renewal (research R3) and never keeps the session alive.
- [X] T027 Run `dotnet build` and `dotnet test OAuthLearn.sln` (with `TEST_PG_CONNECTION`), and `npm test`, `npx tsc -b`, `npm run lint`, `npm run build` in `frontend/`. Fix any failures or warnings
- [ ] T028 Run the 18 manual scenarios in `specs/003-home-dashboard/quickstart.md` against the running app and note the results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → user stories
- **US1 (Phase 3)**: after Foundational
- **US2 (Phase 4)**: after Foundational; independent of US1, but the redirect tests use the US1 Home route
- **US3 (Phase 5)**: after US1 (T018 edits HomePage) and US2 (the Dashboard exists to be left)
- **US4 (Phase 6)**: after US2 (it renders in DashboardPage); its backend tasks T020–T022 can start any time after Foundational
- **Polish (Phase 7)**: after the stories

### Key Task Dependencies

- T001 → T004, T005; T003 + T004 → T005 → T006
- T012 → T013; T013 → T014 (the route exists), T015
- T017 → T018, T019
- T020 → T021 → T022; T023 → T024 → T025

### Parallel Opportunities

- Foundational: T002 (backend) alongside T003/T004 (frontend); T007 alongside T006
- US2: the backend (T010, T011) and frontend (T012) in parallel
- US4: the backend (T020–T022) can run in parallel with US1–US3 frontend work

---

## Parallel Example: User Story 2

```text
Task: "T010 [US2] GET /api/account in backend/OAuthLearn.Api/Auth/AccountEndpoints.cs"
Task: "T011 [US2] AccountEndpointTests.cs"
Task: "T012 [US2] fetchAccount in frontend/src/auth/authApi.ts"
```

## Parallel Example: User Story 4

```text
Task: "T020 [US4] SessionTiming.Compute in backend/OAuthLearn.Api/Auth/SessionTiming.cs"
Task: "T023 [US4] fetchMe/AuthState session plumbing in frontend/src/auth/"
```

---

## Implementation Strategy

### MVP First

1. Phases 1 and 2 (router, guards, shared dialog, no-store). Check that all existing tests still pass.
2. US1 (Home), then US2 (Dashboard with account details). **Validate with quickstart 1–9.**
3. US3 (logout and expiry → Home).
4. US4 (countdown), then Polish.

### Notes

- Never render account data before the sign-in status is known, and use `replace` for redirects.
- The Dashboard must make no requests on a timer. The countdown is display-only until it reaches zero.
