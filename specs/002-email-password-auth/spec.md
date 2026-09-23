# Feature Specification: Email & Password Registration and Sign-in

**Feature Branch**: `002-email-password-auth`

**Created**: 2026-09-23

**Status**: Implemented (2026-09-23). The UI was built first at the user's request; the backend, verify/reset pages and "Forgot password?" followed via `/speckit-plan` → `/speckit-tasks` → `/speckit-implement` (see tasks.md). Decisions for FR-012 / FR-014 / FR-016 are in the Clarifications session below; planning refined FR-012 (research R4).

**Input**: User description: "Add email + password registration and login to the existing OAuth2.0Learn app (feature 001-google-oauth-login already provides Google sign-in via popup, an HttpOnly cookie session, a users table in PostgreSQL keyed by Google sub, session_version revocation, 60 min idle / 8 h absolute session limits, and a sign-in dialog with "Continue with Google" that was built to host more methods). In the same sign-in dialog, users should be able to (1) register a new account with email and password (and optionally a display name), and (2) sign in with email and password. After a successful email/password login or registration, the app must behave exactly like after Google login: header shows "Hi {email}" left of the Logout button, the session survives refresh, logout works the same ("log out everywhere"), and the same session limits and security headers apply. Stack stays React + Vite frontend (http://localhost:5174) and .NET 9 C# backend (https://localhost:5001) with PostgreSQL. Must stay secure: passwords hashed with a strong adaptive algorithm, never logged or returned; protection against brute force / credential stuffing; generic error messages that do not reveal whether an email is registered; CSRF protection consistent with the existing logout endpoint. Consider how an email that already exists from Google sign-in interacts with password registration (account linking), whether email verification is required, and password reset."

## Clarifications

### Session 2026-09-23

- Q: Should a new email/password account have to prove it owns its email address, by opening a link sent to that address, before it can be used? → A: Yes. The verification link is delivered to a local development mailbox (saved on the developer's machine or shown in a local viewer; nothing is really sent). The account works only after the link is opened.
- Q: When the same email address is used for both Google sign-in and an email/password account, should they become one account, and under what rule? → A: One account, linked automatically once email ownership is proven: Google sign-in with a Google-verified email joins the existing account for that email (discarding any never-verified password), and a verified password registration on a Google account's email adds a password to that account.
- Q: Should users who forget their password be able to reset it with a "Forgot password?" link, as part of this feature? → A: Yes. "Forgot password?" always answers "If an account exists, we've sent a reset link."; a single-use link valid for 30 minutes goes to the local mailbox; after setting a new password, the old password and all existing sessions stop working.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create an account with email and password (Priority: P1)

A visitor opens the sign-in dialog, switches to "Create account", enters their email, a password (entered twice), and optionally a display name, and submits. The dialog tells them to check their inbox. They open the verification link from the local development mailbox and confirm the password they chose, which activates the account and signs them in, showing "Hi {email}" and "Logout" in the header, exactly as after Google sign-in.

**Why this priority**: Without registration there is no password account to sign in with; it is the entry point for every non-Google user.

**Independent Test**: With an email that has never been used, register through the dialog, open the verification link from the local mailbox, sign in, and confirm the account works (greeting shown, session survives reload, one user record exists).

**Acceptance Scenarios**:

1. **Given** the sign-in dialog is open, **When** the visitor chooses "Create account", **Then** the dialog shows email, password, confirm-password, and optional display-name fields, and "Continue with Google" remains available.
2. **Given** valid, unused details, **When** the visitor submits, **Then** the dialog shows "Check your inbox — we've sent a verification link to {email}." and a verification message for that address appears in the local development mailbox.
3. **Given** a verification link that has not been used and is less than 24 hours old, **When** the visitor opens it and enters the password they chose at registration, **Then** the account is activated, they are signed in, and the page confirms "Your email is verified. You're signed in." with a way back to the dashboard, where they see "Hi {email}" and "Logout". **When** they enter a different password, **Then** nothing is activated and the page says "That's not the password you chose when registering.".
4. **Given** an account that has not been verified yet, **When** the visitor signs in with the correct password, **Then** they stay signed out and see "Please verify your email first. We've sent you a new link.", and a fresh link is delivered (earlier links stop working).
5. **Given** a password that breaks the password rules (FR-005), **When** the visitor submits, **Then** the form explains which rule failed and no account is created.
6. **Given** the two password entries differ, **When** the visitor submits, **Then** the form says they don't match and no account is created.
7. **Given** an email that already belongs to an account, **When** someone submits the registration form with it, **Then** the dialog shows the same "Check your inbox" message as for a new email, no account or password changes, and the mailbox receives a notice for that address saying someone tried to register with it (with a hint to sign in instead).

---

### User Story 2 - Sign in with email and password (Priority: P1)

A returning user opens the sign-in dialog, enters their email and password, and signs in. The header shows "Hi {email}" and "Logout"; the session behaves exactly like a Google session (survives reload, 60-minute idle and 8-hour absolute limits, "log out everywhere" on logout).

**Why this priority**: This is the core value of the feature; registration is pointless without it.

**Independent Test**: Using an existing password account, sign in, reload, log out, and reload again; confirm the same behaviour as feature 001's Google session.

**Acceptance Scenarios**:

1. **Given** a registered, active account, **When** the user enters the correct email and password, **Then** the dialog closes and the header shows "Hi {email}" and "Logout".
2. **Given** a wrong password, an unknown email, or an account that has no password, **When** the user submits, **Then** the dialog shows the same message "Email or password is incorrect." and the user stays signed out.
3. **Given** a user signed in with a password, **When** they reload the page, **Then** they remain signed in; **When** they click Logout, **Then** all their sessions end, as in feature 001.
4. **Given** the email is typed with different upper/lower case or surrounding spaces, **When** the user signs in, **Then** it matches the same account.

---

### User Story 3 - Protection against password guessing (Priority: P1)

Someone repeatedly tries passwords for an email address (or many emails from one place). After a small number of failures the system slows down and then temporarily refuses further attempts, without revealing whether the email exists.

**Why this priority**: Password sign-in is only safe to offer with guessing protection; the user explicitly required it.

**Independent Test**: Submit repeated wrong passwords for one email and confirm attempts are refused after the limit, both for a registered and an unregistered email, with identical responses.

**Acceptance Scenarios**:

1. **Given** 5 failed sign-in attempts for the same email within 15 minutes, **When** a 6th attempt is made (even with the correct password), **Then** it is refused with "Too many attempts. Please try again later." until 15 minutes after the last failure.
2. **Given** the same limit is reached for an email that is not registered, **When** another attempt is made, **Then** the response is identical to the registered case.
3. **Given** 20 failed sign-in attempts from the same network address within 15 minutes, **When** another attempt is made for any email, **Then** it is refused with the same "Too many attempts" message.
4. **Given** a user who is temporarily refused, **When** the waiting period ends and they enter the correct password, **Then** they sign in normally.

---

### User Story 4 - Google and password on the same email (Priority: P2)

A person who already signed in with Google tries to register a password with the same email, or a person with a password account later chooses "Continue with Google" with the same email. The system handles this predictably and without letting anyone take over someone else's account.

**Why this priority**: It only affects people who use both methods, but getting it wrong is a security hole (account takeover).

**Independent Test**: Create a Google account, then attempt password registration with the same email (and vice versa), and verify the outcome defined in FR-014 and that no second, conflicting account for that email appears.

**Acceptance Scenarios**:

1. **Given** an existing Google-only account, **When** someone registers a password with that same email, **Then** they see the usual "Check your inbox" message, the Google account keeps working, and the password becomes usable only after the verification link is opened; afterwards both Google and the password sign in to the same account (one greeting, one user record).
2. **Given** a verified password account, **When** its owner signs in with Google using the same Google-verified email, **Then** they reach the same account, and from then on both methods work.
3. **Given** someone registered a password for an email but never verified it, **When** the real owner of that email signs in with Google, **Then** the owner gets the account, the unverified password is discarded and can never be used to sign in, and any open sessions for that account end.
4. **Given** a Google account whose Google email is not verified by Google, **When** it signs in, **Then** it is refused as in feature 001 and nothing is linked.

---

### User Story 5 - Forgotten password (Priority: P3)

A user who forgot their password clicks "Forgot password?" in the sign-in form, enters their email, opens the reset link from the local development mailbox, sets a new password, and signs in with it.

**Why this priority**: Important for real users, but the feature is usable for learning without it.

**Independent Test**: Request a reset for an existing account, open the link from the local mailbox, set a new password, and sign in with it; confirm the old password and all previous sessions stop working.

**Acceptance Scenarios**:

1. **Given** the sign-in form, **When** the user clicks "Forgot password?" and submits their email, **Then** they see "If an account exists for {email}, we've sent a reset link." and a reset message appears in the local mailbox.
2. **Given** an email with no account, **When** a reset is requested, **Then** the same message is shown and no message is delivered.
3. **Given** a reset link less than 30 minutes old and not yet used, **When** the user opens it and enters a valid new password twice, **Then** they see "Your password has been changed. You can now sign in." and can sign in with the new password but not the old one.
4. **Given** a password was reset, **When** anyone uses a session created before the reset, **Then** it is refused.
5. **Given** a reset link that is expired, already used, or replaced by a newer one, **When** it is opened, **Then** a page says "This link is no longer valid. Request a new one." and nothing changes.

---

### Edge Cases

- Registering with an email that already has an account → same "Check your inbox" response as a new email; no second account is created and the existing password is unchanged (FR-012).
- A Google account whose Google email changes to an address that already belongs to a different account → sign-in is refused with "Sign-in failed. Please try again."; neither account is changed.
- Opening an expired, already-used, or replaced verification link → a page says "This link is no longer valid. Sign in to get a new one." and nothing changes.
- Registering again with the same unverified email → the new password replaces the pending one and a fresh verification link replaces the old one. (Keeping the first password would let someone who registered a victim's email first get their own password activated when the victim verifies; see research R4.)
- Email with different case or surrounding spaces → treated as the same email everywhere (registration, sign-in, throttling).
- Very long password (e.g., a 128-character passphrase) → accepted; passwords longer than 128 characters are rejected with a clear message.
- Password equal to the email address, or on a list of very common passwords → rejected at registration with a clear message.
- User submits the sign-in form twice quickly → only one session results; no error shown.
- Password manager autofill and paste → work in both forms.
- The database is unavailable during registration or sign-in → friendly "Something went wrong. Please try again." and no partial account.
- A signed-in user opens the page in another tab → sees the signed-in state (same as feature 001).
- Throttling limits are reached while the correct password is typed → still refused until the window passes (so guessing gets no signal).

## Requirements *(mandatory)*

### Functional Requirements

**Sign-in dialog**

- **FR-001**: The sign-in dialog MUST offer, below "Continue with Google" and separated by an "or" divider, an email-and-password sign-in form and a way to switch to a "Create account" form (and back).
- **FR-002**: Both forms MUST work with password managers (autofill, paste), MUST let the user show or hide the password they are typing, and MUST show errors inside the dialog.

**Registration**

- **FR-003**: Registration MUST collect an email address, a password entered twice, and an optional display name (maximum 100 characters).
- **FR-004**: Email addresses MUST be compared and stored in a normalized form (trimmed, case-insensitive), and each normalized email MUST belong to at most one account.
- **FR-005**: Passwords MUST be 10–128 characters long; MUST NOT be equal to the email address; and MUST NOT appear in a list of commonly used passwords. No other composition rules (e.g., forced symbols) are imposed.
- **FR-006**: Passwords MUST be stored only as a salted, deliberately slow one-way hash and MUST never be logged, returned in any response, or stored in plain text anywhere.

**Sign-in and session**

- **FR-007**: A user with an active password account MUST be able to sign in with email and password.
- **FR-008**: A successful password sign-in (or registration that ends signed in) MUST produce the same session as Google sign-in in feature 001: same greeting, persistence across reloads, 60-minute idle limit, 8-hour absolute limit, server-side revocation on logout ("log out everywhere"), and the same security headers.
- **FR-009**: A new session MUST be issued at each successful sign-in; any session present in the browser before sign-in MUST NOT be reused.
- **FR-010**: Every sign-in failure (unknown email, wrong password, account without a password) MUST produce the same message, "Email or password is incorrect.", and take a similar amount of time, so the response does not reveal whether an email is registered.

**Guessing protection**

- **FR-011**: The system MUST refuse sign-in attempts for an email after 5 failures within 15 minutes, and from one network address after 20 failures within 15 minutes, until 15 minutes after the last counted failure; refusals MUST show "Too many attempts. Please try again later." and MUST behave identically for registered and unregistered emails. Registration MUST be limited to 5 accounts per network address per hour.

**Activation, linking, and recovery**

- **FR-012**: A new password account MUST stay inactive until its owner opens a verification link delivered to the email address and confirms the password chosen at registration; successful verification MUST sign the user in. Each new registration for an email that has no active password MUST replace the pending password. Links MUST be single-use, expire after 24 hours, and be replaced (old ones invalidated) whenever a new one is sent. Messages MUST be delivered to a local development mailbox that the developer can open; no real email is sent in this feature. Registration MUST always answer "Check your inbox — we've sent a verification link to {email}." whether or not the email was already registered; if the account for that email already has a password, the mailbox receives a notice instead of a verification link (see FR-014 for accounts without a password). Signing in to an unverified account with the correct password MUST send a new link and show "Please verify your email first. We've sent you a new link."; with a wrong password it MUST show the generic FR-010 message.
- **FR-013**: Existing Google sign-in behaviour from feature 001 MUST keep working unchanged for accounts that never set a password.
- **FR-014**: Each normalized email MUST map to at most one account, and Google and password sign-in for the same email MUST reach the same account, linked only after email ownership is proven:
  - **Google sign-in, email matches an existing account without a Google identity**: if Google reports the email as verified, the Google identity is attached to that account and its email is marked verified. Any password on that account that was never verified is discarded, and all existing sessions of that account end.
  - **Password registration on an email whose account has no password yet (e.g., a Google-only account)**: the password is held as pending and becomes usable only when the verification link is opened; until then Google sign-in keeps working and the pending password cannot be used.
  - **Password registration on an email whose account already has a password**: nothing changes; the mailbox receives a notice (FR-012).
  - Google accounts continue to be matched first by their Google identity (feature 001); matching by email happens only when no account has that Google identity.
- **FR-015**: Linking or unlinking sign-in methods MUST NOT allow anyone to gain access to an account without proving ownership of its email or knowing its current password.
- **FR-016**: The email sign-in form MUST offer "Forgot password?". Submitting an email MUST always show "If an account exists for {email}, we've sent a reset link." whether or not an account exists; a reset link is delivered to the local development mailbox only when an account exists for that email. Reset links MUST be single-use, expire after 30 minutes, and be invalidated when a newer one is sent or the password changes. Opening a valid link MUST let the user set a new password that follows FR-005 (entered twice). After a successful reset: the old password (and any pending password) stops working, all existing sessions of the account end, the email counts as verified, and the user is shown "Your password has been changed. You can now sign in." (they are not signed in automatically). For an account with no password (e.g., Google-only), the reset link sets its first password. Reset requests MUST be limited to 3 per email per hour and 10 per network address per hour, with the same response when the limit is reached.

**Security**

- **FR-017**: All registration, sign-in, and recovery submissions MUST have the same cross-site request protection as the existing logout (only accepted from the app's own pages).
- **FR-018**: The system MUST record security events (successful sign-in, failed sign-in, throttling triggered, registration, password reset) without recording passwords, and without recording the typed email for failed attempts on unknown accounts.

### Key Entities

- **User**: Existing account from feature 001. Now may be reached by Google, by password, or both; the Google identity becomes optional (absent for password-only accounts). Adds: normalized email (unique), whether the email has been verified (and when), and account creation method. A password-created account is inactive until its email is verified.
- **Password Credential**: The hashed password belonging to a User (at most one per user), with when it was set and whether it is pending (not yet verified) or active. Pending credentials cannot be used to sign in and are discarded if the email's owner proves ownership another way. Never readable in plain form.
- **Sign-in Attempt Record**: Recent failed attempts per normalized email and per network address, used for throttling; expires automatically after the throttling window.
- **One-time Token**: A single-use, short-lived secret sent by email, for email verification (valid 24 hours) or password reset (valid 30 minutes); stored only in hashed form; at most one active token per user and purpose.
- **Mailbox Message**: A message "sent" by the app (verification link, reset link, or notice), captured in the local development mailbox with recipient, subject, body, and time. Development-only.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user can create an account, open the verification link from the local mailbox, and be signed in within 2 minutes.
- **SC-002**: A returning user can sign in with email and password within 15 seconds of opening the dialog.
- **SC-003**: 100% of sign-in failures for unknown emails, wrong passwords, and password-less accounts show the identical message, and their response times differ by less than 20% on average.
- **SC-004**: After 5 wrong passwords for one email within 15 minutes, 100% of further attempts in the window are refused, including with the correct password.
- **SC-005**: Inspecting stored data, logs, and all responses reveals 0 plain-text passwords.
- **SC-006**: 100% of feature 001 session checks (reload persistence, logout everywhere, copied session refused after logout, 8-hour cap) pass for password-based sessions.
- **SC-007**: 0 cases where someone gains access to an existing account by registering or linking with its email without proving ownership of that email.
- **SC-008**: 100% of registration and password-reset submissions show the same response whether or not the email is registered.
- **SC-009**: A user who forgot their password can set a new one and sign in within 2 minutes, and 100% of sessions from before the reset are refused afterwards.

## Assumptions

- Same environment and constraints as feature 001: local development only, single user type, not production-ready; React + Vite frontend at http://localhost:5174 and .NET backend at https://localhost:5001 with the existing PostgreSQL database.
- "Network address" for throttling is the client address seen by the backend; locally this is a single address, so per-address limits are mostly demonstrable via tests.
- Multi-factor authentication, "remember me", changing the password while signed in (other than through recovery), account deletion, and sign-in with providers other than Google are out of scope.
- The display name, if given, is shown nowhere new in this feature (the header continues to show the email).
- The common-password list is a static list bundled with the app (e.g., the top 10,000 most common passwords); no online breach lookup.
- Hash algorithm and cost follow current industry guidance for password storage (chosen in planning: PBKDF2-HMAC-SHA512, 210,000 iterations; see research R1).
- The local development mailbox is a stand-in for real email delivery; switching to a real provider later changes only how messages are delivered, not the flows.
