# Quickstart & Validation: Email & Password Registration and Sign-in

See [contracts/password-auth-api.md](./contracts/password-auth-api.md) and [data-model.md](./data-model.md). Prerequisites and base setup are the same as [feature 001's quickstart](../001-google-oauth-login/quickstart.md).

## Extra setup

```powershell
cd backend/OAuthLearn.Api
# New required secret: key for hashing emails/IPs in the throttle table (a "pepper").
$bytes = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Auth:LookupHashKey" ([Convert]::ToBase64String($bytes))
```

The `EmailPasswordAuth` migration is applied automatically on the next backend start in Development.

## Run

Same as 001, plus the development mailbox at **https://localhost:5001/dev/mailbox**.

## Automated tests

```powershell
$env:TEST_PG_CONNECTION = "<connection string with CREATEDB>"   # or start Docker
dotnet test OAuthLearn.sln
cd frontend; npm test
```

## Manual validation scenarios

| # | Steps | Expected | Covers |
|---|-------|----------|--------|
| 1 | Login → "Create one" → fill in a new email and a valid password twice → Create account | Dialog: "Check your inbox — we've sent a verification link to {email}." | US1-2 |
| 2 | Open `/dev/mailbox` | A "Verify your email" message with a `…/verify-email#token=…` link | FR-012 |
| 3 | Open the link and enter the password you chose | "Your email is verified. You're signed in."; back on the dashboard: "Hi {email}" + Logout | US1-3, R4 |
| 4 | Open the same link again | "This link is no longer valid…" | FR-012 |
| 5 | Log out, then sign in with email + password | Signed in; reload keeps you signed in | US2-1, US2-3, SC-006 |
| 6 | Sign in with `  YOUR@EMAIL  ` (other case and spaces) | Signed in | US2-4, FR-004 |
| 7 | Wrong password; then an unknown email | Both: "Email or password is incorrect." | US2-2, FR-010 |
| 8 | 5 wrong passwords for one email, then the correct one | 6th: "Too many attempts. Please try again later."; works again after 15 min | US3, FR-011 |
| 9 | Register a new email but **don't** verify; then sign in with that password | "Please verify your email first. We've sent you a new link."; a new message in the mailbox; the old link is now invalid | US1-4 |
| 10 | Register again with an email that already has a password | Same "Check your inbox" message; the mailbox gets a "someone tried to register" notice; the old password still works | US1-7, SC-008 |
| 11 | Forgot password? → enter your email | "If an account exists for {email}, we've sent a reset link."; a reset message in the mailbox | US5-1 |
| 12 | Forgot password? → unknown email | Same message; no mailbox message | US5-2, SC-008 |
| 13 | While signed in in tab A, reset the password in tab B via the link | Tab B: "Your password has been changed…"; tab A: signed out on reload; old password refused, new one works | US5-3, US5-4, SC-009 |
| 14 | Google-linked account: sign in with Google whose email equals an existing **verified** password account | Same account (the `users` table has one row for the email); both methods work afterwards | US4-2 |
| 15 | Register a password for your **Google** account's email → verify via the link with that password | You're signed in to the same account; Google still works | US4-1 |
| 16 | Register a password for an email (don't verify), then sign in with Google for that email | Google sign-in works; the unverified password no longer works (not even "please verify") | US4-3, SC-007 |
| 17 | DB: `select * from password_credentials; select * from rate_limit_events;` | Only `AQAAAAIAA…`-style hashes, no plain passwords; `key_hash` bytes, no plain emails/IPs | FR-006, FR-018, SC-005 |
| 18 | Logs while doing 1–13 | No passwords, tokens or failed-attempt emails in the output | FR-018 |
| 19 | DevTools → Network on the verify page | The token is in the URL fragment only; never sent to any server except in the JSON body to `/api/auth/verify-email` | R5 |
