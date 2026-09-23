# Implementation Plan: Home Page and Signed-in Dashboard

**Branch**: `003-home-dashboard` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-home-dashboard/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Introduce client-side routing (`react-router`) with a public **Home** (`/`) and a protected **Dashboard** (`/dashboard`), plus guards that redirect by sign-in status and show a loading state while it's unknown.

**Backend** (no database changes):
- `GET /api/auth/me` gains a `session` block giving the server's own view of when the session ends. It mirrors the sliding-cookie renewal rule and the 8 h `auth_time` cap.
- A new `GET /api/account` returns the account summary, including the sign-in methods derived from `google_subject` and the active password hash.
- `/api/*` responses become `Cache-Control: no-store`.

**Frontend**:
- The Dashboard counts down locally from the server's "seconds left" (immune to a wrong computer clock) and makes no requests while ticking, so it never keeps the session alive.
- A 401 on a signed-in refresh flags `sessionEnded`, so Home can say "Your session has ended".
- The sign-in dialog moves into a small provider so Home's "Get started" and the header share it.

## Technical Context

**Language/Version**: C# 13 / .NET 9; TypeScript 6 / React 19.2.8 / Vite 8 (unchanged)

**Primary Dependencies**: Existing, plus **`react-router` 8.x** (frontend, declarative mode; needs React ≥ 19.2.7). No new backend packages.

**Storage**: Existing PostgreSQL; **no schema changes** ([data-model.md](./data-model.md))

**Testing**: xUnit + WebApplicationFactory + real Postgres + `FakeTimeProvider` (existing); Vitest + React Testing Library + `MemoryRouter`

**Target Platform**: Local development; Chromium/Firefox

**Project Type**: Web application (existing `backend/` + `frontend/`)

**Performance Goals**: Dashboard visible within 2 s of signing in (SC-002); logout reaches Home within 1 s (SC-003); countdown accurate to within 1 min of the server (SC-004)

**Constraints**:
- No background requests from the Dashboard (FR-017).
- No account data rendered before the sign-in status is known (FR-006, SC-001).
- The countdown uses server-reported seconds, not the client clock (FR-016).

**Scale/Scope**: 2 backend endpoint changes; about 6 new or changed frontend components; no migrations

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so there are no gates. **PASS (vacuous)** before and after design.

The design still keeps to simplicity:
- one new dependency, justified in research R1;
- no new tables;
- no polling.

## Project Structure

### Documentation (this feature)

```text
specs/003-home-dashboard/
├── spec.md
├── plan.md               # this file
├── research.md           # R1–R10
├── data-model.md         # read models only
├── quickstart.md         # 18 manual scenarios
├── contracts/
│   └── api-and-routes.md
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
backend/OAuthLearn.Api/
├── Auth/
│   ├── SessionTiming.cs          # NEW: Compute(properties, principal, now, cookieOptions, absoluteLifetime) (R3)
│   ├── AuthEndpoints.cs          # /me adds `session`; MeResponse gains SessionTimingDto?
│   ├── AccountEndpoints.cs       # NEW: GET /api/account (R2, R9)
│   └── SecurityHeaders.cs        # + Cache-Control: no-store for /api/* (R8)
└── Program.cs                    # MapAccountEndpoints()

backend/OAuthLearn.Api.Tests/
├── SessionTimingTests.cs         # NEW: /me timing with FakeTimeProvider (fresh, before/after renewal, cap)
├── AccountEndpointTests.cs       # NEW: methods matrix, 401, no-store
└── AuthEndpointsTests.cs         # MeResponse assertion updated for the new field

frontend/
├── package.json                  # + react-router
└── src/
    ├── main.tsx                  # <BrowserRouter> + providers
    ├── App.tsx                   # <Routes>: /, /dashboard, /verify-email, /reset-password, *
    ├── routing/
    │   └── guards.tsx            # NEW: RequireSignedIn, RedirectIfSignedIn, LoadingScreen
    ├── auth/
    │   ├── authApi.ts            # fetchMe → { user, session }; fetchAccount()
    │   ├── useAuth.ts            # AuthState + sessionEnded / session types
    │   ├── AuthContext.tsx       # sessionEnded on 401-after-signedIn; pageshow refresh; clearError clears flag
    │   └── LoginDialog.tsx       # NEW: LoginDialogProvider + useLoginDialog (R6)
    ├── components/
    │   ├── Header.tsx            # uses useLoginDialog; navigate('/') after logout
    │   └── SessionCountdown.tsx  # NEW: 1 s tick from receivedAt; "Ends first"; "Ending soon"; refresh at 0+2 s
    └── pages/
        ├── HomePage.tsx          # NEW
        ├── DashboardPage.tsx     # NEW: fetchAccount on mount; error + retry; account + countdown
        ├── NotFoundPage.tsx      # NEW
        └── VerifyEmailPage.tsx   # on success → navigate('/dashboard', { replace: true })
```

**Structure Decision**: Same web-app layout. Frontend routing concerns go in `src/routing/`, and pages stay in `src/pages/`.

## Key Design Points

1. **The server's view of expiry** (R3): `idleExpiresAt` = the cookie's `ExpiresUtc`, unless this request renews it, in which case it's `now + 60 min`. The absolute end is `auth_time + 8 h`. Both are sent as seconds left.
2. **Countdown without keep-alive** (R4): a 1 s local tick measured from `performance.now()`. At zero the page waits 2 s, then makes exactly one `refresh()`. Focus refreshes (existing) re-sync it.
3. **Guards render nothing sensitive until the status is known** (R1): `loading` shows a placeholder, and the redirects use `replace`.
4. **Session-ended vs logout** (R5): the flag is set only when `/me` returns 401 after `signedIn`.
5. **One sign-in dialog** (R6): a provider shared by the Header and Home.
6. **`no-store` on `/api/*` and a `pageshow` refresh** (R8): no stale account data from caches or the back-forward cache.

## Post-Design Constitution Re-check

No principles to check: **PASS**. No complexity deviations.

## Complexity Tracking

None.
