# Feature Specification: Google OAuth Login Dashboard

**Feature Branch**: `001-google-oauth-login`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "so as you know this folder name is OAuth2.0Learn, basically i want to learn about oauth, especially google. so i want the FE will be react vite and the BE will .NET Core c#. i already have my client id and secret from google, now i want to implement the apps, web apps. So make a simple dashboard with login button in the right corner, when click on the login button it will open a popup/modal for login via google, once login success, simply show the Hi {email} in the leftside of login button, but pls note, that login text will transform to logout once alread login, and logout function need to done as well. backend url i have set is https://localhost:5001 and the authrorized redirect is https://localhost:5001/signin-google, you can set the FE to http://localhost:5174/."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in with Google (Priority: P1)

A visitor opens the dashboard and sees a header with a "Login" button in the top-right corner. They click "Login", and a separate sign-in window opens showing Google's sign-in and consent screen. After they choose their Google account and approve, the sign-in window closes by itself and the dashboard header now shows "Hi {email}" (their Google email address) immediately to the left of the button, and the button now reads "Logout".

**Why this priority**: This is the core learning goal — seeing the complete Google sign-in round trip work end to end. Without it nothing else is meaningful.

**Independent Test**: Open the dashboard as a guest, click "Login", complete Google sign-in in the popup, and confirm the header shows "Hi {your email}" and a "Logout" button without the main page navigating away.

**Acceptance Scenarios**:

1. **Given** a visitor who is not signed in, **When** they open the dashboard, **Then** they see a header with a "Login" button in the top-right corner and no greeting.
2. **Given** a visitor who is not signed in, **When** they click "Login", **Then** a sign-in dialog appears offering "Continue with Google"; **When** they choose it, **Then** a separate sign-in window opens showing Google's sign-in screen, and the dashboard itself stays on the same page.
3. **Given** the sign-in window is open, **When** the visitor successfully signs in and grants consent, **Then** the sign-in window closes automatically, the header shows "Hi {email}" to the left of the button, and the button label reads "Logout".

---

### User Story 2 - Sign out (Priority: P1)

A signed-in user clicks "Logout". Their session in the app ends, the "Hi {email}" greeting disappears, and the button changes back to "Login".

**Why this priority**: The user explicitly requires a working logout; a login flow that cannot be reversed is incomplete for learning the full session lifecycle.

**Independent Test**: While signed in, click "Logout", confirm the greeting disappears and the button reads "Login", then reload the page and confirm the user is still signed out.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they click "Logout", **Then** the greeting is removed and the button label changes to "Login".
2. **Given** a user who has just logged out, **When** they reload the dashboard, **Then** they remain signed out (no greeting, "Login" button shown).
3. **Given** a user who has just logged out, **When** they click "Login" again, **Then** they can sign in again successfully.

---

### User Story 3 - Stay signed in across page reloads (Priority: P2)

A signed-in user refreshes the dashboard or opens it in a new tab and still sees "Hi {email}" and "Logout" without having to sign in again, until they log out or their session expires.

**Why this priority**: Expected behavior of any real web app and demonstrates how a sign-in session is kept after the OAuth flow completes, but the core flow works without it.

**Independent Test**: Sign in, reload the page, and confirm the greeting and "Logout" button are still shown.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they reload the dashboard, **Then** the header still shows "Hi {email}" and "Logout".
2. **Given** a user whose session has expired, **When** they open or reload the dashboard, **Then** they see the signed-out state with the "Login" button.

---

### Edge Cases

- The visitor closes the sign-in window, or cancels, before finishing → the dashboard stays in the signed-out state and the "Login" button remains usable.
- The visitor declines consent on Google's screen → the sign-in window closes (or can be closed) and the dashboard shows a short, friendly "Sign-in was cancelled." message while remaining signed out (other failures show "Sign-in failed. Please try again.").
- The browser blocks the popup → the dashboard shows a message asking the user to allow popups for this site.
- The visitor clicks "Login" repeatedly while a sign-in window is already open → the existing window is focused instead of opening multiple windows.
- The Google account does not provide an email address → sign-in is treated as failed and the user sees a friendly error.
- The database is unavailable during sign-in → sign-in fails gracefully, the user stays signed out, and sees a friendly error.
- A returning user whose Google email has changed → matched to their existing record by Google account identifier, and the email is updated.
- The sign-in service is unreachable when the page loads → the dashboard still renders in the signed-out state rather than breaking.
- The user logs out in one tab while another tab is open → the other tab shows the signed-out state on its next reload.
- The Google account's email address is not verified by Google → sign-in is treated as failed and the user sees a friendly error.
- Someone copied the user's session before the user logged out → after logout, the copied session no longer works.
- A user keeps the dashboard active for more than 8 hours → they are signed out and must sign in again, even though they were never idle.
- The database is unavailable while a signed-in user reloads → the dashboard shows the signed-out state (the app fails closed rather than trusting an unverifiable session).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST display a dashboard page with a header containing an authentication button in the top-right corner.
- **FR-002**: When the user is signed out, the button MUST read "Login" and no greeting is shown.
- **FR-003**: Clicking "Login" MUST open a sign-in dialog on the dashboard listing the available sign-in methods (currently only "Continue with Google"). Choosing "Continue with Google" MUST open Google's sign-in in a separate popup window, keeping the dashboard page in place. The dialog can be closed (close button, Escape, or clicking outside) without signing in. Sign-in errors are shown inside the dialog.
- **FR-022**: The sign-in dialog MUST be structured so additional methods (e.g., email/password sign-in and account registration, followed by the same session handling) can be added later without changing the Google flow. Those methods are out of scope for this feature.
- **FR-004**: The system MUST use Google as the only sign-in provider for this feature.
- **FR-005**: The app's Google client secret MUST only be used server-side and MUST never be sent to or visible in the user's browser.
- **FR-006**: Upon successful sign-in, the popup MUST close automatically and the dashboard MUST update without a manual reload.
- **FR-007**: When the user is signed in, the header MUST show "Hi {email}" (the email from the user's Google account) immediately to the left of the button, and the button MUST read "Logout".
- **FR-008**: Clicking "Logout" MUST end the user's session in this app, remove the greeting, and change the button back to "Login".
- **FR-009**: Logout MUST NOT sign the user out of their Google account itself; it only ends the session in this app.
- **FR-010**: On page load, the dashboard MUST determine whether a valid session exists and show the matching signed-in or signed-out state.
- **FR-011**: A signed-in session MUST survive page reloads until the user logs out or the session expires.
- **FR-012**: If sign-in is cancelled, denied, blocked, or fails, the dashboard MUST remain signed out and show a short, user-friendly message where applicable (no raw error details).
- **FR-013**: The Google client ID and secret MUST be supplied through configuration kept out of source-controlled files.
- **FR-014**: Browser requests from any origin other than the configured dashboard address MUST be refused when they try to read the user's sign-in status or trigger logout.
- **FR-015**: On a user's first successful sign-in, the system MUST create a persistent user record from their Google identity; on later sign-ins it MUST reuse that record (matched by Google account identifier) and update its last sign-in time and email/name if they changed.
- **FR-016**: Logging out MUST NOT delete the user's persistent record.
- **FR-017**: If the user record cannot be saved during sign-in, the sign-in MUST be treated as failed and the dashboard MUST remain signed out with a friendly error message.
- **FR-018**: The system MUST reject sign-in when Google does not confirm the account's email address as verified.
- **FR-019**: Logging out MUST invalidate the user's sessions on the server side, so that any copy of a session taken before logout is refused afterwards. This applies to all of the user's browsers ("log out everywhere").
- **FR-020**: A session MUST end no later than 8 hours after sign-in, regardless of activity, in addition to the 60-minute inactivity expiry.
- **FR-021**: Server responses MUST instruct browsers not to embed the app's pages in frames on other sites and not to guess content types. The sign-in completion page MUST only run scripts served by the app itself.

### Key Entities

- **User**: A person who has signed in to the app at least once, stored persistently. Attributes: Google account identifier (unique), email, display name (optional), first sign-in time, last sign-in time, session generation (a counter that is increased on logout to invalidate all existing sessions).
- **User Session**: Represents a signed-in user in the current browser. Attributes: reference to the User, email, session generation at sign-in, sign-in time, expiry. Created on successful Google sign-in. It is valid only while its session generation matches the User's, it is within 60 minutes of the last activity, and it is within 8 hours of sign-in.
- **Google Identity**: The identity information returned by Google after sign-in (account identifier, email, and optionally name). Used to create or update the User record; Google access/refresh tokens are not stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from clicking "Login" to seeing "Hi {email}" in under 30 seconds (excluding time spent typing Google credentials).
- **SC-002**: After the sign-in window closes, the greeting appears within 2 seconds without any manual page reload.
- **SC-003**: After clicking "Logout", the signed-out state is shown within 1 second and persists after reload in 100% of attempts.
- **SC-004**: 100% of cancelled, denied, or failed sign-in attempts leave the dashboard in a usable signed-out state.
- **SC-005**: Inspecting everything the browser receives (page source, network traffic, storage) reveals the client secret in 0 cases.
- **SC-006**: A signed-in user who reloads the page sees the signed-in state again in 100% of attempts before session expiry.
- **SC-007**: Signing in with the same Google account any number of times results in exactly one stored user record, whose last sign-in time matches the most recent sign-in.
- **SC-008**: 100% of requests that use a session copied before logout are refused after logout.
- **SC-009**: 100% of sessions older than 8 hours are refused, even if they were used continuously.
- **SC-010**: 0 Google accounts with an unverified email are signed in.

## Assumptions

- This is a personal learning project run on a local machine only; there is a single type of user and no roles or permissions.
- The frontend is a React + Vite app served at http://localhost:5174; the backend is a .NET Core (C#) service at https://localhost:5001; Google's authorized redirect URI is https://localhost:5001/signin-google (already registered by the user, along with a client ID and secret).
- "Popup/modal" is implemented as a separate popup window, because Google does not allow its sign-in page to be embedded inside a modal on another site.
- User records are stored in a PostgreSQL database, set up and run locally by the developer. Connection details are supplied through configuration kept out of source control.
- Only user records live in the database. The sign-in session itself is still tracked per browser rather than as database rows, but each request is checked against the user's session generation in the database so logout can revoke sessions.
- Session lifetime: 60 minutes of inactivity (sliding), an absolute maximum of 8 hours from sign-in, or until the browser session ends, whichever comes first.
- Logout is "log out everywhere" for simplicity: it ends the user's sessions on all browsers, not only the current one.
- The local database only accepts connections from the developer's own machine and is accessed with a dedicated, non-administrator account.
- This setup is for local development only and is not production-ready. Deploying it would need HTTPS for the frontend, a same-site deployment, persisted key storage for session encryption, and a managed secret store. These are out of scope.
- The dashboard has no content beyond the header and a simple placeholder body.
- No access to other Google APIs (Drive, Calendar, etc.) is requested; only basic identity (email, profile).
- The developer's local HTTPS development certificate is trusted by the browser.
- Local development targets current Chromium-based browsers and Firefox; Safari is not supported for local dev (it may not send secure cookies to `http://localhost`).
