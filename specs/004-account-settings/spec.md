# Feature Specification: Account & Security Settings

**Feature Branch**: `004-account-settings`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Account & security settings for the OAuth2.0Learn app (builds on 001 Google sign-in, 002 email/password with verification and reset, 003 Home/Dashboard with session countdown). (1) Header user menu: when signed in, replace the plain "Hi {email}" + Logout button with a user menu button showing the user's name (or email) that opens a dropdown containing: the signed-in email, "Dashboard", "Account settings", and "Log out". Must be keyboard accessible (open with Enter/Space/ArrowDown, arrow keys to move, Escape to close, focus returns to the button) and close on outside click. (2) Account settings page at /settings (signed-in only): Profile section where the user can update their display name (email is shown but cannot be changed); Password section: change password (requires current password, new password follows the existing rules, afterwards all OTHER sessions are signed out while the current one stays signed in), or for Google-only accounts "Set a password" (the same rules; proves ownership through a recent Google sign-in / re-authentication); Sign-in methods section: shows Google and Password; allow disconnecting Google only if the account has an active password (never leave an account with no way to sign in). (3) Re-authentication for sensitive actions: changing/setting the password or disconnecting Google requires having signed in within the last 10 minutes; otherwise the user must confirm with their current password (password accounts) or a fresh Google sign-in (Google-only accounts) before the action proceeds. (4) Active sessions: a section listing where the user is signed in (browser/device description, approximate IP, signed-in time, last active time, "This device" marker) with "Sign out" per other session and "Sign out all other devices"; normal Logout signs out only the current device (today logout signs out everywhere — change that), while password change/reset still signs out all others. (5) Session-expiry warning: when either session limit is 2 minutes away while the app is open, show a dialog "You'll be signed out in m:ss" with "Stay signed in" (a deliberate action that renews the inactivity limit; cannot extend past the 8-hour absolute limit — then only "Sign out"/"OK") and "Sign out". Security: all changes require CSRF protection like existing POSTs, rate limit re-authentication attempts like sign-in, log security events without secrets, and never reveal password hashes. Stack unchanged: React + Vite frontend (http://localhost:5174), .NET 9 backend (https://localhost:5001), PostgreSQL."

## Clarifications

### Session 2026-09-23

- Q: Can the user change their email address in Account settings? → A: No. Only the display name is editable; the email is shown read-only.
- Q: What happens when a disconnected Google account signs in again? → A: Question withdrawn. "Disconnect Google" was removed from this feature at the user's request, and the Sign-in methods section is read-only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - User menu in the header (Priority: P1)

A signed-in user sees a menu button in the top-right corner showing their name (or email when no name is set), in place of today's "Hi {email}" text and Logout button. Opening it shows their email and three choices: "Dashboard", "Account settings", and "Log out". It works with a mouse, a keyboard, and a screen reader.

**Why this priority**: It is the entry point to everything else in this feature and the change the user asked for most directly.

**Independent Test**: Sign in, open the menu with a mouse and then with the keyboard only, and use each of the three choices.

**Acceptance Scenarios**:

1. **Given** a signed-in user with a display name, **When** they look at the header, **Then** they see a menu button labelled with their display name (their email when there is none) instead of "Hi {email}" and a Logout button.
2. **Given** the menu is closed, **When** the user clicks the button or presses Enter, Space, or Arrow Down on it, **Then** the menu opens showing the signed-in email (not clickable) and the choices "Dashboard", "Account settings", "Log out", with the first choice focused when opened by keyboard.
3. **Given** the menu is open, **When** the user presses Arrow Down/Up, Home/End, **Then** focus moves between choices (wrapping around); **When** they press Escape or click outside, **Then** it closes and focus returns to the menu button.
4. **Given** the menu is open, **When** the user chooses "Dashboard" or "Account settings", **Then** that page opens and the menu closes; **When** they choose "Log out", **Then** they are logged out on this device and land on Home.
5. **Given** a signed-out visitor, **When** they look at the header, **Then** they see the "Login" button exactly as before.

---

### User Story 2 - Change my display name (Priority: P1)

On the Account settings page, the user sees their email (read-only, with a note that it can't be changed) and can change their display name. After saving, the new name appears in the header menu and on the Dashboard.

**Why this priority**: Explicitly requested, simple, and makes the settings page useful on its own.

**Independent Test**: Open Account settings, change the name, save, and confirm the header and Dashboard show the new name; clear the name and confirm the email is shown instead.

**Acceptance Scenarios**:

1. **Given** the Account settings page, **When** it loads, **Then** the Profile section shows the email as plain, non-editable text with "Your email can't be changed", and the display name in an editable field.
2. **Given** a valid new name (1–100 characters after trimming), **When** the user saves, **Then** a confirmation "Profile updated." appears and the header menu and Dashboard show the new name without signing out or reloading.
3. **Given** the name field is emptied, **When** the user saves, **Then** the display name is removed and the email is shown wherever the name would be.
4. **Given** a name longer than 100 characters, **When** the user saves, **Then** the form explains the limit and nothing changes.

---

### User Story 3 - Change or set my password, confirming it's really me (Priority: P1)

A user with a password can change it by entering their current password and a new one twice; afterwards all their other devices are signed out but this one stays signed in. A user who only has Google sign-in can set a password instead. Sensitive actions require that the user signed in recently (within 10 minutes); otherwise they first confirm their identity with their current password or, for Google-only accounts, a fresh Google sign-in.

**Why this priority**: The main security value of the feature; it lets users recover from a leaked password without the email reset flow.

**Independent Test**: Change the password on one device while signed in on a second; confirm the second is signed out and the first stays signed in, and that the new password works. For a Google-only account, set a password after confirming with Google, then sign in with it.

**Acceptance Scenarios**:

1. **Given** a password account on the settings page, **When** the user enters the correct current password and a valid new password twice, **Then** they see "Password changed. Other devices have been signed out.", stay signed in on this device, and every other session of the account ends.
2. **Given** a wrong current password, **When** they submit, **Then** they see "Your current password is incorrect." and nothing changes; repeated wrong attempts are throttled exactly like sign-in (feature 002).
3. **Given** a new password that breaks the password rules (feature 002: 10–128 characters, not the email, not a common password) or doesn't match its confirmation, **When** they submit, **Then** the specific problem is shown and nothing changes.
4. **Given** a Google-only account that signed in more than 10 minutes ago, **When** the user chooses "Set a password", **Then** they are first asked to confirm with Google; **When** that Google sign-in succeeds for the same Google account, **Then** they can set the password (same rules, entered twice), which then works for sign-in.
5. **Given** a Google-only account that signed in less than 10 minutes ago, **When** the user sets a password, **Then** no extra confirmation is asked.
6. **Given** a confirmation attempt with a different Google account than the one linked, **When** it completes, **Then** it is refused with "That Google account isn't the one linked to this account." and nothing changes.

---

### User Story 4 - See and end my active sessions (Priority: P2)

The settings page lists every device where the user is signed in: a description of the browser and device, an approximate location on the network, when it signed in, when it was last active, and a "This device" marker. The user can sign out any other device, or all other devices at once. Logging out from the menu now ends only the current device's session.

**Why this priority**: Important for noticing and stopping unwanted access, but the rest of the feature works without it.

**Independent Test**: Sign in from two browsers, open the sessions list in one, sign out the other, and confirm the other browser is signed out on its next action while this one stays signed in.

**Acceptance Scenarios**:

1. **Given** a user signed in on two browsers, **When** they open the sessions list on browser A, **Then** it shows two entries, with A marked "This device" and listed first, each with browser/device description, approximate network address, signed-in time and last-active time.
2. **Given** the list, **When** the user signs out browser B's entry, **Then** B's entry disappears and browser B is signed out on its next request (it lands on Home with "Your session has ended").
3. **Given** several other sessions, **When** the user chooses "Sign out all other devices", **Then** only "This device" remains.
4. **Given** a user signed in on two browsers, **When** they choose "Log out" in the menu on A, **Then** A is signed out and B stays signed in.
5. **Given** a password reset by email (feature 002), **When** it completes, **Then** every session, including the current device's, ends, as today.
6. **Given** the current device's own entry, **When** the user looks at it, **Then** it has no "Sign out" button (they use "Log out" instead).

---

### User Story 5 - Warn me before my session ends (Priority: P2)

While any signed-in page is open, when the session will end within 2 minutes the user sees a dialog "You'll be signed out in m:ss" counting down. If the inactivity limit is the cause, they can choose "Stay signed in" (which counts as activity and renews it) or "Sign out". If the 8-hour limit is the cause, it can't be extended, so they see "Sign out" and "OK".

**Why this priority**: Prevents losing work unexpectedly; builds on feature 003's countdown, but isn't required for account management.

**Independent Test**: Let a session reach 2 minutes before the inactivity limit, confirm the dialog appears and "Stay signed in" resets the inactivity countdown; separately, reach 2 minutes before the 8-hour limit and confirm only "Sign out"/"OK" are offered.

**Acceptance Scenarios**:

1. **Given** a signed-in page is open and the inactivity limit is 2 minutes away (and before the 8-hour limit), **When** that moment comes, **Then** a dialog shows "You'll be signed out in 1:59" counting down every second, with "Stay signed in" and "Sign out".
2. **Given** the dialog, **When** the user chooses "Stay signed in", **Then** the dialog closes and the inactivity limit is renewed, as confirmed by the Dashboard countdown.
3. **Given** the 8-hour limit is the one 2 minutes away, **When** the dialog appears, **Then** it says the session can't be extended and offers only "Sign out" and "OK"; "OK" closes it without extending anything.
4. **Given** the dialog is ignored, **When** the countdown reaches zero, **Then** the user lands on Home with "Your session has ended. Please sign in again." (feature 003).
5. **Given** the dialog, **When** the user chooses "Sign out", **Then** they are logged out on this device and land on Home.
6. **Given** the dialog is shown, **When** nothing is clicked, **Then** the app itself makes no request that extends the session (only the user's choice does).

### Edge Cases

- The user opens Account settings in two tabs and changes the name in one → the other shows the old name until it reloads or regains focus; saving from the stale tab simply overwrites with its value (last write wins).
- Re-authentication with the password fails 5 times in 15 minutes → further attempts are refused with "Too many attempts. Please try again later." (same limits as sign-in, counted together with sign-in failures for this account).
- The user's session ends (expiry, sign-out from another device) while they are filling a settings form → submitting takes them to Home with "Your session has ended" and nothing is changed.
- The session-expiry dialog is open in two tabs and the user chooses "Stay signed in" in one → the other tab's dialog closes the next time that tab refreshes its session information (on focus or when its countdown ends) because the session is no longer about to end.
- A user signed in with Google changes nothing but opens "Set a password" 11 minutes after signing in → asked to confirm with Google; if the popup is blocked or cancelled, they stay on the page with the relevant message from feature 001 and nothing changes.
- The sessions list shows a session that ended moments ago (e.g., expired) → it is not listed; only sessions that are still valid are shown.
- A session from before this feature (issued before per-device tracking existed) → it keeps working until it expires, and appears in the list with "Unknown device".
- Password change while the account also has Google → Google stays connected; only sessions other than the current one end.
- Display name made only of spaces → treated as empty (name removed).
- Menu open when the user becomes signed out (e.g., expiry) → the menu disappears along with the signed-in header.

## Requirements *(mandatory)*

### Functional Requirements

**Header user menu**

- **FR-001**: When signed in, the header MUST show a menu button labelled with the user's display name, or their email when no display name is set, instead of "Hi {email}" and the Logout button.
- **FR-002**: The menu MUST show the signed-in email (non-interactive) and the choices "Dashboard", "Account settings", and "Log out".
- **FR-003**: The menu MUST be operable by keyboard (Enter, Space, or Arrow Down to open; Arrow Up/Down, Home/End to move; Escape to close), MUST close on a choice or a click outside, MUST return focus to the button on close, and MUST expose its open/closed state and items to assistive technology.

**Account settings page**

- **FR-004**: The app MUST provide an Account settings page at `/settings`, available only to signed-in users (signed-out visitors are sent to Home, as with the Dashboard in feature 003).
- **FR-005**: The Profile section MUST show the email as read-only with the note "Your email can't be changed", and MUST let the user change the display name: trimmed, at most 100 characters; empty or whitespace-only removes it. Changes MUST appear in the header and Dashboard without reloading.

**Passwords and re-authentication**

- **FR-006**: A user with an active password MUST be able to change it by giving the current password and a new password twice; the new password MUST follow feature 002's rules (FR-005 of feature 002).
- **FR-007**: After a successful password change, every session of the account except the current one MUST end, and the current device MUST stay signed in.
- **FR-008**: A user without an active password (e.g., Google-only) MUST be able to set one (same rules, entered twice). A password still waiting for email verification (feature 002) MUST be replaced by the one set here, which becomes active immediately.
- **FR-009**: Setting a password MUST require that the user authenticated within the last 10 minutes (signing in or re-authenticating both count). Otherwise the user MUST first re-authenticate: with their current password if the account has an active password, or with a fresh Google sign-in for the same linked Google account if it doesn't.
- **FR-010**: Changing the password MUST always require the current password, regardless of how recently the user signed in.
- **FR-011**: Wrong current-password or re-authentication attempts MUST be throttled with the same limits as sign-in (feature 002, FR-011) and show "Too many attempts. Please try again later." when refused.
- **FR-012**: A Google re-authentication with a Google account other than the one linked MUST be refused with "That Google account isn't the one linked to this account." and change nothing.

**Active sessions**

- **FR-013**: The settings page MUST list the account's currently valid sessions with: a browser and operating-system description ("Unknown device" when it can't be determined), an approximate network address (last part hidden, e.g. `203.0.113.x`), signed-in time, last-active time (accurate to within 5 minutes), and a "This device" marker on the current session, which is listed first.
- **FR-014**: The user MUST be able to end any other session individually and all other sessions at once; an ended session MUST stop working on its next request, and the device then lands on Home with "Your session has ended" (feature 003).
- **FR-015**: "Log out" MUST end only the current device's session. This replaces feature 001's "log out everywhere" behaviour. Password reset by email (feature 002) MUST still end every session.
- **FR-016**: Existing sessions from before this feature MUST keep working until they expire and MUST appear in the list as "Unknown device".

**Session-expiry warning**

- **FR-017**: On any signed-in page, when either session limit is 2 minutes or less away, the app MUST show a dialog "You'll be signed out in m:ss" counting down every second.
- **FR-018**: If the inactivity limit ends first, the dialog MUST offer "Stay signed in" (renews the inactivity limit, never past the 8-hour limit) and "Sign out". If the 8-hour limit ends first, it MUST state the session can't be extended and offer "Sign out" and "OK".
- **FR-019**: Showing or counting down the dialog MUST NOT extend the session; only choosing "Stay signed in" may. When it reaches zero, feature 003's session-ended behaviour applies.

**Sign-in methods**

- **FR-020**: The Sign-in methods section MUST show, read-only, whether Google and a password are connected, following feature 003's rules (a password waiting for verification counts as not connected). Disconnecting Google is out of scope.
**Security**

- **FR-021**: All changes (name, password, sessions, "Stay signed in") MUST have the same cross-site request protection as existing actions and MUST only affect the signed-in user's own account.
- **FR-022**: The system MUST record security events (name change, password change, password set, re-authentication success/failure, session ended by the user) without recording passwords or other secrets; password hashes MUST never be returned.

### Key Entities

- **User**: Existing account (features 001/002). Display name becomes editable; nothing else about identity changes.
- **Session**: One signed-in device. Attributes: which user, browser/device description, approximate network address, signed-in time, last-active time, last authentication time (for the 10-minute rule), and whether it has been ended. Replaces "sessions exist only as cookies"; the existing 60-minute inactivity and 8-hour limits still apply.
- **Security Event**: A record of a sensitive action (type, user, session, time) without secrets; used for logs in this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user can reach Account settings from any page in 2 clicks (or 3 key presses) via the header menu.
- **SC-002**: 100% of menu interactions listed in FR-003 work with the keyboard alone.
- **SC-003**: A display-name change appears in the header and Dashboard within 1 second of saving, without signing out.
- **SC-004**: After a password change, 100% of the account's other sessions are refused on their next request, and the current device stays signed in.
- **SC-005**: 100% of attempts to set a password more than 10 minutes since the last authentication are blocked until the user re-authenticates.
- **SC-006**: After "Sign out" on another device's session or "Sign out all other devices", those devices are signed out on their next request in 100% of cases; "Log out" on one device leaves the others signed in.
- **SC-007**: The expiry warning appears within 1 second of the 2-minute mark, and "Stay signed in" restores the full inactivity window in 100% of attempts before the 8-hour limit.
- **SC-008**: All feature 001–003 checks still pass, except "logout signs out every device", which is intentionally replaced by FR-015.

## Assumptions

- Same environment as features 001–003: local development, React + Vite at http://localhost:5174, .NET backend at https://localhost:5001, existing PostgreSQL database.
- "Signed-in time" for a session is when it was created by a sign-in (Google, password, or email verification); re-authentication updates only the "last authentication" time used for the 10-minute rule.
- Browser/device descriptions are best-effort from what the browser reports (e.g., "Chrome on Windows"); locally every session shows the same network address.
- The email address itself can't be changed in this feature (explicitly requested); changing it would need its own verification flow.
- Security events are written to the application log in this feature; showing them to users ("recent activity") is out of scope.
- Disconnecting (or re-connecting) Google is out of scope, so an account can't lose its sign-in methods through this feature. Two-factor authentication, security notification emails, and deleting the account are also out of scope (candidates for later features).
- Sessions that ended or expired are removed from the list; keeping a history of past sessions is out of scope.
