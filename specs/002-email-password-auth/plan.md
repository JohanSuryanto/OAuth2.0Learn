# Implementation Plan: Email & Password Registration and Sign-in

**Branch**: `002-email-password-auth` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-email-password-auth/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add email/password registration, verification, sign-in and password reset to the existing app, reusing feature 001's cookie session end to end. The sign-in dialog's forms (already built against a stub) are wired to five new backend endpoints.

**Backend**
- Passwords are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2-HMAC-SHA512, 210k iterations).
- Verification and reset use single-use, hashed tokens sent to a Development-only mailbox (a database table plus a `/dev/mailbox` page).
- Verification requires the token **and** the chosen password, then signs the user in. This closes a pre-registration hijack found during design ([research R4](./research.md#r4-verification-token-plus-password-confirmation-and-signing-in-on-success)).
- Guessing protection is a database-backed failure counter per email and per IP, with keys HMAC'd using a new `Auth:LookupHashKey` secret.
- The Google upsert is extended to link accounts by verified email.

**Frontend**
- Two small pages, `/verify-email` and `/reset-password`, read the token from the URL fragment.
- The dialog gains a "Forgot password?" view and a "Check your inbox" state.

## Technical Context

**Language/Version**: C# 13 / .NET 9; TypeScript 6 / Node.js 22 (unchanged from 001)

**Primary Dependencies**: Unchanged from 001, plus `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (in the ASP.NET Core shared framework, so no new package). No new frontend dependencies.

**Storage**: PostgreSQL (existing `oauthlearn2` database). One new migration: `users` changes plus `password_credentials`, `one_time_tokens`, `rate_limit_events`, `mailbox_messages` ([data-model.md](./data-model.md))

**Testing**: The existing xUnit suite (real Postgres through `TEST_PG_CONNECTION`/Testcontainers, `WebApplicationFactory`, and a `FakeTimeProvider` now injected into the app) and Vitest + React Testing Library

**Target Platform**: Local development on Windows; Chromium/Firefox

**Project Type**: Web application (existing `backend/` + `frontend/`)

**Performance Goals**: Sign-in within 15 s of opening the dialog, well above what's needed. About 200 ms of deliberate hashing per sign-in attempt (2 hashes, research R2)

**Constraints**:
- No plain passwords, emails of failed attempts, or tokens stored or logged.
- Identical responses for registered and unregistered emails.
- Every new POST needs `X-Requested-With: fetch`.
- The dev mailbox must be impossible to enable outside Development and Testing.

**Scale/Scope**: 5 endpoints + 1 dev page; 4 new tables; 2 new frontend pages; about 3 new frontend components

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so there are no gates. **PASS (vacuous)** before and after design.

The design still keeps to simplicity:
- no ASP.NET Core Identity framework, only its hasher;
- no router library;
- no external mail service;
- one migration.

## Project Structure

### Documentation (this feature)

```text
specs/002-email-password-auth/
├── spec.md              # updated during planning (research R4)
├── plan.md              # this file
├── research.md          # R1–R13
├── data-model.md
├── quickstart.md        # 19 manual scenarios
├── contracts/
│   └── password-auth-api.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
backend/OAuthLearn.Api/
├── Program.cs                         # + hasher options, email sender, mailbox route, new endpoints, LookupHashKey check
├── Auth/
│   ├── AuthClaims.cs                  # + ForUser(User, TimeProvider) shared claim builder
│   ├── AuthEndpoints.cs               # logout now uses RequireFetchHeaderFilter
│   ├── GoogleAuthEvents.cs            # uses AuthClaims.ForUser; email_conflict reason
│   ├── PasswordEndpoints.cs           # NEW: register, verify-email, password/sign-in, forgot-password, reset-password
│   ├── RequireFetchHeaderFilter.cs    # NEW: X-Requested-With: fetch → else 400
│   └── SessionIssuer.cs               # NEW: HttpContext.SignInAsync with ForUser claims
├── Passwords/
│   ├── PasswordPolicy.cs              # NEW: FR-005 rules + email/display-name validation (R12)
│   ├── CommonPasswords.cs             # NEW: loads embedded list
│   ├── common-passwords.txt           # NEW: SecLists 10k (MIT), EmbeddedResource
│   └── PasswordHashing.cs             # NEW: wraps PasswordHasher + dummy hash (R1, R2)
├── Throttling/
│   └── AttemptLimiter.cs              # NEW: rate_limit_events rules (R7)
├── Email/
│   ├── IEmailSender.cs                # NEW
│   ├── DevMailboxEmailSender.cs       # NEW: writes mailbox_messages
│   ├── DevMailboxEndpoints.cs         # NEW: GET /dev/mailbox (Development only)
│   └── EmailTemplates.cs              # NEW: verify / reset / "someone tried to register" texts
├── Data/
│   ├── User.cs                        # GoogleSubject nullable; + EmailNormalized, EmailVerifiedAt, CreatedVia
│   ├── PasswordCredential.cs, OneTimeToken.cs, RateLimitEvent.cs, MailboxMessage.cs   # NEW
│   ├── AppDbContext.cs                # new sets + mappings
│   ├── UserService.cs                 # Google upsert with linking (R8)
│   ├── AccountService.cs              # NEW: register / verify / sign-in / forgot / reset orchestration
│   └── TokenService.cs                # NEW: create / consume one-time tokens (R5)
└── Migrations/<ts>_EmailPasswordAuth.cs   # NEW (with data back-fill SQL)

backend/OAuthLearn.Api.Tests/
├── TestAppFactory.cs                  # + FakeTimeProvider, LookupHashKey, Testing env mailbox
├── PasswordPolicyTests.cs             # NEW
├── AccountFlowsTests.cs               # NEW: register/verify/sign-in/forgot/reset through HTTP
├── ThrottlingTests.cs                 # NEW
├── AccountLinkingTests.cs             # NEW: UpsertFromGoogleAsync linking paths
└── (existing tests updated for nullable GoogleSubject / new columns)

frontend/src/
├── App.tsx                            # pathname switch: /verify-email, /reset-password, else dashboard
├── auth/passwordAuthApi.ts            # stub → real fetch calls with typed results
├── components/LoginModal.tsx          # + "Check your inbox" state, "Forgot password?" view
├── components/auth/EmailSignInForm.tsx, RegisterForm.tsx   # map server results/codes to messages
├── components/auth/ForgotPasswordForm.tsx                   # NEW
├── components/auth/formMessages.ts    # + server codes → messages
└── pages/
    ├── VerifyEmailPage.tsx            # NEW: token from #fragment + password → POST verify
    └── ResetPasswordPage.tsx          # NEW: token from #fragment + new password ×2 → POST reset
```

**Structure Decision**: Same web-application layout as 001. New backend code is grouped by concern (`Passwords/`, `Throttling/`, `Email/`) next to the existing `Auth/` and `Data/`.

## Key Design Points

1. **Verification = token + chosen password → signed in** (R4). Registration always replaces the pending password and invalidates older links.
2. **Response indistinguishability** (FR-010, SC-003, SC-008):
   - registration always answers `202` after hashing;
   - sign-in always runs 2 hash verifications;
   - forgot-password always answers `202`, even when throttled.
3. **Throttling** (R7): only failures count; refused attempts don't; keys are HMAC'd with `Auth:LookupHashKey`; all times come from `TimeProvider`.
4. **Linking** (R8): Google matches by `sub`, then by `email_normalized`. Linking discards a pending password and bumps `session_version` when the account was unverified.
5. **One session model**: `SessionIssuer` produces the same claims as Google, so `SessionValidator`, `/me`, logout, and the 60 min / 8 h limits apply unchanged.
6. **Dev mailbox isolation** (R6): the sender and `/dev/mailbox` are registered only in Development and Testing; elsewhere the app fails at startup.
7. **Tokens in URL fragments** (R5): they never reach server logs or `Referer`, and are stripped from the address bar after reading.

## Spec Updates Made During Planning

Research R4 found a pre-registration hijack in the clarified flow. The spec was updated accordingly:
- US1 scenario 3 and FR-012: verification needs the chosen password and signs the user in.
- Edge case: re-registration **replaces** the pending password.

## Post-Design Constitution Re-check

No principles to check: **PASS**. No complexity deviations.

## Complexity Tracking

None.
