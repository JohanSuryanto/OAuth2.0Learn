# Quickstart & Validation: Account & Security Settings

See [contracts/account-api.md](./contracts/account-api.md). No new secrets. The `UserSessions` migration applies automatically on the next backend start in Development. After it, existing signed-in browsers keep working and appear as "Unknown device".

## Automated tests

```powershell
$env:TEST_PG_CONNECTION = "<connection string with CREATEDB>"
dotnet test OAuthLearn.sln
cd frontend; npm test
```

## Manual validation scenarios

Use two browsers (for example Chrome and Firefox, or a normal and a private window): **A** and **B**.

| # | Steps | Expected | Covers |
|---|-------|----------|--------|
| 1 | Sign in on A | Header shows a menu button with your name (or email), not "Hi …" + Logout | US1-1 |
| 2 | Open the menu with the mouse | Email line, Dashboard, Account settings, Log out | US1-2 |
| 3 | Tab to the button, press Enter, then ↓ ↓ ↑, End, Home, Esc | Focus moves (wrapping); Esc closes and focus returns to the button | US1-3, FR-003 |
| 4 | Menu → Account settings | `/settings` with Profile, Password, Sign-in methods, Where you're signed in | FR-004 |
| 5 | Change the display name → Save | "Profile updated."; the header and Dashboard show the new name at once | US2-2, SC-003 |
| 6 | Clear the name → Save | The header shows your email; the Dashboard says "Not set" | US2-3 |
| 7 | Sign in on B too; on A open "Where you're signed in" | Two rows; A first with "This device"; browser/OS labels; `127.0.0.x` | US4-1 |
| 8 | On A, "Sign out" B's row; then do anything on B | B lands on Home with "Your session has ended…"; A is still signed in | US4-2, SC-006 |
| 9 | Sign in on B again; on A press Log out | A is on Home; B is **still** signed in | US4-4, FR-015 |
| 10 | Password account: on A change the password (current + new ×2) | "Password changed. Other devices have been signed out."; A stays signed in; B is signed out on its next action; the new password works | US3-1, SC-004 |
| 11 | Wrong current password 5 times | "Your current password is incorrect." then "Too many attempts…" | US3-2, FR-011 |
| 12 | Google-only account: sign in, then "Set a password" within 10 min | Set immediately; then sign in with email + that password | US3-5 |
| 13 | Google-only account: wait more than 10 min (or temporarily lower the window), then "Set a password" | "Confirm it's you" → Continue with Google → the popup asks you to sign in to Google again → then the password is set | US3-4, SC-005 |
| 14 | Same as 13 but pick a different Google account in the popup | "That Google account isn't the one linked to this account."; nothing changes | US3-6, FR-012 |
| 15 | Wait until 2 min before the inactivity limit (or temporarily set cookie `ExpireTimeSpan` to 3 min) | Dialog "You'll be signed out in 1:59" counting down; "Stay signed in" closes it and the Dashboard countdown is back to about 60 min | US5-1, US5-2, SC-007 |
| 16 | Ignore the dialog | At 0: Home with "Your session has ended…" | US5-4 |
| 17 | Near the 8 h limit (temporarily set `Auth:AbsoluteSessionLifetime` to 00:05:00) | Dialog says it can't be extended; only "Sign out" and "OK" | US5-3 |
| 18 | With the dialog showing, watch DevTools → Network | No requests until you click something | FR-019 |
| 19 | Sign in with Google after renaming yourself in settings | Your chosen name is kept (not replaced by Google's) | research R8 |
| 20 | Settings → Sign-in methods | Read-only; no Disconnect button | FR-020 |
| 21 | Feature 001–003 quickstarts | Still pass, except that logout no longer signs out other devices (intended) | SC-008 |
