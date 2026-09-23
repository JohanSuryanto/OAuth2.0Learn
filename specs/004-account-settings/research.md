# Research: Account & Security Settings

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-23

Builds on features 001–003:
- the cookie session (60 min sliding, 8 h cap from `auth_time`, `SessionValidator` checking `users.session_version`);
- `SessionIssuer` (password sign-in and verify) and Google `OnCreatingTicket`;
- `/api/auth/me` with session timing, `/api/account`;
- the React app with `react-router`, `AuthContext`, `LoginDialogProvider`, `loginPopup.ts` and `SessionCountdown`.

---

## R1. Per-device sessions: a `user_sessions` table plus a `sid` claim

- **Decision**:
  - Every sign-in (Google, password, email verification) creates a `user_sessions` row and puts its id in a new cookie claim, `sid`.
  - `SessionValidator` now loads the row together with the user (one query) and rejects the cookie if:
    - the row is missing, ended (`revoked_at IS NOT NULL`) or belongs to another user;
    - the user's `session_version` differs (the existing global kill switch);
    - `auth_time` is past 8 h (unchanged).
- **Revocation per action**:

  | Action | Rows | `session_version` | Current cookie |
  |---|---|---|---|
  | Log out (menu) | current row revoked | unchanged | deleted |
  | Sign out one device | that row revoked | unchanged | — |
  | Sign out all other devices | all rows except current revoked | **+1** | re-issued (R3) |
  | Change password | all rows except current revoked | **+1** | re-issued (R3) |
  | Password reset (002) | all rows revoked | +1 (as today) | — |

  Bumping `session_version` on "all others" and password change also kills **legacy cookies** without a `sid` (R2), which the rows alone can't reach. The current device keeps working because its cookie is re-issued with the new version.
- **`last_seen_at`**: updated by the validator at most once every 5 minutes per session (FR-013 allows ±5 min), which avoids a DB write on every request.
- **"Valid" for listing**: `revoked_at IS NULL AND last_seen_at > now − 60 min AND created_auth_time > now − 8 h`. Rows older than 8 h are purged opportunistically.
- **Alternatives considered**:
  - ASP.NET Core `ITicketStore` (server-side tickets): heavier. It moves the whole ticket into the DB and changes cookie semantics for no extra benefit here.
  - Keeping only `session_version`: can't end a single device.

## R2. Sessions created before this feature (FR-016)

- **Decision**: a legacy cookie (valid, but without a `sid`) is validated by `session_version` as before. On its first request after the upgrade:
  - `SessionValidator` creates a row with `device_label = NULL` (shown as "Unknown device"), `created_at = auth_time` and `last_auth_at = auth_time`;
  - it adds `sid` to the principal via `ReplacePrincipal` and sets `ShouldRenew = true`, so the browser gets an upgraded cookie.
  
  From then on the cookie is an ordinary tracked session.
- **Rationale**: every session shows up in the list without forcing anyone to sign in again.

## R3. Re-issuing the current cookie without starting a new session

- **Decision**: `SessionIssuer.ReissueAsync(ctx, user, sid, authTime)` signs in again with the same `sid` and `auth_time` but the user's current `session_version` and display name. Uses:
  - after a password change or "sign out all other devices" (a new version);
  - after a profile update (a new `Name` claim, so the header and `/me` show the new name immediately; FR-005, SC-003);
  - for "Stay signed in" (a guaranteed new 60 min idle window, R7).
  
  The absolute 8 h limit can't be extended, because `auth_time` is preserved.

## R4. "Recently authenticated" (FR-009, 10-minute rule)

- **Decision**: `user_sessions.last_auth_at` is set at sign-in and updated by any successful re-authentication or password change. It's recent if `now − last_auth_at ≤ 10 min`, using `TimeProvider`.
- It's stored **server-side**, so a client can't forge it (a cookie claim could only be trusted as far as the cookie itself; the DB value is authoritative).
- **Endpoints needing it**: set password. Change password always needs the current password instead (FR-010). If it isn't recent, the endpoint returns `403 { code: "reauth_required", methods: ["password"|"google"] }`.

## R5. Password re-authentication

- **Decision**: `POST /api/account/reauth/password { password }` verifies against the **active** hash only.
  - Throttled with the **same buckets and limits as sign-in** (`signin_fail_email` keyed by the account's normalized email, `signin_fail_ip`), so failures count together with sign-in failures (spec edge case).
  - Success sets `last_auth_at = now` and clears the email failure counter.
  - It always does exactly one hash check, even when the account has no active password (a dummy hash, as in 002 R2).

## R6. Google re-authentication through the existing popup

- **Decision**: a new `GET /api/auth/reauth/google` (requires a signed-in cookie) issues a Google challenge whose `AuthenticationProperties.Items["purpose"] = "reauth"`, with `Items["sid"]` and `Items["uid"]` from the current session (the properties are protected in the state parameter, so they can't be tampered with).
  - **Forcing a fresh login**: `Events.OnRedirectToAuthorizationEndpoint` appends `prompt=select_account&max_age=0` for re-auth challenges. `max_age=0` makes Google ask for the user's credentials again.
  - **`OnCreatingTicket`**, when purpose is `reauth`:
    - if the Google `sub` ≠ the account's `google_subject` → `SignInRejectedException("reauth_mismatch")`;
    - otherwise set that session's `last_auth_at = now`.
  - **`Events.OnTicketReceived`**, when purpose is `reauth`: `HandleResponse()` and redirect to `/api/auth/popup-complete?result=reauth_ok`. This **skips** the sign-in, so the current session and cookie are unchanged.
  - **Popup result codes** gain `reauth_ok` and `reauth_mismatch`, whitelisted in `popup-complete`, `auth-complete.js` and `loginPopup.ts`.
- **Rationale**: it reuses the whole feature 001 popup + BroadcastChannel machinery, and skipping sign-in means re-auth can't swap the user into another account.
- **Why cookies reach `/signin-google`**: Google redirects the popup with a top-level GET, and `SameSite=Lax` cookies are sent on top-level GET navigations. The sid/uid are in the protected state anyway, so the design doesn't depend on that.

## R7. "Stay signed in" (FR-018, FR-019)

- **Decision**: `POST /api/auth/session/extend` (CSRF filter) re-issues the cookie via R3, which gives a new 60 min idle window from now. It returns the new timing.
  - When the absolute limit is less than 2 min away, the endpoint still succeeds, but the UI doesn't offer it (the dialog shows "Sign out"/"OK").
  - Nothing else in the dialog calls the server. Its countdown uses the same `performance.now()` maths as `SessionCountdown` (003 R4).

## R8. Display name vs Google sign-in (conflict found)

- **Problem**: feature 001's `UpsertFromGoogleAsync` overwrites `display_name` with Google's name on **every** Google sign-in, and the Google cookie's `Name` claim comes from Google. A name the user set in settings would silently revert at the next Google sign-in.
- **Decision**:
  - Google's name is used only when creating an account, or when `display_name` is null. Later Google sign-ins keep the user's name.
  - `OnCreatingTicket` replaces the Google `Name` claim with the stored display name, or removes it when null.
- This intentionally changes feature 001 FR-015 ("update … email/name if changed"): the email is still refreshed, the name isn't.

## R9. Device description and approximate address

- **Decision**: a small built-in `UserAgentDescriber`, with no package, that maps `User-Agent` to "{Browser} on {OS}".
  - Browsers: Edge (`Edg/`), Opera (`OPR/`), Chrome, Firefox, Safari (Safari only without Chrome/Chromium).
  - OS: Windows, macOS, iOS (iPhone/iPad), Android, Linux, ChromeOS.
  - Unknown → null, shown as "Unknown device". Stored truncated to 200 characters, and only the description, never the raw header.
- **`IpMasker`**:
  - IPv4 `a.b.c.d` → `a.b.c.x`.
  - IPv6 → the first 4 groups + `:…`.
  - IPv4-mapped IPv6 → treated as IPv4.
  - Null → "Unknown".
  
  Only the masked value is stored (privacy: the full IP isn't needed for this feature).

## R10. Header user menu (WAI-ARIA "menu button")

- **Decision**:
  - The button has `aria-haspopup="menu"`, `aria-expanded` and `aria-controls`.
  - The list is `role="menu"`, with a non-interactive `role="presentation"` email line and three `role="menuitem"` buttons/links.
  - Keys:
    - Enter, Space or ArrowDown opens and focuses the first item; ArrowUp opens and focuses the last;
    - Arrow Up/Down move with wrap-around, Home/End jump to the first/last item;
    - Escape and Tab close it; Escape returns focus to the button.
  - A `mousedown` outside closes it. Focus is managed with refs, not a library.
- **Rationale**: FR-003. It's small enough that a headless-UI dependency isn't justified.

## R11. Frontend structure

- **Decision**:
  - `/settings` is protected with the existing `RequireSignedIn`.
  - `SettingsPage` has four sections: Profile, Password (Change or Set), Sign-in methods (read-only), Active sessions.
  - A `useReauth()` helper runs the re-auth step when an action returns `reauth_required`. It shows an inline "Confirm it's you" panel: a password field, or a "Continue with Google" button that opens the popup via `openLoginPopup` with a re-auth URL. It then retries the action once.
  - `SessionExpiryWarning` is mounted once in the app shell whenever the state is signed in with session timing.

## R12. Testing

- **Backend**, reusing the existing fixture, `FakeTimeProvider`, per-factory client IP and real cookies:
  - sessions lifecycle (create at each sign-in method; list with "This device" first; revoke one; revoke others; logout keeps other devices; reset kills all; legacy cookie upgrade);
  - password change (the current device survives with a re-issued cookie, others get 401, the legacy cookie is killed);
  - set password (403 `reauth_required` after `Time.Advance(11 min)`, allowed within 10 min, allowed after password re-auth);
  - re-auth throttling shared with sign-in;
  - profile update (`/me` name changes at once; the 100-character limit; whitespace clears it);
  - extend (a new idle window, the absolute limit unchanged);
  - the Google upsert no longer overwriting a custom name;
  - Google re-auth logic via `AccountSecurityService.CompleteGoogleReauthAsync(sid, uid, sub)` unit tests (match and mismatch);
  - `UserAgentDescriber` / `IpMasker` tables.
- **Frontend**:
  - UserMenu keyboard and mouse behaviour;
  - the settings sections, including the re-auth flow with a mocked popup;
  - the sessions list actions;
  - SessionExpiryWarning (appears at 2:00, "Stay signed in" calls extend, the absolute-limit variant, no requests while showing);
  - the popup result codes.
