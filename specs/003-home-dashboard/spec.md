# Feature Specification: Home Page and Signed-in Dashboard

**Feature Branch**: `003-home-dashboard`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Split the OAuth2.0Learn app into a public Home page and a signed-in Dashboard. Existing features: 001 Google sign-in (popup, HttpOnly cookie session, 60 min idle / 8 h absolute limits, logout everywhere) and 002 email/password (register, verify via dev mailbox, sign in, forgot/reset). Today both signed-out and signed-in users see the same single page with a header (Login/Logout, "Hi {email}"). Wanted: (1) Home page at "/" for signed-out visitors: short welcome explaining what the app demonstrates, and a "Get started" button that opens the existing sign-in dialog; signed-in users visiting "/" are sent to the dashboard. (2) Dashboard at "/dashboard", only for signed-in users (signed-out visitors are sent to Home): welcome with the user's name (or email), account details — email, display name, sign-in methods linked to the account (Google, password, or both), account creation time, last sign-in time — and session details: when the session will expire (60-minute inactivity limit and 8-hour absolute limit), shown as a live countdown. (3) After a successful sign-in (Google or password or email verification) the user lands on the dashboard; after logout they land on Home. The header stays as it is (Login/Logout on the right, "Hi {email}" when signed in). The existing verify-email and reset-password pages keep working. Session expiry while on the dashboard should return the user to Home. Stack unchanged: React + Vite frontend (http://localhost:5174), .NET 9 backend (https://localhost:5001), PostgreSQL."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Signed-out visitors see a Home page (Priority: P1)

A visitor who is not signed in opens the app and sees a Home page: a short welcome that explains the app lets them try signing in with Google or with email and password, and a "Get started" button. Clicking "Get started" opens the same sign-in dialog as the header's "Login" button.

**Why this priority**: It is the entry point for everyone who isn't signed in and replaces today's placeholder text.

**Independent Test**: While signed out, open the app's root address and confirm the welcome text and "Get started" button appear, and that the button opens the sign-in dialog.

**Acceptance Scenarios**:

1. **Given** a signed-out visitor, **When** they open the Home page, **Then** they see a welcome heading, one or two sentences explaining what the app demonstrates, and a "Get started" button; no account details are shown.
2. **Given** the Home page, **When** the visitor clicks "Get started", **Then** the sign-in dialog opens exactly as it does from the header's "Login" button.
3. **Given** a signed-in user, **When** they open the Home page address, **Then** they are taken to the Dashboard instead.

---

### User Story 2 - Signed-in users land on a Dashboard with their account details (Priority: P1)

After signing in by any method (Google, email and password, or finishing email verification), the user is taken to the Dashboard. It greets them by name (or email if they have no display name) and shows their account details: email, display name, which sign-in methods their account has (Google, password, or both), when the account was created, and when they last signed in.

**Why this priority**: It is the "logged-in area" the user asked for and the main visible change.

**Independent Test**: Sign in with Google, confirm the Dashboard opens with the correct details and "Google" as the method; sign out, sign in with a password account, and confirm "Password"; for an account with both, confirm both are listed.

**Acceptance Scenarios**:

1. **Given** a signed-out visitor on Home, **When** they sign in successfully with Google or with email and password, **Then** the sign-in dialog closes and they are on the Dashboard.
2. **Given** a user who has just verified their email (feature 002), **When** verification succeeds, **Then** they are taken to the Dashboard.
3. **Given** the Dashboard, **When** it loads, **Then** it shows "Welcome, {display name}" (or "Welcome, {email}" when there is no display name), and the email, display name ("Not set" when empty), sign-in methods, account creation time, and last sign-in time, with times in the viewer's local time zone.
4. **Given** an account with a Google identity and an active (verified) password, **When** the Dashboard loads, **Then** both "Google" and "Password" are listed; a password that is still waiting for verification is not listed.
5. **Given** a signed-out visitor, **When** they open the Dashboard address directly (typed, bookmarked, or via the browser's back button after logging out), **Then** they are taken to Home and no account details are shown, not even briefly.

---

### User Story 3 - Leaving: logout and expiry return the user to Home (Priority: P1)

When a user logs out, they end up on Home. When their session ends while they are on the Dashboard — because of the inactivity limit, the 8-hour limit, a logout in another tab, or a password reset — they are returned to Home with a short explanation.

**Why this priority**: Without it, a signed-out user could be left looking at a Dashboard whose data is no longer theirs to see.

**Independent Test**: Log out from the Dashboard and confirm Home is shown; separately, let a session expire (or log out in another tab) and confirm the Dashboard switches to Home with the explanation.

**Acceptance Scenarios**:

1. **Given** a user on the Dashboard, **When** they click "Logout", **Then** they are on Home.
2. **Given** a user on the Dashboard whose session has ended for any reason, **When** the app next notices it (at the countdown reaching zero, when the window regains focus, or on the next action), **Then** they are taken to Home and see "Your session has ended. Please sign in again."
3. **Given** a user who logged out, **When** they press the browser's back button, **Then** they do not see the Dashboard's account details.

---

### User Story 4 - See when my session will end (Priority: P2)

On the Dashboard, the user sees when their session will end under each limit: the time left before the inactivity limit (60 minutes) and before the absolute limit (8 hours after sign-in), each as a live countdown that ticks every second, plus the exact end time. It is clear which limit will end the session first.

**Why this priority**: It makes the session rules from feature 001 visible, which is the learning goal, but the Dashboard is useful without it.

**Independent Test**: Sign in, open the Dashboard, and confirm both countdowns tick down every second and match the rules (about 60 minutes and about 8 hours right after sign-in); reload after a few minutes and confirm the inactivity countdown resets while the 8-hour one keeps counting down.

**Acceptance Scenarios**:

1. **Given** a user who just signed in, **When** they open the Dashboard, **Then** the inactivity countdown shows about 60 minutes and the absolute countdown about 8 hours, both decreasing every second.
2. **Given** the Dashboard, **When** the user reloads it (a real interaction with the app), **Then** the inactivity countdown reflects that activity, and the absolute countdown does not reset.
3. **Given** the Dashboard left open without interaction, **When** time passes, **Then** the countdowns keep ticking down and the page's own display updates never extend the session.
4. **Given** less than 5 minutes remain before either limit, **When** the user looks at the Dashboard, **Then** that countdown is visually highlighted as ending soon.
5. **Given** a countdown reaches zero, **When** that happens, **Then** the user is returned to Home as in User Story 3.

---

### Edge Cases

- The app is still checking whether the visitor is signed in → neither Home nor Dashboard content is shown yet (a neutral loading state), so a signed-in user never sees a flash of Home and a signed-out visitor never sees a flash of Dashboard data.
- Signing in from the Dashboard address after being sent to Home (e.g., a bookmark) → after sign-in they land on the Dashboard.
- Signing in while already on a verify-email or reset-password page → those pages keep their current behaviour; reset still ends with "You can now sign in", and successful verification goes to the Dashboard.
- An unknown address inside the app → a "Page not found" message with a link to Home.
- The backend is unreachable when the Dashboard loads → the user sees "Couldn't load your account details. Please try again." with a retry option, and is not shown stale or partial data; if the session check itself fails, they are treated as signed out (feature 001 behaviour).
- The user's computer clock is wrong → countdowns are still correct because they are based on the time left reported by the server, not on the computer's clock.
- The Dashboard is open in two tabs and the user logs out in one → the other tab goes to Home the next time it regains focus or its countdown ends.
- An account created by Google with a password added later (feature 002) → both methods listed; an account whose unverified password was discarded → only Google.
- Display name longer than fits on screen → wraps without breaking the layout.

## Requirements *(mandatory)*

### Functional Requirements

**Pages and navigation**

- **FR-001**: The app MUST have a Home page at its root address, shown to visitors who are not signed in, with a welcome heading, a short explanation of what the app demonstrates (signing in with Google, or with email and password), and a "Get started" button that opens the existing sign-in dialog.
- **FR-002**: The app MUST have a Dashboard page at `/dashboard` shown only to signed-in users.
- **FR-003**: A signed-in user who opens the Home address MUST be taken to the Dashboard; a signed-out visitor who opens the Dashboard address MUST be taken to Home without any account details being displayed.
- **FR-004**: After any successful sign-in (Google, email and password, or email verification), the user MUST land on the Dashboard.
- **FR-005**: After logging out, the user MUST land on Home.
- **FR-006**: While the sign-in status is still being determined, the app MUST show a neutral loading state instead of Home or Dashboard content.
- **FR-007**: The header (app title, "Hi {email}", Login/Logout) MUST stay as it is on every page. The existing verify-email and reset-password pages MUST keep working.
- **FR-008**: Unknown addresses inside the app MUST show a "Page not found" message with a link to Home.
- **FR-009**: Moving between Home and Dashboard MUST NOT reload the whole page, and the browser's back and forward buttons MUST follow the rules in FR-003.

**Dashboard content**

- **FR-010**: The Dashboard MUST greet the user with "Welcome, {display name}", or "Welcome, {email}" when no display name is set.
- **FR-011**: The Dashboard MUST show the account's email, display name ("Not set" when empty), sign-in methods, account creation time, and last sign-in time. Times MUST be shown in the viewer's local time zone with date and time.
- **FR-012**: Sign-in methods MUST list "Google" when the account has a Google identity and "Password" when it has an active (verified) password; a password still waiting for verification MUST NOT be listed.
- **FR-013**: Account details MUST come from the server for the signed-in user only; the Dashboard MUST NOT show another user's data or cached data after the user changed.
- **FR-014**: If account details can't be loaded, the Dashboard MUST show "Couldn't load your account details. Please try again." with a way to retry, and no partial data.

**Session countdown**

- **FR-015**: The Dashboard MUST show, for the current session, the time remaining until the inactivity limit ends it and until the absolute limit ends it, each as a countdown updating every second, together with the exact end time in local time, and MUST indicate which one comes first.
- **FR-016**: Countdown values MUST be derived from the time remaining as reported by the server, so they are correct even if the viewer's computer clock is wrong, and MUST match when the server actually ends the session to within 1 minute.
- **FR-017**: Updating the countdown display MUST NOT count as activity; only real interactions with the app (such as loading or reloading the Dashboard) may extend the inactivity limit, as defined in feature 001.
- **FR-018**: A countdown with less than 5 minutes left MUST be visually highlighted as ending soon.

**Session end**

- **FR-019**: When the session ends while the user is on the Dashboard (a countdown reaches zero, the window regains focus after a logout or reset elsewhere, or any request finds the session gone), the app MUST take the user to Home and show "Your session has ended. Please sign in again." The message MUST NOT be shown after a normal logout.

### Key Entities

- **Account Summary**: What the Dashboard shows about the signed-in user: email, display name, sign-in methods (Google and/or Password), account creation time, last sign-in time. Read-only; derived from the existing User and Password Credential data (features 001 and 002).
- **Session Timing**: For the current session: time remaining until the inactivity limit and until the absolute limit (and the corresponding end times). Derived from the existing session rules; nothing new is stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of signed-out visits to the Dashboard address end on Home without any account detail becoming visible.
- **SC-002**: After signing in by any method, the user sees the Dashboard with their details within 2 seconds.
- **SC-003**: After logout, the user is on Home within 1 second, and pressing back shows no account details in 100% of attempts.
- **SC-004**: The session countdowns match the moment the server actually ends the session to within 1 minute, and leaving the Dashboard open for longer than the inactivity limit without interacting ends the session (the page does not keep it alive).
- **SC-005**: When a session ends while the Dashboard is open, the user is on Home with the session-ended message within 5 seconds of the countdown reaching zero, or immediately on regaining focus.
- **SC-006**: The listed sign-in methods are correct for 100% of account types: Google-only, password-only, and both.
- **SC-007**: All existing sign-in, verification, reset, and logout checks from features 001 and 002 still pass.

## Assumptions

- Same environment and constraints as features 001 and 002: local development, single user type, React + Vite frontend at http://localhost:5174, .NET backend at https://localhost:5001, existing PostgreSQL database.
- "Last sign-in time" is the last successful sign-in by any method (already recorded by features 001 and 002); it is shown as stored at the time the Dashboard loads.
- The inactivity limit follows feature 001's rolling 60-minute rule as the server actually applies it; the Dashboard shows the server's view of when the session will end, which may be less than a full 60 minutes right after an interaction.
- The Home page is intentionally short (heading, 1–2 sentences, one button); no marketing sections, images, or extra pages.
- Account editing (changing the display name, password, or linked methods) is out of scope; the Dashboard is read-only.
- Returning to the exact page a user originally asked for after sign-in is only needed for the Dashboard, since it is the only protected page.
