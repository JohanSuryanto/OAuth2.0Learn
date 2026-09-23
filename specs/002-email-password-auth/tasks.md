---

description: "Task list for Email & Password Registration and Sign-in"
---

# Tasks: Email & Password Registration and Sign-in

**Input**: Design documents from `/specs/002-email-password-auth/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/password-auth-api.md, quickstart.md

**Tests**: Included. plan.md (Technical Context → Testing) and research R13 commit to extending the existing xUnit and Vitest suites, as in feature 001. Backend DB tests need `TEST_PG_CONNECTION` (or Docker).

**Organization**: Tasks are grouped by user story. The frontend forms (FR-001–FR-003, the client-side part of FR-005) were built before this task list; see the spec status line. Tasks here wire them to the backend.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 register+verify, US2 sign-in, US3 guessing protection, US4 Google/password linking, US5 password reset

## Path Conventions

- Backend: `backend/OAuthLearn.Api/`; tests: `backend/OAuthLearn.Api.Tests/`
- Frontend: `frontend/src/`
- API shapes, status codes and UI messages: `specs/002-email-password-auth/contracts/password-auth-api.md` (referred to below as **the contract**)

---

## Phase 1: Setup

- [X] T001 Download the SecLists list `Passwords/Common-Credentials/10k-most-common.txt` (MIT licence) to `backend/OAuthLearn.Api/Passwords/common-passwords.txt`. Prepend comment lines starting with `#` giving the source URL and MIT attribution. In `backend/OAuthLearn.Api/OAuthLearn.Api.csproj` add `<EmbeddedResource Include="Passwords\common-passwords.txt" />` (research R3)
- [X] T002 In `backend/OAuthLearn.Api/Program.cs`, add `"Auth:LookupHashKey"` to the `required` keys in `EnsureRequiredConfiguration`. In `backend/OAuthLearn.Api.Tests/TestAppFactory.cs`, add `builder.UseSetting("Auth:LookupHashKey", "dGVzdC1sb29rdXAtaGFzaC1rZXktMzItYnl0ZXMtbG9uZyE=")`. The developer sets the real value with the command in `specs/002-email-password-auth/quickstart.md` (research R7)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Data

- [X] T003 Update `backend/OAuthLearn.Api/Data/User.cs`:
  - `GoogleSubject` becomes `string?` (drop `required`);
  - add `required string EmailNormalized`, `DateTimeOffset? EmailVerifiedAt`, and `string CreatedVia` (values `"google"` / `"password"`);
  - add a navigation `PasswordCredential? PasswordCredential`.
- [X] T004 [P] Create the entities in `backend/OAuthLearn.Api/Data/`:
  - `PasswordCredential.cs`: `Guid UserId`, `string? ActiveHash`, `string? PendingHash`, `DateTimeOffset? ActiveSetAt`, `DateTimeOffset? PendingSetAt`.
  - `OneTimeToken.cs`: `Guid Id`, `Guid UserId`, `string Purpose`, `byte[] TokenHash`, `DateTimeOffset CreatedAt`, `DateTimeOffset ExpiresAt`, `DateTimeOffset? UsedAt`, plus `public static class TokenPurposes { VerifyEmail = "verify_email"; ResetPassword = "reset_password"; }`.
  - `RateLimitEvent.cs`: `long Id`, `string Bucket`, `byte[] KeyHash`, `DateTimeOffset OccurredAt`.
  - `MailboxMessage.cs`: `Guid Id`, `string ToAddress`, `string Subject`, `string BodyText`, `DateTimeOffset CreatedAt`.
- [X] T005 Update `backend/OAuthLearn.Api/Data/AppDbContext.cs` with DbSets and mappings per data-model.md. Depends on T003, T004.
  - **`users`**:
    - `google_subject` "**now NULL**, still UNIQUE";
    - `email_normalized` "NOT NULL", "**UNIQUE**";
    - `email_verified_at` "NULL";
    - `created_via` "NOT NULL", default `'google'`;
    - one-to-one `PasswordCredential` with FK `user_id` "ON DELETE CASCADE".
  - **`password_credentials`**: PK `user_id`; `active_hash`/`pending_hash` "NULL"; `active_set_at`/`pending_set_at` "NULL" `timestamptz`.
  - **`one_time_tokens`**:
    - PK `id` (`ValueGeneratedNever`);
    - `user_id` FK "ON DELETE CASCADE, indexed";
    - `purpose` "NOT NULL";
    - `token_hash` "NOT NULL, **UNIQUE**";
    - `created_at`/`expires_at` "NOT NULL";
    - `used_at` "NULL".
  - **`rate_limit_events`**: PK `id` "bigint identity"; `bucket`/`key_hash`/`occurred_at` "NOT NULL"; index on `(bucket, key_hash, occurred_at DESC)`.
  - **`mailbox_messages`**: all columns "NOT NULL".
- [X] T006 Generate the migration with `$env:ConnectionStrings__Default='Host=127.0.0.1;Database=design_time_only'; dotnet tool run dotnet-ef migrations add EmailPasswordAuth --project backend/OAuthLearn.Api --output-dir Migrations`. Then edit the generated `Up()` so existing rows are back-filled **before** the NOT NULL constraint and unique index are created:
  1. Add `email_normalized` as nullable.
  2. `migrationBuilder.Sql("UPDATE users SET email_normalized = lower(btrim(email)), email_verified_at = created_at, created_via = 'google';")`
  3. `AlterColumn` `email_normalized` to NOT NULL.
  4. Create the unique index.
  
  Verify `Down()` reverses everything. Depends on T005.
- [X] T007 Update `UserService.UpsertFromGoogleAsync` in `backend/OAuthLearn.Api/Data/UserService.cs` for the new columns without changing feature 001 behaviour:
  - on insert: set `EmailNormalized = email.Trim().ToLowerInvariant()`, `EmailVerifiedAt = now`, `CreatedVia = "google"`;
  - on update: refresh `EmailNormalized` along with `Email`.
  
  Linking comes in US4. Add `public static string NormalizeEmail(string email)` to `UserService` and use it everywhere. Fix compile errors in the existing tests caused by the nullable `GoogleSubject`. Depends on T003.

### Session, CSRF, hashing, policy

- [X] T008 [P] Add `public static List<Claim> ForUser(User user, string nameIdentifier, TimeProvider time)` to `backend/OAuthLearn.Api/Auth/AuthClaims.cs`. It returns `NameIdentifier`, `Email`, `Name` (only if `DisplayName` is not null), `app_user_id`, `session_version` and `auth_time` (Unix seconds). Refactor `GoogleAuthEvents.OnCreatingTicket` to add the three custom claims via this helper, without adding duplicate NameIdentifier/Email/Name claims that Google already set. Create `backend/OAuthLearn.Api/Auth/SessionIssuer.cs` with `static Task SignInAsync(HttpContext ctx, User user)`: it builds a `ClaimsIdentity(ForUser(user, user.GoogleSubject ?? user.Id.ToString(), time), CookieAuthenticationDefaults.AuthenticationScheme)` and calls `ctx.SignInAsync(Cookie, principal, new AuthenticationProperties { IsPersistent = false })`, which always issues a new ticket (FR-009, research R9)
- [X] T009 [P] Create `backend/OAuthLearn.Api/Auth/RequireFetchHeaderFilter.cs`, an `IEndpointFilter` that returns `Results.BadRequest()` unless `X-Requested-With == "fetch"`. In `backend/OAuthLearn.Api/Auth/AuthEndpoints.cs`, replace the inline check in `/logout` with `.AddEndpointFilter<RequireFetchHeaderFilter>()`. The existing logout tests must still pass (FR-017, research R10)
- [X] T010 [P] Create `backend/OAuthLearn.Api/Passwords/PasswordHashing.cs`:
  - a singleton wrapping `PasswordHasher<User>`;
  - register `builder.Services.Configure<PasswordHasherOptions>(o => { o.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3; o.IterationCount = 210_000; })` in `Program.cs`;
  - `string Hash(string password)`;
  - `PasswordCheck Verify(string? hash, string password)` returning `Failed | Success | SuccessRehashNeeded`. When `hash` is null it verifies against a dummy hash computed once at construction and returns `Failed`, so every call costs one hash (research R1, R2).
- [X] T011 [P] Create `backend/OAuthLearn.Api/Passwords/CommonPasswords.cs`: loads the embedded `common-passwords.txt` once, skipping `#` lines and blanks, into a `HashSet<string>` of lower-cased entries, with `bool Contains(string password)` (case-insensitive). Create `backend/OAuthLearn.Api/Passwords/PasswordPolicy.cs` with static validators returning an error code or null, using the codes in research R12:
  - `ValidateEmail(string?)`: `email_invalid` unless trimmed length ≤ 254, `MailAddress.TryCreate` succeeds, the parsed `.Address` equals the trimmed input, and the domain contains `.`;
  - `ValidateNewPassword(string?, string email, CommonPasswords)`: `password_too_short` (< 10), `password_too_long` (> 128), `password_is_email` (trimmed and lower-cased equals the normalized email), `password_common`;
  - `ValidateDisplayName(string?)`: `display_name_too_long` if trimmed > 100.
  
  Register `CommonPasswords` as a singleton.

### Email, tokens, throttling

- [X] T012 [P] Create `backend/OAuthLearn.Api/Email/IEmailSender.cs` (`Task SendAsync(string to, string subject, string bodyText, CancellationToken ct)`) and `backend/OAuthLearn.Api/Email/DevMailboxEmailSender.cs` (inserts a `MailboxMessage` using `TimeProvider`).
  - Create `backend/OAuthLearn.Api/Email/EmailTemplates.cs` with three static methods returning `(subject, body)` and building links from `Frontend:Origin`:
    - `VerifyEmail(origin, token)` with link `{origin}/verify-email#token={token}`;
    - `ResetPassword(origin, token)` with link `{origin}/reset-password#token={token}`;
    - `RegistrationAttempt(origin)`: "Someone tried to create an account with this email. If it was you, sign in or use Forgot password."
  - In `Program.cs`, register `IEmailSender` → `DevMailboxEmailSender` only when `builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")`. Otherwise throw `InvalidOperationException("No email sender configured for this environment…")` at startup (research R6).
- [X] T013 Create `backend/OAuthLearn.Api/Email/DevMailboxEndpoints.cs`: `MapDevMailbox()` maps `GET /dev/mailbox` **only when** `app.Environment.IsDevelopment()`.
  - It renders HTML (HTML-encode every value with `WebUtility.HtmlEncode`; turn URLs in the body into `<a>` links after encoding) of the latest 50 messages, newest first, with to, subject, time and body.
  - It sets `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'` on its response.
  - Change `backend/OAuthLearn.Api/Auth/SecurityHeaders.cs` to set `Content-Security-Policy` only if the response doesn't already have one. The other headers are unchanged. Depends on T012.
- [X] T014 [P] Create `backend/OAuthLearn.Api/Data/TokenService.cs` (scoped; `AppDbContext`, `TimeProvider`):
  - `Task<string> CreateAsync(Guid userId, string purpose, TimeSpan lifetime, ct)`: deletes the user's unused tokens for that purpose, generates 32 bytes with `RandomNumberGenerator`, stores `SHA256` of the raw token, and returns `WebEncoders.Base64UrlEncode(raw)`.
  - `Task<OneTimeToken?> FindValidAsync(string rawToken, string purpose, ct)`: decodes safely (invalid input → null), looks up by hash, and requires `UsedAt == null && ExpiresAt > now`.
  - `Task MarkUsedAsync(OneTimeToken, ct)`.
  - `Task DeleteAllForUserAsync(Guid userId, ct)`.
  
  Constants: `VerifyEmailLifetime = 24h`, `ResetPasswordLifetime = 30min` (research R5).
- [X] T015 [P] Create `backend/OAuthLearn.Api/Throttling/AttemptLimiter.cs` (scoped; `AppDbContext`, `TimeProvider`, `IConfiguration`):
  - `byte[] Key(string bucket, string value)` = `HMACSHA256(Convert.FromBase64String(config["Auth:LookupHashKey"]), UTF8(bucket + ":" + value))`.
  - `Task RecordAsync(bucket, value)`: inserts a row; with probability 1/100 it also deletes rows older than 24 h.
  - `Task<int> CountSinceAsync(bucket, value, TimeSpan window)`.
  - `Task<bool> IsLockedOutAsync(bucket, value, int limit, TimeSpan window)`: takes the `limit` newest events; locked if there are `limit` of them, the newest is less than `window` old, and newest − oldest ≤ `window` (research R7).
  - `Task ClearAsync(bucket, value)`.
  - `public static class Buckets` holding the 5 bucket names from data-model.md.
  - `static string ClientIp(HttpContext)` = `RemoteIpAddress?.ToString() ?? "unknown"`.

### Frontend and test plumbing

- [X] T016 [P] In `backend/OAuthLearn.Api.Tests/TestAppFactory.cs`:
  - replace `TimeProvider` with a `FakeTimeProvider` exposed as `public FakeTimeProvider Time` (starting at 2026-09-23T08:00Z), using `services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Time)`;
  - add `Task<HttpResponseMessage> PostJsonAsync(this HttpClient, string url, object body)`, which sets `X-Requested-With: fetch`;
  - add `Task<string?> LatestTokenAsync(PostgresFixture db, string to, string path)`, which reads the newest `mailbox_messages` row for `to` and extracts the token after `{path}#token=` (put it in a new `backend/OAuthLearn.Api.Tests/TestHelpers.cs`).
  
  Clients must keep cookies (the default `HandleCookies = true`).
- [X] T017 [P] Rewrite the base of `frontend/src/auth/passwordAuthApi.ts`:
  - keep the `NotAvailableError` export (tests use it) but stop throwing it;
  - add `async function postJson(url, body)` with `method: 'POST'`, `credentials: 'include'`, headers `Content-Type: application/json` and `X-Requested-With: fetch`, which returns `{ status, body }` (JSON parsed when present);
  - export the types `FieldErrors = Record<string, string>` and `ApiFailure = { reason: 'too_many_attempts' } | { reason: 'validation'; errors: FieldErrors } | { reason: 'unexpected' }`.
  
  Extend `frontend/src/components/auth/formMessages.ts` with a `SERVER_MESSAGES` map covering every UI message in the contract's "UI messages" table, and `fieldMessage(code)` covering every validation code (`email_invalid`, `password_too_short`, `password_too_long`, `password_is_email`, `password_common`, `display_name_too_long`, `password_mismatch`, `token_invalid`).

**Checkpoint**: `dotnet build` and `dotnet test` pass (the existing 34 tests still green after T007/T009), the migration applies, `npm test` passes.

---

## Phase 3: User Story 1 - Create an account with email and password (Priority: P1) 🎯 MVP

**Goal**: Register → "Check your inbox" → open the link from `/dev/mailbox` → confirm the password → signed in.

**Independent Test**: Quickstart scenarios 1–4, 9 and 10.

- [X] T018 [US1] Create `backend/OAuthLearn.Api/Data/AccountService.cs` (scoped; `AppDbContext`, `PasswordHashing`, `CommonPasswords`, `TokenService`, `IEmailSender`, `AttemptLimiter`, `IConfiguration`, `TimeProvider`, `ILogger<AccountService>`) with `Task<RegisterOutcome> RegisterAsync(string email, string password, string? displayName, string clientIp, ct)`:
  1. Validate with `PasswordPolicy`, returning field errors.
  2. If `CountSinceAsync(register_ip, ip, 1h) >= 5`, return `TooManyAttempts`; otherwise `RecordAsync(register_ip, ip)`.
  3. `hash = Hash(password)`, computed **before** looking up the user (R2).
  4. Look up the user by `EmailNormalized`, including `PasswordCredential`:
     - **none**: insert a `User` (`CreatedVia="password"`, `EmailVerifiedAt=null`, `GoogleSubject=null`, `DisplayName` trimmed or null) and a credential with `PendingHash=hash`, `PendingSetAt=now`;
     - **exists with `ActiveHash != null`**: send `EmailTemplates.RegistrationAttempt` and change nothing;
     - **exists without an active hash**: set or replace `PendingHash`/`PendingSetAt`, creating the credential row if missing.
  5. For the new and pending cases, `TokenService.CreateAsync(VerifyEmail)` (which invalidates the older link) and send `EmailTemplates.VerifyEmail`.
  6. Return `Accepted`.
  
  Use one `SaveChangesAsync` per flow; the email insert goes through the same `DbContext`. Log "registration requested" with **no** email in the message (FR-018). (FR-003, FR-004, FR-012, FR-014 registration bullets, research R4)
- [X] T019 [US1] Add `Task<VerifyOutcome> VerifyEmailAsync(string token, string password, string clientIp, ct)` to `AccountService`:
  1. `FindValidAsync(token, VerifyEmail)`; if null → `TokenInvalid`.
  2. Load the user and credential.
  3. If the email or IP is locked out (same limits as sign-in, T027) → `TooManyAttempts`.
  4. `Verify(credential?.PendingHash, password)`; on `Failed` → record `signin_fail_email` and `signin_fail_ip` and return `PasswordMismatch` (the token stays usable).
  5. On success:
     - `ActiveHash = PendingHash`, `ActiveSetAt = now`, `PendingHash = null`;
     - `EmailVerifiedAt ??= now`, `LastLoginAt = now`;
     - `MarkUsedAsync`;
     - `ClearAsync(signin_fail_email, email)`;
     - return `Verified(user)`.
- [X] T020 [US1] Create `backend/OAuthLearn.Api/Auth/PasswordEndpoints.cs` with `MapPasswordEndpoints()`, called from `Program.cs`. It maps group `/api/auth` with `.AddEndpointFilter<RequireFetchHeaderFilter>().AllowAnonymous()`:
  - `POST /register` (request record `RegisterRequest(string? Email, string? Password, string? DisplayName)`) → per the contract: `202 {}`, `400 { errors }` or `429 { code: "too_many_attempts" }`;
  - `POST /verify-email` (`VerifyEmailRequest(string? Token, string? Password)`) → `204` after `SessionIssuer.SignInAsync(ctx, user)`, or `400 { errors: { token: "token_invalid" } }`, `400 { errors: { password: "password_mismatch" } }`, `429`.
  
  Register `AccountService`, `TokenService`, `AttemptLimiter` and `PasswordHashing` in `Program.cs`. Depends on T018, T019.
- [X] T021 [P] [US1] In `frontend/src/auth/passwordAuthApi.ts` implement:
  - `register(request): Promise<{ ok: true } | ({ ok: false } & ApiFailure)>`: 202 → ok; 400 → validation; 429 → too_many_attempts; else unexpected;
  - `verifyEmail(token, password): Promise<{ ok: true } | ({ ok: false } & ({ reason: 'token_invalid' } | { reason: 'password_mismatch' } | ApiFailure))>`.
- [X] T022 [US1] Update `frontend/src/components/auth/RegisterForm.tsx` to use the result type:
  - on `ok`, call a new prop `onCheckInbox(email)` instead of `onRegistered`;
  - on `validation`, show `fieldMessage(code)` under the matching field (`email`, `password`, `displayName`);
  - on `too_many_attempts` or `unexpected`, show the form-level alert with the contract message.
  
  In `frontend/src/components/LoginModal.tsx`, add a `checkInbox` mode: the title is "Check your inbox", the body is "Check your inbox — we've sent a verification link to {email}.", with a "Back to sign in" link-button. Depends on T021.
- [X] T023 [US1] Create `frontend/src/pages/VerifyEmailPage.tsx`:
  - on mount, read `token` from `window.location.hash` (`#token=…`) into state and call `history.replaceState(null, '', window.location.pathname)`;
  - render the heading "Verify your email", a `PasswordInput` "Password you chose" (`autoComplete="current-password"`) and a "Verify and sign in" button;
  - on submit, call `verifyEmail`:
    - ok → `await refresh()` from `useAuth`, then show "Your email is verified. You're signed in." with a link to `/`;
    - `token_invalid` → "This link is no longer valid. Sign in to get a new one." (hide the form);
    - `password_mismatch` → field error;
    - 429 / unexpected → alert.
  - A missing token is treated as `token_invalid`.
  
  In `frontend/src/App.tsx`, switch on `window.location.pathname`: `/verify-email` → `<VerifyEmailPage/>` inside the same app shell (Header plus main); anything else → the dashboard. Styles go in `frontend/src/App.css`, reusing `.card`, `.field`, `.primary-button`. Depends on T021.
- [X] T024 [P] [US1] Create `backend/OAuthLearn.Api.Tests/AccountFlowsTests.cs` (`[Collection(PostgresCollection.Name)]`, a new `TestAppFactory` per class, unique emails via `Guid`) with register and verify tests:
  - a new email → 202, one `users` row with `created_via='password'`, `email_verified_at` null, a pending hash starting `AQAAAAIAA` (V3), and a verify link in the mailbox;
  - an existing **active** email → the same 202 body, a "tried to create an account" notice, and the active hash unchanged;
  - validation codes: short, long, equal to email, common (`qwertyuiop` — the list has no 10+ char `password123`), bad email, display name of 101 characters;
  - the 6th registration from one IP within an hour → 429, and after `Time.Advance(61 min)` → 202;
  - verify with the correct password → 204 and a `Set-Cookie` of `oauthlearn.auth`; a follow-up `GET /api/auth/me` with the same client → 200 with the email;
  - verify with the wrong password → `password_mismatch`, and the same token still works afterwards;
  - reusing a used token, or using one after `Time.Advance(24h + 1 min)` → `token_invalid`;
  - **the hijack case (R4)**: A registers the email with password `attacker-pass-1`; B registers the same email with `victim-pass-12`; A's link → `token_invalid`; B's link with `attacker-pass-1` → `password_mismatch`; B's link with `victim-pass-12` → 204;
  - a missing `X-Requested-With` → 400;
  - no response body ever contains the password.
- [X] T025 [P] [US1] Frontend tests:
  - update `frontend/src/components/auth/authForms.test.tsx` for `RegisterForm`: `onCheckInbox` is called on ok; the server `password_common` → "This password is too common. Choose a less predictable one." under Password; 429 → "Too many attempts. Please try again later.";
  - add to `frontend/src/components/LoginModal.test.tsx` that a successful registration shows the "Check your inbox" view with the email and "Back to sign in" returns to the sign-in form;
  - create `frontend/src/pages/VerifyEmailPage.test.tsx`: the token is read from the hash and removed from the URL; success calls `refresh` and shows the verified message; `token_invalid` hides the form; `password_mismatch` shows the field error.
  
  Mock `passwordAuthApi` in all three.

**Checkpoint**: Quickstart 1–4 work end to end with `/dev/mailbox`.

---

## Phase 4: User Story 2 - Sign in with email and password (Priority: P1)

**Goal**: Email + password sign-in producing the same session as Google.

**Independent Test**: Quickstart scenarios 5, 6, 7 and 9.

- [X] T026 [US2] Add `Task<SignInOutcome> SignInAsync(string email, string password, string clientIp, ct)` to `AccountService`:
  1. The throttle check comes in T027 (leave a clearly marked call site).
  2. Look up the user by `EmailNormalized` (the email only needs to be non-empty; don't validate its format, so any typo gives the generic result) including the credential.
  3. **Always** run `activeResult = Verify(credential?.ActiveHash, password)` and `pendingResult = Verify(credential?.PendingHash, password)`, i.e. exactly two hashes (R2).
  4. If `activeResult != Failed` and `EmailVerifiedAt != null`:
     - `LastLoginAt = now`;
     - on `SuccessRehashNeeded`, set `ActiveHash = Hash(password)`;
     - clear the email failures;
     - return `SignedIn(user)`.
  5. Else if `pendingResult != Failed`: `TokenService.CreateAsync(VerifyEmail)`, send `EmailTemplates.VerifyEmail`, and return `EmailNotVerified` (not a failure).
  6. Else: record `signin_fail_email` and `signin_fail_ip` and return `InvalidCredentials`.
  
  Log "password sign-in succeeded" with the user id, and "password sign-in failed" with **no** email (FR-018). (FR-007–FR-010, FR-012 unverified bullet)
- [X] T027 [US2] Add `POST /password/sign-in` (`SignInRequest(string? Email, string? Password)`) to `backend/OAuthLearn.Api/Auth/PasswordEndpoints.cs`. It returns `204` plus `SessionIssuer.SignInAsync`, `401 { code: "invalid_credentials" }`, `403 { code: "email_not_verified" }` or `429 { code: "too_many_attempts" }`, per the contract. Null or empty email/password → treat as `invalid_credentials` (still run both hash verifications against the dummy hash). Depends on T026.
- [X] T028 [P] [US2] Implement `signInWithPassword(email, password): Promise<{ ok: true } | ({ ok: false } & ({ reason: 'invalid_credentials' } | { reason: 'email_not_verified' } | ApiFailure))>` in `frontend/src/auth/passwordAuthApi.ts`. Update `frontend/src/components/auth/EmailSignInForm.tsx`:
  - ok → `onSignedIn()` (the modal already calls `refresh`, and the dialog closes when signed in);
  - `invalid_credentials` → "Email or password is incorrect.";
  - `email_not_verified` → "Please verify your email first. We've sent you a new link.";
  - `too_many_attempts` → "Too many attempts. Please try again later.";
  - unexpected → the generic message.
  
  Clear the password field after any failure.
- [X] T029 [P] [US2] Add sign-in tests to `backend/OAuthLearn.Api.Tests/AccountFlowsTests.cs` (helper: register, then verify via the mailbox token):
  - correct password → 204, and `/me` returns the email;
  - `  UPPER@case  ` form of the email works;
  - a wrong password, an unknown email, and a Google-only account (inserted via `UserService.UpsertFromGoogleAsync`) all → `401` with identical bodies;
  - an unverified account with the correct pending password → 403 `email_not_verified`, a new mailbox message, and the previous verify token now `token_invalid`;
  - after sign-in, `POST /api/auth/logout` → 204, and reusing the old cookie value on a fresh client → `/me` 401 (session_version, SC-006);
  - signing in twice issues a different cookie value each time (FR-009).
- [X] T030 [P] [US2] Update the `EmailSignInForm` tests in `frontend/src/components/auth/authForms.test.tsx` for each result: the right message for each reason, `onSignedIn` called on ok, and the password field cleared after a failure.

**Checkpoint**: Quickstart 5–7 and 9 pass. Password sessions behave like Google sessions.

---

## Phase 5: User Story 3 - Protection against password guessing (Priority: P1)

**Goal**: Lock out after 5 failures per email or 20 per IP within 15 minutes, identically for unknown emails.

**Independent Test**: Quickstart scenario 8, plus `ThrottlingTests`.

- [X] T031 [US3] At the marked call site in `AccountService.SignInAsync` (T026), and in `VerifyEmailAsync` (T019), check `IsLockedOutAsync(signin_fail_email, normalizedEmail, 5, 15 min)` and `IsLockedOutAsync(signin_fail_ip, ip, 20, 15 min)` **before** any lookup or hash. If either is locked, return `TooManyAttempts` without recording anything. Only `InvalidCredentials` and `PasswordMismatch` outcomes record failures. Log "sign-in throttled" with the bucket name only. (FR-011)
- [X] T032 [P] [US3] Create `backend/OAuthLearn.Api.Tests/ThrottlingTests.cs` using `factory.Time`:
  - 5 wrong passwords → the 6th attempt **with the correct password** → 429;
  - `Time.Advance(15 min + 1 s)` after the last failure → the correct password → 204;
  - the same 5+1 sequence for an unregistered email returns identical status and body at every step to the registered case;
  - 20 failures spread across 20 different emails from one client → the 21st attempt for a new email → 429;
  - a successful sign-in clears that email's counter (4 failures, then success, then 4 more failures → still not locked);
  - `rate_limit_events.key_hash` never equals the UTF-8 bytes of the email;
  - unit tests of `IsLockedOutAsync` edge cases (exactly 5 within 15 min → locked; 5 spread over 20 min → not locked).
- [X] T033 [P] [US3] Add a frontend test to `frontend/src/components/auth/authForms.test.tsx`: `too_many_attempts` from sign-in shows "Too many attempts. Please try again later." and keeps the form usable.

**Checkpoint**: Quickstart 8 passes.

---

## Phase 6: User Story 4 - Google and password on the same email (Priority: P2)

**Goal**: One account per email, linked only after ownership is proven (FR-014, FR-015).

**Independent Test**: Quickstart scenarios 14–16, plus `AccountLinkingTests`.

- [X] T034 [US4] Rewrite `UserService.UpsertFromGoogleAsync` in `backend/OAuthLearn.Api/Data/UserService.cs` per research R8:
  1. Find by `GoogleSubject`, then update the email, name and `EmailNormalized`. If another user already has the new `EmailNormalized`, throw `SignInRejectedException("email_conflict", …)` and change nothing.
  2. Else find by `EmailNormalized` (include the credential) and link:
     - `GoogleSubject = sub`;
     - `wasUnverified = EmailVerifiedAt == null`; `EmailVerifiedAt ??= now`;
     - if `credential?.PendingHash != null`, set `PendingHash = null` and `PendingSetAt = null`, deleting the row if `ActiveHash` is also null, and set `hadPending = true`;
     - if `wasUnverified || hadPending`, do `SessionVersion++`;
     - delete the user's unused `verify_email` tokens.
  3. Else insert a new user (`CreatedVia = "google"`, `EmailVerifiedAt = now`).
  
  In `backend/OAuthLearn.Api/Auth/GoogleAuthEvents.cs`, keep mapping all `SignInRejectedException`s to `signin_failed`; add `email_conflict` to the logged reason codes.
- [X] T035 [P] [US4] Create `backend/OAuthLearn.Api.Tests/AccountLinkingTests.cs`. It calls `UserService.UpsertFromGoogleAsync` directly, and uses `AccountService` or HTTP for the password side:
  - a Google-only account, then password registration → pending; verify → 204; now both the password sign-in and a repeated Google upsert reach the same `users.id`, and there's one row for the email;
  - a verified password account, then Google upsert with the same email → the same `id`, `google_subject` set, the active hash kept, `session_version` unchanged;
  - an unverified password registration, then Google upsert with the same email → the pending hash removed; signing in with that password → 401 `invalid_credentials` (not `email_not_verified`); the old verify token → `token_invalid`; `session_version` incremented;
  - an existing Google user whose Google email changes to another account's email → `SignInRejectedException` with reason `email_conflict`, and neither row changes;
  - Google upsert with a mixed-case email matches the lower-case `email_normalized`.

**Checkpoint**: Quickstart 14–16 pass. SC-007 is covered by tests.

---

## Phase 7: User Story 5 - Forgotten password (Priority: P3)

**Goal**: "Forgot password?" → a reset link in the mailbox → new password → all sessions end.

**Independent Test**: Quickstart scenarios 11–13.

- [X] T036 [US5] Add to `AccountService`:
  - `Task<ForgotOutcome> ForgotPasswordAsync(string email, string ip, ct)`:
    1. Validate the email format → `email_invalid`.
    2. Check whether `CountSinceAsync(reset_email, email, 1h) >= 3` or `CountSinceAsync(reset_ip, ip, 1h) >= 10`; if limited, return `Accepted` doing nothing else.
    3. Otherwise record both events.
    4. If a user exists: `TokenService.CreateAsync(ResetPassword)` and send `EmailTemplates.ResetPassword`.
    5. Return `Accepted`.
  - `Task<ResetOutcome> ResetPasswordAsync(string token, string newPassword, ct)`:
    1. `FindValidAsync(token, ResetPassword)` → `TokenInvalid`.
    2. `ValidateNewPassword` against the user's email → field error `newPassword` (the token stays usable).
    3. Otherwise: upsert the credential with `ActiveHash = Hash(newPassword)`, `ActiveSetAt = now`, `PendingHash = null`, `PendingSetAt = null`; `EmailVerifiedAt ??= now`; `SessionVersion++`; `DeleteAllForUserAsync`; `ClearAsync(signin_fail_email)`.
    4. Return `Reset`.
  
  Log "password reset requested" (no email) and "password reset completed" with the user id. (FR-016)
- [X] T037 [US5] Add `POST /forgot-password` (`ForgotPasswordRequest(string? Email)`) → `202 {}` or `400 { errors: { email } }`, and `POST /reset-password` (`ResetPasswordRequest(string? Token, string? NewPassword)`) → `204`, `400 { errors: { token: "token_invalid" } }` or `400 { errors: { newPassword: code } }`, to `backend/OAuthLearn.Api/Auth/PasswordEndpoints.cs`, per the contract. Depends on T036.
- [X] T038 [P] [US5] Implement `forgotPassword(email)` and `resetPassword(token, newPassword)` in `frontend/src/auth/passwordAuthApi.ts`, with typed results like T021.
  - Create `frontend/src/components/auth/ForgotPasswordForm.tsx`: an Email field and a "Send reset link" button; on 202 it shows "If an account exists for {email}, we've sent a reset link."; there's a "Back to sign in" link-button.
  - In `frontend/src/components/auth/EmailSignInForm.tsx`, add a "Forgot password?" link-button under the password field, calling a new prop `onForgotPassword`.
  - In `frontend/src/components/LoginModal.tsx`, add a `forgot` mode with the title "Reset your password", rendering `ForgotPasswordForm` (the Google button and divider stay hidden in this mode).
- [X] T039 [US5] Create `frontend/src/pages/ResetPasswordPage.tsx`, reading the token from `#token=` and stripping it as in T023:
  - render "Choose a new password", then `PasswordInput` New password (hint "10–128 characters…", `autoComplete="new-password"`) and Confirm new password, then "Change password";
  - check `validateNewPassword` client-side, matching the confirmation;
  - on success show "Your password has been changed. You can now sign in." with a link to `/`;
  - `token_invalid` → "This link is no longer valid. Request a new one." (hide the form);
  - a server field error → under New password.
  
  Add the `/reset-password` route in `frontend/src/App.tsx`. Depends on T038.
- [X] T040 [P] [US5] Add reset tests to `backend/OAuthLearn.Api.Tests/AccountFlowsTests.cs`:
  - forgot for an existing and an unknown email → identical `202` bodies; a mailbox message only for the existing one;
  - reset with a valid token → 204; the old password → 401; the new one → 204;
  - a session cookie obtained before the reset → `/me` 401 afterwards;
  - reusing the token, or using it after `Time.Advance(31 min)` → `token_invalid`;
  - requesting a second link invalidates the first;
  - a common new password → `400 { errors: { newPassword: "password_common" } }` and the token is still usable;
  - the 4th forgot request for one email within an hour → still 202 but no new mailbox message;
  - a Google-only account can set a first password via reset and then sign in with it.
- [X] T041 [P] [US5] Frontend tests:
  - `frontend/src/components/auth/authForms.test.tsx`: ForgotPasswordForm success message and invalid email;
  - `frontend/src/components/LoginModal.test.tsx`: "Forgot password?" opens the reset view, and "Back to sign in" returns;
  - create `frontend/src/pages/ResetPasswordPage.test.tsx`: token read and stripped; mismatch confirmation blocks submit; success message; `token_invalid` hides the form.

**Checkpoint**: Quickstart 11–13 pass.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T042 [P] Create `backend/OAuthLearn.Api.Tests/PasswordPolicyTests.cs` covering every `PasswordPolicy` code and boundary (9/10/128/129 characters, equal-to-email ignoring case and spaces, a common password in different case, a 254/255-character email, a display name of 100/101 characters), plus `CommonPasswords` ignoring `#` header lines
- [X] T043 [P] Add `GET /dev/mailbox` tests to `backend/OAuthLearn.Api.Tests/AccountFlowsTests.cs`:
  - in `Testing` → 404 (the route exists only in Development);
  - a separate factory with `UseEnvironment("Development")` (keep `UseSetting` values so no user secrets are needed) → 200, the CSP header contains `style-src 'unsafe-inline'`, and a message body containing `<script>` is rendered encoded.
- [X] T044 Security pass:
  - run the whole flow in tests with logging captured (add a test `ILoggerProvider` in `TestHelpers.cs` collecting messages) and assert no log line contains the test password, a raw token, or the unknown email used in failed attempts (FR-018, SC-005);
  - grep `frontend/dist` after `npm run build` for `GOCSPX-` (still none).
- [X] T045 [P] Update `README.md`: add "Email & password" to "How the login works" (register → mailbox → verify with password → session; reset flow) and "Session security" (PBKDF2-SHA512 210k, throttling limits, HMAC'd throttle keys, tokens in the URL fragment, the pre-registration hijack and how verification-with-password stops it). Add the `Auth:LookupHashKey` setup step and the `/dev/mailbox` URL
- [X] T046 Update `specs/002-email-password-auth/spec.md` **Status** to say the backend is implemented, and remove the "not available yet" stub note in `frontend/src/auth/passwordAuthApi.ts`. Delete `NotAvailableError` if nothing uses it any more, updating `formMessages.ts` and its tests accordingly
- [X] T047 Run `dotnet build`, `dotnet test OAuthLearn.sln` (with `TEST_PG_CONNECTION`), and `npm test`, `npx tsc -b`, `npm run lint`, `npm run build` in `frontend/`. Fix any failures or warnings
- [ ] T048 Run the 19 manual scenarios in `specs/002-email-password-auth/quickstart.md` and the feature 001 quickstart scenarios 1–6 (regression) against the running app, and note the results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none
- **Foundational (Phase 2)**: after Setup; blocks all stories
- **US1 (Phase 3)**: after Foundational. The MVP: the only way to create a password account
- **US2 (Phase 4)**: after Foundational; its tests use US1's register + verify helper, so in practice after US1
- **US3 (Phase 5)**: after US2 (it hooks into `SignInAsync`) and T019
- **US4 (Phase 6)**: after Foundational + T018/T019 (it needs pending credentials and tokens); independent of US2/US3
- **US5 (Phase 7)**: after Foundational; its tests reuse US1/US2 helpers
- **Polish (Phase 8)**: after the stories

### Key Task Dependencies

- T003, T004 → T005 → T006; T003 → T007
- T010, T011, T012, T014, T015 → T018 → T019 → T020
- T012 → T013
- T018 → T026 → T027; T026 + T019 → T031
- T021 → T022, T023; T028 → T030; T038 → T039
- T016 → all backend test tasks (T024, T029, T032, T035, T040, T043, T044)
- T017 → T021, T028, T038

### Parallel Opportunities

- Foundational: T004, T008, T009, T010, T011, T012, T014, T015, T016, T017 all touch different files
- US1: T021 (frontend) alongside T018–T020 (backend); T024 and T025 together
- US2–US5: each story's frontend task runs alongside its backend tasks; test tasks marked [P] run together
- US4 can run in parallel with US2/US3 once T018/T019 exist

---

## Parallel Example: Foundational

```text
Task: "T010 PasswordHashing in backend/OAuthLearn.Api/Passwords/PasswordHashing.cs"
Task: "T011 CommonPasswords + PasswordPolicy in backend/OAuthLearn.Api/Passwords/"
Task: "T012 IEmailSender + DevMailboxEmailSender + EmailTemplates in backend/OAuthLearn.Api/Email/"
Task: "T014 TokenService in backend/OAuthLearn.Api/Data/TokenService.cs"
Task: "T015 AttemptLimiter in backend/OAuthLearn.Api/Throttling/AttemptLimiter.cs"
Task: "T017 passwordAuthApi base + formMessages in frontend/src/"
```

## Parallel Example: User Story 1

```text
Task: "T018–T020 AccountService.RegisterAsync/VerifyEmailAsync + PasswordEndpoints (backend)"
Task: "T021 register/verifyEmail in frontend/src/auth/passwordAuthApi.ts"
# then
Task: "T024 AccountFlowsTests (register/verify)"
Task: "T025 RegisterForm/LoginModal/VerifyEmailPage tests"
```

---

## Implementation Strategy

### MVP First

1. Phase 1 + Phase 2. Stop and check that the existing 34 backend and 58 frontend tests still pass (T007/T009 change shared code).
2. Phase 3 (US1): register, then `/dev/mailbox`, then verify, and you're signed in. **Validate with quickstart 1–4.**
3. Phase 4 (US2) + Phase 5 (US3) together make password sign-in safe to use, so ship them as one increment.
4. Phase 6 (US4), then Phase 7 (US5), then Polish.

### Notes

- Never log or return passwords, raw tokens, or the email of a failed attempt.
- Keep every new POST behind `RequireFetchHeaderFilter`.
- All time-based logic uses the injected `TimeProvider`, so tests control the clock with `factory.Time.Advance(...)`.
