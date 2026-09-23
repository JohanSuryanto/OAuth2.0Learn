# Implementation Plan: Google OAuth Login Dashboard

**Branch**: `001-google-oauth-login` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-google-oauth-login/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

A learning project for Google OAuth 2.0: a React dashboard with a Login/Logout button in the top-right. Login opens a popup that runs the **server-side Authorization Code flow** in ASP.NET Core's Google handler, using redirect URI `https://localhost:5001/signin-google`. On success, the backend:
1. upserts the user into PostgreSQL, keyed by Google `sub`;
2. issues an HttpOnly cookie session;
3. tells the opener via `postMessage` and closes the popup.

The React app then reads `/api/auth/me` and shows "Hi {email}". Logout clears the cookie. The React app reaches the API through the Vite dev proxy, so cookie handling between `http://localhost:5174` and `https://localhost:5001` stays same-origin ([research R3](./research.md)).

## Technical Context

**Language/Version**: C# 13 / .NET 9 (SDK 9.0.101 installed); TypeScript 5 / Node.js 22

**Primary Dependencies**: Backend: ASP.NET Core 9 minimal APIs, `Microsoft.AspNetCore.Authentication.Google` 9.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x, `EFCore.NamingConventions`. Frontend: Vite 6, React 19

**Storage**: PostgreSQL 16+ (local); a single `users` table ([data-model.md](./data-model.md))

**Testing**: Backend: xUnit, `Microsoft.AspNetCore.Mvc.Testing`, Testcontainers.PostgreSql. Frontend: Vitest, React Testing Library, jsdom. Plus manual end-to-end checks with a real Google account ([quickstart.md](./quickstart.md))

**Target Platform**: Local development on Windows; latest Chromium or Firefox

**Project Type**: Web application (separate SPA frontend + API backend)

**Performance Goals**: Greeting shown within 2 s of popup close (SC-002); logout reflected within 1 s (SC-003)

**Constraints**: Backend must be `https://localhost:5001`; Google redirect URI fixed at `/signin-google`; FE at `http://localhost:5174`; client secret server-only; secrets kept in User Secrets, outside the repo

**Scale/Scope**: Single developer, a handful of users; 1 page, 4 endpoints, 1 table

**Security posture**: Hardened for local learning (verified email only, server-side revocation, 8 h absolute session cap, security headers, loopback-only DB with least-privilege role). Explicitly not production-ready; see research R5c S6

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template: it has no ratified principles, so there are no gates to evaluate. **Status: PASS (vacuous)**, both before research and after design.

The design still keeps to general simplicity:
- two projects (+ tests);
- no repository layer over EF Core;
- no UI library;
- no server-side session store.

## Project Structure

### Documentation (this feature)

```text
specs/001-google-oauth-login/
├── plan.md              # This file
├── research.md          # Phase 0: decisions R1–R10
├── data-model.md        # Phase 1: users table, session claims, FE state
├── quickstart.md        # Phase 1: setup + validation scenarios
├── contracts/
│   └── auth-api.md      # Phase 1: /api/auth/* + popup postMessage contract
└── tasks.md             # Phase 2 (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
OAuthLearn.sln
backend/
├── OAuthLearn.Api/
│   ├── Program.cs                  # DI, auth (Cookie + Google), CORS, EF, endpoint mapping
│   ├── Auth/
│   │   ├── AuthEndpoints.cs        # /api/auth/login, /me, /logout, /popup-complete
│   │   ├── GoogleAuthEvents.cs     # OnCreatingTicket (verified email, upsert, claims), OnAccessDenied, OnRemoteFailure
│   │   ├── SessionValidator.cs     # cookie OnValidatePrincipal: session_version + 8 h cap
│   │   └── SecurityHeaders.cs      # response-header middleware
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── User.cs
│   │   └── UserService.cs          # UpsertFromGoogleAsync(sub, email, name)
│   ├── Migrations/
│   ├── Properties/launchSettings.json   # https://localhost:5001
│   └── appsettings.json            # Frontend:Origin (no secrets)
└── OAuthLearn.Api.Tests/
    ├── AuthEndpointsTests.cs       # /me 200/401, /logout 204/400, popup-complete origin, headers
    ├── SessionValidatorTests.cs    # revoked / 8 h / DB-down rejection
    └── UserServiceTests.cs         # insert, update, one row per sub, revoke

frontend/
├── index.html
├── vite.config.ts                  # port 5174 strict, proxy /api → https://localhost:5001
├── package.json
├── public/
│   ├── auth-complete.html          # popup lands here; strict meta CSP; loads auth-complete.js
│   └── auth-complete.js            # BroadcastChannel → dashboard, then closes
└── src/
    ├── main.tsx
    ├── App.tsx                     # layout: Header + placeholder dashboard body
    ├── App.css
    ├── components/
    │   ├── Header.tsx              # "Hi {email}" + Login/Logout button, error text
    │   └── Header.test.tsx
    └── auth/
        ├── AuthContext.tsx         # status/user/error, login(), logout(), refresh()
        ├── AuthContext.test.tsx
        ├── authApi.ts              # fetchMe(), logout()
        ├── loginPopup.ts           # window.open, focus-if-open, BroadcastChannel listener
        └── loginPopup.test.ts
```

**Structure Decision**: Web application layout, with `backend/` (ASP.NET Core API + xUnit tests) and `frontend/` (Vite React SPA) at the repo root, tied together by `OAuthLearn.sln` for the .NET side.

## Key Design Points

1. **Auth pipeline**:
   - `AddAuthentication(Cookie)` with `DefaultChallengeScheme = Cookie`, **not Google**, so `/me` returns 401 (research R5b). `/api/auth/login` challenges Google explicitly.
   - Cookie: `HttpOnly`, `SecurePolicy=Always`, `SameSite=Lax`, 60 min sliding, non-persistent.
   - `OnRedirectToLogin` / `OnRedirectToAccessDenied` return 401/403.
   - Google options:
     - `ClientId`/`ClientSecret` come from config.
     - `CallbackPath=/signin-google` (the default).
     - Scopes: `openid profile email`.
     - `SaveTokens=false`.
2. **Upsert on ticket creation** (R5): `OnCreatingTicket`:
   - reads `sub`/email/name;
   - calls `UserService`;
   - adds the `app_user_id` claim;
   - throws if email is missing or the DB write fails.
   
   `OnAccessDenied` (the user cancelled) redirects to `popup-complete?error=access_denied`, and `OnRemoteFailure` (anything else) redirects to `?error=signin_failed`.
3. **Popup handshake** (R2):
   1. `popup-complete` redirects the popup to `{Frontend:Origin}/auth-complete.html?result=...` (the origin comes from config only).
   2. That page notifies the dashboard over `BroadcastChannel("oauth-login")`, because it is on the same origin, and then closes itself.
   3. `popup.closed` is only a hint to refresh `/me`. This keeps the flow working even when Google's COOP cuts `window.opener`.
4. **Logout**: `POST` + `X-Requested-With` header → increment `users.session_version` → `SignOutAsync(Cookie)`. Google's session is untouched and the DB row is kept.
5. **Dev startup**: the backend applies EF migrations automatically in Development.
6. **Session validation** (research R5c S1/S4): the cookie `OnValidatePrincipal` runs on every request and rejects the session if its `session_version` is stale, `auth_time` is more than 8 h old, the user is missing, or the DB can't be reached. The app fails closed.
7. **Verified email only** (S2): `OnCreatingTicket` requires `email_verified`/`verified_email == true`.
8. **Security headers** (S5): a small middleware in `Program.cs` sets them. `auth-complete.html` gets a strict meta CSP plus an external `auth-complete.js`.
9. **Local-only posture** (S3/S6/S7): Postgres is bound to 127.0.0.1 with a dedicated non-superuser role. The README has a "Not production-ready" section and secret-hygiene rules.

## Post-Design Constitution Re-check

There are still no constitution principles to check: **PASS**. No complexity deviations need justifying.

## Complexity Tracking

None.
