# Quickstart & Validation: Home Page and Signed-in Dashboard

See [contracts/api-and-routes.md](./contracts/api-and-routes.md). No new setup: same as features 001 and 002 (secrets, Postgres). There are no migrations. The frontend gets one new dependency (`react-router`), so run `npm install` in `frontend/` after pulling.

## Automated tests

```powershell
$env:TEST_PG_CONNECTION = "<connection string with CREATEDB>"
dotnet test OAuthLearn.sln
cd frontend; npm test
```

## Manual validation scenarios

| # | Steps | Expected | Covers |
|---|-------|----------|--------|
| 1 | Signed out, open `http://localhost:5174/` | Home: welcome heading, explanation, "Get started"; no account details | US1-1, FR-001 |
| 2 | Click "Get started" | The same sign-in dialog as the header's Login | US1-2 |
| 3 | Sign in with Google | Dialog closes; URL is `/dashboard`; "Welcome, {name}"; methods "Google" | US2-1, US2-3 |
| 4 | Log out | URL is `/`, Home shown, no "session ended" notice | US3-1, FR-005 |
| 5 | Press the browser Back button | Home (not the Dashboard), no account details | US3-3, SC-003 |
| 6 | Signed out, type `http://localhost:5174/dashboard` | Ends on `/` without a flash of account details | US2-5, SC-001 |
| 7 | Sign in with email + password | `/dashboard`; methods "Password" (or "Google and Password" if linked) | US2-1, US2-4 |
| 8 | Register a new email, verify via `/dev/mailbox` | After "Verify and sign in" → `/dashboard` | US2-2 |
| 9 | Signed in, open `/` | Goes to `/dashboard` | US1-3 |
| 10 | On the Dashboard, watch the countdowns | Both tick every second; about 60 min and about 8 h right after sign-in; "Ends first" on the inactivity one | US4-1 |
| 11 | Wait about 35 min, then reload | The inactivity countdown is back to about 60 min; the absolute one keeps counting down | US4-2, R3 |
| 12 | Leave the Dashboard untouched past the inactivity limit (or temporarily set cookie `ExpireTimeSpan` to 2 min) | At zero: Home with "Your session has ended. Please sign in again." | US4-5, US3-2, SC-004, SC-005 |
| 13 | Two tabs on the Dashboard; log out in tab A; focus tab B | Tab B shows Home with the session-ended notice | US3-2 |
| 14 | Open `http://localhost:5174/nope` | "Page not found" + "Go to Home" | FR-008 |
| 15 | Stop the backend, reload `/dashboard` | Treated as signed out, so Home (feature 001 behaviour) | Edge cases |
| 16 | DevTools → Network: stay on the Dashboard for 2 minutes | No requests while the countdown ticks (only on focus) | FR-017 |
| 17 | DevTools → a `/api/account` response | `Cache-Control: no-store` | R8 |
| 18 | Feature 001 and 002 quickstarts (sign-in, verify, reset, logout) | Still pass | SC-007 |
