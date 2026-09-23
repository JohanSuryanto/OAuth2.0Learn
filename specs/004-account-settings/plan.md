# Implementation Plan: Account & Security Settings

**Branch**: `004-account-settings` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-account-settings/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

- **Header user menu**: replaces "Hi {email}" + Logout with an accessible menu button (Dashboard / Account settings / Log out).
- **`/settings` page** with four sections:
  - Profile: edit the display name; the email is read-only.
  - Password: change it (with the current password), or set one for Google-only accounts (needs authentication within the last 10 minutes, otherwise password or Google re-authentication).
  - Sign-in methods: read-only.
  - Active sessions: per-device list and sign-out.
- **Per-device sessions**: a new `user_sessions` table and a `sid` cookie claim, checked by the existing `SessionValidator`.
- **Logout** ends only the current device.
- **Google re-authentication** reuses the feature 001 popup with a special challenge that verifies the same Google account and skips the sign-in.
- **Session-expiry warning dialog**: "Stay signed in" re-issues the cookie (new idle window, same `auth_time`).
- **Planning fix**: Google sign-in no longer overwrites a user-chosen display name (research R8).

## Technical Context

**Language/Version**: C# 13 / .NET 9; TypeScript 6 / React 19.2 / Vite 8 / react-router 8 (unchanged)

**Primary Dependencies**: Unchanged. No new packages: the user-agent description, IP masking and the menu keyboard handling are small in-house code (research R9, R10).

**Storage**: PostgreSQL; one migration, `UserSessions` (new `user_sessions` table) ([data-model.md](./data-model.md))

**Testing**: xUnit + WebApplicationFactory + real Postgres + `FakeTimeProvider` + per-factory client IP (existing); Vitest + React Testing Library + `MemoryRouter`

**Target Platform**: Local development; Chromium/Firefox

**Project Type**: Web application (existing `backend/` + `frontend/`)

**Performance Goals**: At most one extra indexed row lookup per authenticated request (the session row, joined with the user in the existing validator query); a `last_seen_at` write at most every 5 minutes per session; name changes visible within 1 s (SC-003)

**Constraints**:
- Every new POST needs the CSRF header.
- Re-authentication failures share the sign-in throttle.
- The 10-minute check is server-side (`last_auth_at`).
- The expiry dialog makes no requests unless the user clicks.
- Legacy cookies keep working (FR-016).

**Scale/Scope**: About 9 endpoints (7 new, logout and `/me` changed), 1 table, and about 8 new frontend components

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so there are no gates. **PASS (vacuous)** before and after design.

The design still keeps to simplicity:
- no new dependencies;
- one table;
- it reuses the existing session, throttle and popup infrastructure.

## Project Structure

### Documentation (this feature)

```text
specs/004-account-settings/
├── spec.md
├── plan.md               # this file
├── research.md           # R1–R12
├── data-model.md
├── quickstart.md         # 21 manual scenarios
├── contracts/
│   └── account-api.md
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
backend/OAuthLearn.Api/
├── Data/
│   ├── UserSession.cs                 # NEW entity
│   ├── AppDbContext.cs                # + UserSessions mapping
│   ├── UserService.cs                 # Google upsert keeps a user-set display name (R8)
│   ├── SessionStore.cs                # NEW: create / touch / list / revoke / revokeOthers / legacy upgrade
│   └── AccountSecurityService.cs      # NEW: profile, change/set password, reauth (password + Google), recent-auth check
├── Auth/
│   ├── AuthClaims.cs                  # + Sid; ForUser/SessionClaims include sid
│   ├── SessionIssuer.cs               # SignInAsync creates a session row; + ReissueAsync (R3)
│   ├── SessionValidator.cs            # sid → row check; last_seen throttle; legacy upgrade (R1, R2)
│   ├── GoogleAuthEvents.cs            # session row on sign-in; reauth purpose branch; Name claim from DB
│   ├── AuthEndpoints.cs               # logout → current session only; + GET /reauth/google; + POST /session/extend; popup whitelist
│   ├── AccountEndpoints.cs            # + profile, password change/set, reauth/password, sessions list/revoke/revoke-others
│   ├── UserAgentDescriber.cs          # NEW (R9)
│   └── IpMasker.cs                    # NEW (R9)
├── Program.cs                         # Google events: OnRedirectToAuthorizationEndpoint, OnTicketReceived; SessionStore/AccountSecurityService DI
└── Migrations/<ts>_UserSessions.cs

backend/OAuthLearn.Api.Tests/
├── SessionsTests.cs                   # NEW: lifecycle, revoke, logout-one, legacy upgrade
├── AccountSecurityTests.cs            # NEW: profile, change/set password, reauth, throttle, extend
├── DeviceInfoTests.cs                 # NEW: UserAgentDescriber, IpMasker tables
└── (existing tests updated where logout semantics changed)

frontend/src/
├── auth/
│   ├── accountApi.ts                  # NEW: profile, password change/set, reauth, sessions, extend
│   ├── loginPopup.ts                  # openLoginPopup(url?) + reauth_ok / reauth_mismatch results
│   └── AuthContext.tsx                # + extendSession(); login keeps using the default URL
├── components/
│   ├── Header.tsx                     # signed-in part → <UserMenu/>
│   ├── UserMenu.tsx                   # NEW (R10)
│   ├── SessionExpiryWarning.tsx       # NEW (R7)
│   └── settings/
│       ├── ProfileSection.tsx         # NEW
│       ├── PasswordSection.tsx        # NEW (change or set, with re-auth)
│       ├── ReauthPanel.tsx            # NEW
│       ├── SignInMethodsSection.tsx   # NEW (read-only)
│       └── SessionsSection.tsx        # NEW
├── pages/SettingsPage.tsx             # NEW
├── App.tsx                            # + /settings route; <SessionExpiryWarning/> in the shell
public/auth-complete.js                # whitelist + reauth_ok / reauth_mismatch
```

**Structure Decision**: The existing web-app layout. Settings UI pieces go in `components/settings/`.

## Key Design Points

1. **Sessions** (R1–R3): a row per sign-in, `sid` in the cookie, the validator checks the row plus `session_version`. Logout revokes one row. "All others" and password change revoke the other rows, bump the version, and re-issue the current cookie. Legacy cookies are upgraded lazily.
2. **Recent authentication is server-side** (R4): `last_auth_at` on the session row, a 10-minute window, `403 reauth_required` with the available methods.
3. **Google re-auth** (R6): a purpose-tagged challenge with `max_age=0`; the sub must match; `OnTicketReceived` skips the sign-in; two new popup result codes.
4. **Stay signed in** (R7): a cookie re-issue that preserves `auth_time`; it's the only request the expiry dialog can make.
5. **Display name** (R8): Google no longer overwrites it; `Name` claims come from the DB, and a profile save re-issues the cookie.

## Spec and Earlier-Feature Updates Made During Planning

- Feature 001's Google upsert updated the name on every sign-in. It now only fills an empty name (R8), which changes feature 001 FR-015 for names only. Recorded here and in research.

## Post-Design Constitution Re-check

No principles to check: **PASS**. No complexity deviations.

## Complexity Tracking

None.
