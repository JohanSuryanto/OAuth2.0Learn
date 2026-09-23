# Data Model: Account & Security Settings

**Feature**: [spec.md](./spec.md) | **Date**: 2026-09-23

One migration, `UserSessions`: one new table and no changes to existing columns.

## `user_sessions` (new)

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `id` | `uuid` | PK | also the cookie's `sid` claim |
| `user_id` | `uuid` | NOT NULL, FK → `users.id` ON DELETE CASCADE, indexed | |
| `created_at` | `timestamptz` | NOT NULL | "Signed in" time |
| `auth_time` | `timestamptz` | NOT NULL | same as the cookie's `auth_time`; used for the 8 h filter |
| `last_auth_at` | `timestamptz` | NOT NULL | sign-in or latest re-authentication / password change (10-minute rule, research R4) |
| `last_seen_at` | `timestamptz` | NOT NULL | refreshed at most every 5 min by the validator |
| `device_label` | `text` | NULL, ≤ 200 chars | "Chrome on Windows"; NULL means "Unknown device" |
| `ip_masked` | `text` | NULL | "203.0.113.x"; never the full address |
| `revoked_at` | `timestamptz` | NULL | set when ended (logout, sign-out, password change, reset) |

**Rules**
- **Valid (listed and accepted)**: `revoked_at IS NULL`, `last_seen_at > now − 60 min` (listing only; the cookie's own idle expiry is authoritative), and `auth_time > now − 8 h`.
- **Accepted by the validator**: the row exists, `user_id` matches the `app_user_id` claim, `revoked_at IS NULL`, and `users.session_version` equals the cookie's claim.
- **Purge**: rows with `auth_time < now − 8 h` are deleted opportunistically (1 in 100 validator writes).

**Lifecycle**

```text
sign-in (Google / password / verify) ──▶ row created, cookie gets sid
legacy cookie (no sid) first request ──▶ row created (device_label NULL), cookie upgraded (R2)
request ────────────────────────────────▶ last_seen_at bumped if > 5 min old
re-auth / password change ─────────────▶ last_auth_at = now
log out / sign out one ─────────────────▶ revoked_at = now
sign out all others / password change ─▶ others revoked_at = now; session_version++; current cookie re-issued
password reset (002) ───────────────────▶ all revoked_at = now; session_version++
```

## `users` (behaviour change only)

- `display_name`: editable via settings. Trimmed, **at most 100 characters**; empty or whitespace becomes NULL (FR-005). The Google upsert no longer overwrites a non-null value (research R8).

## Cookie claims

| Claim | Change |
|-------|--------|
| `sid` | **new**: the `user_sessions.id` |
| `Name` | now always the stored display name (removed when NULL), including for Google sign-ins |
| others | unchanged (`NameIdentifier`, `Email`, `app_user_id`, `session_version`, `auth_time`) |

## Frontend types

```ts
type SessionInfo = {
  id: string
  device: string | null        // null → "Unknown device"
  ipMasked: string | null      // null → "Unknown"
  createdAt: string            // ISO
  lastSeenAt: string           // ISO
  current: boolean             // "This device"
}

type ReauthRequired = { reason: 'reauth_required'; methods: ('password' | 'google')[] }
```
