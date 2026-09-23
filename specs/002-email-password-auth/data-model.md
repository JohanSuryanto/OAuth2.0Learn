# Data Model: Email & Password Registration and Sign-in

**Feature**: [spec.md](./spec.md) | **Date**: 2026-09-23 | Extends [001 data model](../001-google-oauth-login/data-model.md)

All tables use snake_case (EFCore.NamingConventions). Times are `timestamptz` in UTC from the injected `TimeProvider`. A single migration, `EmailPasswordAuth`, makes every change.

## `users` (changed)

| Column | Type | Change | Notes |
|--------|------|--------|-------|
| `id` | `uuid` PK | — | |
| `google_subject` | `text` | **now NULL**, still UNIQUE | NULL for password-only accounts; Postgres allows multiple NULLs in a unique index |
| `email` | `text` NOT NULL | — | as entered or as given by Google (displayed) |
| `email_normalized` | `text` NOT NULL | **new**, **UNIQUE** | `lower(trim(email))`; migration back-fills existing rows |
| `email_verified_at` | `timestamptz` NULL | **new** | migration sets it to `created_at` for existing (Google) rows; NULL means unverified |
| `created_via` | `text` NOT NULL | **new**, default `'google'` | `'google'` or `'password'` |
| `display_name` | `text` NULL | — | at most 100 characters (FR-003) |
| `created_at`, `last_login_at` | `timestamptz` NOT NULL | — | `last_login_at` also set on password sign-in |
| `session_version` | `integer` NOT NULL | — | incremented on logout, password reset, and Google linking over a pending or unverified account |

**Rules**
- An account is **active** when `email_verified_at IS NOT NULL`. A password-created account stays inactive until verified.
- Lookups by email always use `email_normalized`, never `email`.

## `password_credentials` (new, 1:1 with users)

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `user_id` | `uuid` | PK, FK → `users.id` ON DELETE CASCADE | at most one credential per user |
| `active_hash` | `text` | NULL | PasswordHasher V3 string; usable for sign-in |
| `pending_hash` | `text` | NULL | set by registration; activated by verification; never usable for sign-in on its own |
| `active_set_at` | `timestamptz` | NULL | |
| `pending_set_at` | `timestamptz` | NULL | |

**State transitions** (A = active hash, P = pending hash)

```text
(none) ──register──────────────▶ P
P      ──register again─────────▶ P' (replaced; older verify links invalid)
P      ──verify(token, pw==P)───▶ A=P, P=null, email verified, signed in
P      ──Google links account───▶ row deleted (P discarded), session_version++
A      ──register (same email)──▶ unchanged (notice email only)
A(+P?) ──reset password─────────▶ A=new, P=null, session_version++, email verified
(none) ──reset password─────────▶ A=new (first password for a Google-only account)
```

A row with both hashes NULL is deleted. `A` and `P` can coexist only in a state that can't arise, because registration on an account with `A` doesn't create `P`.

## `one_time_tokens` (new)

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `id` | `uuid` | PK | |
| `user_id` | `uuid` | FK → `users.id` ON DELETE CASCADE, indexed | |
| `purpose` | `text` | NOT NULL | `'verify_email'` or `'reset_password'` |
| `token_hash` | `bytea` | NOT NULL, **UNIQUE** | SHA-256 of the raw token; the raw token exists only in the email |
| `created_at` | `timestamptz` | NOT NULL | |
| `expires_at` | `timestamptz` | NOT NULL | verify: +24 h; reset: +30 min |
| `used_at` | `timestamptz` | NULL | set when consumed |

**Rules**
- Creating a token deletes other **unused** tokens for the same `(user_id, purpose)`, so at most one is active.
- A token is valid when `used_at IS NULL AND expires_at > now`.
- A successful reset deletes all the user's tokens.
- A new registration on a pending account deletes its unused `verify_email` token and creates a new one.

## `rate_limit_events` (new)

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `id` | `bigint` | PK, identity | |
| `bucket` | `text` | NOT NULL | `signin_fail_email`, `signin_fail_ip`, `register_ip`, `reset_email`, `reset_ip` |
| `key_hash` | `bytea` | NOT NULL | `HMAC-SHA256(Auth:LookupHashKey, bucket + ":" + value)`; no plain emails or IPs |
| `occurred_at` | `timestamptz` | NOT NULL | |

Index: `(bucket, key_hash, occurred_at DESC)`. Rows older than 24 h are purged opportunistically. The rules are in [research R7](./research.md#r7-throttling-database-backed-event-counts).

## `mailbox_messages` (new, development/testing only)

| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` PK | |
| `to_address` | `text` NOT NULL | |
| `subject` | `text` NOT NULL | |
| `body_text` | `text` NOT NULL | plain text containing the link |
| `created_at` | `timestamptz` NOT NULL | |

It's written only by `DevMailboxEmailSender`, and the app doesn't start outside Development or Testing without a real sender.

## Session claims (unchanged set, new issuer)

Password sign-in and verification issue the same claims as Google sign-in (see the [001 data model](../001-google-oauth-login/data-model.md#session-auth-cookie)): `NameIdentifier` (the user id for password-only accounts, the Google sub otherwise), `Email`, `Name`, `app_user_id`, `session_version`, `auth_time`.

## Frontend state additions

```ts
// Result types returned by passwordAuthApi (replacing the stub)
type SignInResult = { ok: true } | { ok: false; reason: 'invalid_credentials' | 'email_not_verified' | 'too_many_attempts' }
type RegisterResult = { ok: true } | { ok: false; reason: 'too_many_attempts' } | { ok: false; reason: 'validation'; errors: FieldErrors }
```
