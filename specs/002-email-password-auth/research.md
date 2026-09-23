# Research: Email & Password Registration and Sign-in

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-23

Builds on feature 001: `backend/OAuthLearn.Api` (ASP.NET Core 9, cookie session, `SessionValidator`, `UserService`, EF Core + Npgsql) and `frontend/` (React + Vite, `LoginModal` with the email forms already built against a stubbed `passwordAuthApi.ts`).

---

## R1. Password hashing: ASP.NET Core `PasswordHasher<T>`, PBKDF2-HMAC-SHA512, 210,000 iterations

- **Decision**: Use `Microsoft.AspNetCore.Identity.PasswordHasher<User>` on its own, with `PasswordHasherOptions.IterationCount = 210_000`. The rest of ASP.NET Core Identity (UserManager, EF stores) is not used.
- **How it works**: The V3 format stores version, PRF (HMAC-SHA512), iteration count, a 16-byte random salt and the subkey in one Base64 string. `VerifyHashedPassword` returns `SuccessRehashNeeded` when the stored hash used fewer iterations; the app then re-hashes transparently on the next successful sign-in.
- **Rationale**:
  - It ships with the ASP.NET Core shared framework, so there's no new dependency.
  - It's constant-time, salted and versioned.
  - 210k iterations of PBKDF2-HMAC-SHA512 is the current OWASP Password Storage Cheat Sheet figure; .NET's default of 100k is below it.
  - Using the hasher alone keeps our own `users` schema from feature 001.
- **Alternatives considered**:
  - Argon2id (OWASP's first choice): needs a third-party package (e.g., Konscious/Isopoh) and memory tuning.
  - bcrypt (BCrypt.Net-Next): limited to 72 bytes, but our passwords go up to 128 characters.
  - Full ASP.NET Core Identity: it would take over the users table and add a lot of unused surface.

## R2. Constant-ish response time for unknown emails

- **Decision**: Every sign-in attempt runs exactly **two** hash verifications:
  - against the active credential, or a fixed dummy hash if there is none;
  - against the pending credential, or the dummy hash.
  
  Registration always hashes the submitted password before branching on whether the email exists.
- **Rationale**: FR-010 and SC-003 require that response time doesn't reveal whether an email or a pending registration exists. Hash cost dominates the response time, so a fixed number of hashes per request equalizes it. Cost is about 2 × 100 ms per sign-in, which is fine for this app.

## R3. Common-password list: bundled top-10k list, embedded resource

- **Decision**: Embed `Passwords/common-passwords.txt` as an `EmbeddedResource`. It is the SecLists `10k-most-common.txt` list (MIT licence, attribution in the file header). It's loaded once into a `HashSet<string>` (ordinal, lower-cased) at startup. Server-side only; the UI shows the server's error.
- **Rationale**: FR-005 asks for "not a commonly used password". A static list needs no network access, which matches the spec assumption.
- **Alternatives considered**: HaveIBeenPwned k-anonymity range API (better coverage, but an external call); zxcvbn strength estimation (a larger dependency, and it's a different policy).

## R4. Verification: token plus password confirmation, and signing in on success

- **Problem found while designing**: as written, the spec edge case "registering again with the same unverified email → fresh link; the password is not changed" allows **pre-registration hijack**:
  1. An attacker registers `victim@x` with the attacker's password.
  2. The victim registers and gets a fresh link, but the attacker's password is still the pending one.
  3. The victim clicks the link, which activates the attacker's password, and the attacker can sign in. That violates SC-007.
- **Decision**:
  - Each registration **replaces** the pending password (last registration wins) and invalidates earlier verification links.
  - The verification page asks for the password chosen at registration. The token and a password matching the **current pending hash** are required together to activate it. On success the password becomes active, the email is marked verified, and the user is **signed in** (a new session is issued).
  - An attacker can't complete verification because they never see the link. A victim who clicks an attacker-triggered link fails because their password doesn't match the attacker's pending one. They then simply register again, which replaces the attacker's pending password.
  - Verification is `POST /api/auth/verify-email` with `{ token, password }` from a frontend page, **never a GET that consumes the token**. Mail scanners and link prefetchers often open links automatically, and a GET would use up the token.
- **Spec impact**: US1 scenario 3, FR-012 and the "registering again" edge case are updated to match. The flow gets safer and shorter: the user is signed in right after verifying.
- **Alternatives considered**:
  - Link-only activation: vulnerable, as described above.
  - Keeping every registration's token with its own password: the victim may still click the attacker's email.
  - Refusing re-registration while one is pending: an attacker could block a real user for 24 hours.

## R5. One-time tokens

- **Decision**:
  - 32 bytes from `RandomNumberGenerator`, Base64Url-encoded (43 characters) in the link.
  - Only `SHA-256(token)` is stored.
  - Table `one_time_tokens` has a `purpose` of `verify_email` or `reset_password`.
  - Creating a token deletes any other unused token for the same user and purpose (at most one active).
  - Using a token sets `used_at`.
  - Expiry: 24 h for verification, 30 min for reset.
  - A successful password reset deletes **all** the user's tokens.
- **Rationale**: High-entropy tokens don't need a slow hash, and a SHA-256 lookup is O(1) by unique index. Storing only the hash means a database leak doesn't expose usable links.
- **Links**: `http://localhost:5174/verify-email#token=…` and `…/reset-password#token=…`. The token goes in the URL **fragment**, which browsers never send to servers or proxies and which is excluded from `Referer`. The page reads it and then removes it with `history.replaceState`.

## R6. Local development mailbox

- **Decision**:
  - An `IEmailSender` interface.
  - The `DevMailboxEmailSender` implementation stores each message in a `mailbox_messages` table.
  - A **Development-only** HTML page at `https://localhost:5001/dev/mailbox` lists the latest 50 messages, with clickable links.
  - Outside Development and Testing, no sender is registered and the app refuses to start ("configure an email sender"), so the dev mailbox can't accidentally run anywhere else.
- **Rationale**:
  - Docker isn't running on this machine, so Mailpit/MailHog aren't practical.
  - A database table makes tests trivial: the test reads the last message to get the link.
  - One interface means a real SMTP sender later is a single new class.
- **CSP**: The backend sends `default-src 'none'` everywhere. The mailbox page overrides this for its own response to `default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'`: no scripts, just static HTML plus inline CSS.
- **Alternatives considered**: `.eml` files in a folder (opening them on Windows launches a mail client); logging links to the console (easy to miss, and it puts secrets in logs, against FR-018's spirit).

## R7. Throttling: database-backed event counts

- **Decision**: Table `rate_limit_events(id, bucket, key_hash, occurred_at)`.

  | Bucket | Key | Rule |
  |---|---|---|
  | `signin_fail_email` | normalized email | blocked when the 5 most recent failures all fall within 15 min of each other **and** the newest is < 15 min old |
  | `signin_fail_ip` | client IP | same with 20 |
  | `register_ip` | client IP | max 5 per rolling hour |
  | `reset_email` | normalized email | max 3 per rolling hour |
  | `reset_ip` | client IP | max 10 per rolling hour |

  - Refused attempts are not recorded, which is what "until 15 minutes after the last counted failure" means.
  - A successful sign-in or password reset deletes that email's `signin_fail_email` rows.
  - Rows older than 24 h are purged opportunistically (1 in 100 writes).
- **Key hashing**: `key_hash = HMAC-SHA256(Auth:LookupHashKey, bucket + ":" + value)`. Emails and IPs are never stored in plain form (FR-018), and unknown emails can't be read back. `Auth:LookupHashKey` is a new **required secret** (user-secrets) and acts as a "pepper".
- **Client IP**: `HttpContext.Connection.RemoteIpAddress`. `X-Forwarded-For` isn't trusted, because the Vite proxy is local and the header is spoofable. Locally all traffic comes from loopback, which the spec assumption already notes.
- **Rationale**: Only *failures* count (not all requests), per-email keys come from the JSON body, the counts survive restarts, and the time comes from `TimeProvider` so tests can control it. The built-in `Microsoft.AspNetCore.RateLimiting` middleware counts requests per partition in memory and can't express "5 failures per email".

## R8. Account linking and changes to the Google upsert

- **Decision**: `UserService.UpsertFromGoogleAsync` becomes:
  1. Find by `google_subject`, then update the email and name (feature 001 behaviour). If the new normalized email belongs to **another** user, throw `SignInRejectedException("email_conflict")`, which becomes `signin_failed`.
  2. Otherwise find by `email_normalized`. If found:
     - attach `google_subject`;
     - set `email_verified_at` if it's null;
     - delete a **pending** credential (FR-014);
     - if the account was unverified or had a pending password, increment `session_version`, which ends any sessions.
  3. Otherwise insert a new user with `created_via = 'google'` and `email_verified_at = now`.
- **Rationale**: This matches the FR-014 rules. Google already requires `email_verified` (feature 001, FR-018), so a Google sign-in proves ownership of the email.

## R9. Password session issuance

- **Decision**:
  - A new `SessionIssuer.SignInAsync(HttpContext, User)` builds the same claim set as the Google path: `NameIdentifier`, `Email`, `Name`, `app_user_id`, `session_version`, `auth_time`.
  - It calls `HttpContext.SignInAsync(Cookie, principal, new AuthenticationProperties { IsPersistent = false })`, which overwrites any existing cookie with a new ticket (FR-009).
  - The cookie options, `SessionValidator`, `/me` and logout are reused unchanged, so SC-006 holds automatically.
  - The Google `OnCreatingTicket` is refactored to share the claim builder (`AuthClaims.ForUser`).

## R10. CSRF protection for the new POST endpoints

- **Decision**: The logout check (`X-Requested-With: fetch`, else 400) moves into a reusable endpoint filter, `RequireFetchHeaderFilter`, applied to logout and every new POST. CORS already allows only `http://localhost:5174` and only that header.
- **Rationale**: FR-017. The same header also blocks **login CSRF**, where an attacker's page silently signs the victim into the attacker's account, because a cross-site form can't set custom headers.

## R11. Frontend pages without a router library

- **Decision**:
  - `App.tsx` switches on `window.location.pathname`: `/verify-email` renders `VerifyEmailPage`, `/reset-password` renders `ResetPasswordPage`, anything else renders the dashboard.
  - Vite's dev server already falls back to `index.html` for unknown paths.
  - Each page reads `#token=` once, removes it from the URL, and after success links back to `/`.
  - `passwordAuthApi.ts` becomes real `fetch` calls with `credentials: 'include'` and `X-Requested-With: fetch`. It maps response codes to typed results that the existing forms already understand.
- **Rationale**: Two extra pages don't justify adding react-router.

## R12. Server-side validation

- **Decision**:
  - **Email**: at most 254 characters and parsed by `System.Net.Mail.MailAddress.TryCreate`. The parsed `Address` must equal the trimmed input, and the domain must contain a dot.
  - **Password**: 10–128 characters (`string.Length`, same as the UI); not equal to the normalized email; not in the common list.
  - **Display name**: at most 100 characters after trimming.
  - Errors come back as `400 { "errors": { "<field>": "<code>" } }`. Codes: `email_invalid`, `password_too_short`, `password_too_long`, `password_is_email`, `password_common`, `display_name_too_long`, `token_invalid`.
- **Rationale**: The server is the source of truth, and the UI already has matching messages for all but `password_common` and `token_invalid`.

## R13. Testing

- **Backend**: extends the feature 001 xUnit suite, which uses a real Postgres through the `TEST_PG_CONNECTION` or Testcontainers fixture.
  - `TestAppFactory` also swaps `TimeProvider` for a `FakeTimeProvider`, sets `Auth:LookupHashKey`, and runs as `Testing` with the dev mailbox registered.
  - Registration, verification and reset tests read links from `mailbox_messages`.
  - Linking tests call `UserService.UpsertFromGoogleAsync` directly, since the real Google flow can't be automated.
- **Frontend**: Vitest, covering the real `passwordAuthApi` (with `fetch` mocked), the new forms and pages, and the modal flows.
