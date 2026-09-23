# Quickstart & Validation: Google OAuth Login Dashboard

See [contracts/auth-api.md](./contracts/auth-api.md) and [data-model.md](./data-model.md) for details.

## Prerequisites

- .NET SDK 9 (`dotnet --list-sdks`), Node.js 22+ (`node --version`)
- PostgreSQL 16+ running locally and reachable **only from this machine**. With Docker (binds to loopback only, strong admin password):
  `docker run -d --name oauthlearn-pg -e POSTGRES_PASSWORD=<strong-admin-password> -p 127.0.0.1:5432:5432 postgres:16`
  If you use a Windows-service install instead, keep `listen_addresses = 'localhost'` in `postgresql.conf`.
- A dedicated, non-superuser app role and database (run once as the admin, e.g. `docker exec -it oauthlearn-pg psql -U postgres`):
  ```sql
  -- CREATEDB lets the test suite create/drop its temporary databases
  CREATE ROLE oauthlearn2 LOGIN PASSWORD '<strong-app-password>' CREATEDB;
  CREATE DATABASE oauthlearn2 OWNER oauthlearn2;
  ```
- Google Cloud OAuth client (Web application) with:
  - Authorized JavaScript origin: `http://localhost:5174` (optional here, harmless)
  - Authorized redirect URI: `https://localhost:5001/signin-google`
- `dotnet tool install --global dotnet-ef`
- Trusted dev certificate: `dotnet dev-certs https --trust`

## Setup

```powershell
cd backend/OAuthLearn.Api
dotnet user-secrets set "Authentication:Google:ClientId" "<your-client-id>"
dotnet user-secrets set "Authentication:Google:ClientSecret" "<your-client-secret>"
dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=5432;Database=oauthlearn2;Username=oauthlearn2;Password=<strong-app-password>"
# Never put these values in appsettings*.json, frontend/.env, or any VITE_* variable. If the client secret (starts with GOCSPX-) is ever exposed, rotate it in Google Cloud Console.
dotnet ef database update      # creates the users table (also auto-applied in Development)

cd ../../frontend
npm install
```

## Run

```powershell
# terminal 1
dotnet run --project backend/OAuthLearn.Api --launch-profile https   # https://localhost:5001
# terminal 2
cd frontend; npm run dev                                              # http://localhost:5174
```

## Automated tests

```powershell
dotnet test backend
cd frontend; npm test
```

Expected: all tests pass. Backend integration tests need Docker (Testcontainers) or a local Postgres given via `TEST_PG_CONNECTION`. That role needs `CREATEDB`, or point it at a dedicated test database it owns.

## Manual validation scenarios

| # | Steps | Expected | Covers |
|---|-------|----------|--------|
| 1 | Open `http://localhost:5174` | Header with "Login" top-right, no greeting | US1-1, FR-002 |
| 2 | Click Login, then "Continue with Google" in the dialog | Sign-in dialog appears first; the Google popup opens only after choosing Google; dashboard stays put. Esc / × / clicking outside closes the dialog | US1-2, FR-003 |
| 3 | Sign in + consent | Popup closes; "Hi {email}" left of button; button says "Logout" | US1-3, FR-006/7, SC-002 |
| 4 | Check DB: `select * from users;` | Exactly one row for your account with `last_login_at` ≈ now | FR-015, SC-007 |
| 5 | Reload page | Still signed in | US3-1, FR-011 |
| 6 | Click Logout, then reload | "Login" shown, no greeting; the `users` row still exists | US2-1/2, FR-008, FR-016 |
| 7 | Log in again | Works; still one row; `last_login_at` updated | US2-3, SC-007 |
| 8 | Click Login, close popup without signing in | Stays signed out, Login still clickable | Edge: cancel |
| 9 | Click Login, click "Cancel" on Google | Message "Sign-in was cancelled.", signed out | Edge: denied |
| 10 | Block popups for the site, click Login | Message asking to allow popups | Edge: blocked |
| 11 | Click Login twice quickly | Only one popup (second click focuses it) | Edge: double click |
| 12 | Stop Postgres, sign in | "Sign-in failed…" message, signed out | FR-017 |
| 13 | Stop backend, reload the dashboard | Signed-out state renders, no crash | Edge: backend down |
| 14 | DevTools: search page source, JS bundles, network, storage for the client secret | Not found anywhere | FR-005, SC-005 |
| 15 | DevTools → Application → Cookies | Auth cookie is `HttpOnly`, `Secure`, `SameSite=Lax` | R3 |
| 16 | While signed in, copy the `oauthlearn.auth` cookie value, then click Logout, then run `curl -k -H "Cookie: oauthlearn.auth=<value>" https://localhost:5001/api/auth/me` | `401` (the copied session is revoked) | FR-019, SC-008 |
| 17 | Sign in, then run `select session_version from users;` before and after logout | Value increases by 1 on logout | FR-019 |
| 18 | DevTools → Network → any `/api/auth/*` response | Headers include `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy` | FR-021 |
| 19 | From another machine on the same network, try to connect to port 5432 on this PC | Connection refused / times out | S3 |
| 20 | The 8 h cap is covered by an automated test (T050). For a manual check, temporarily run `dotnet user-secrets set "Auth:AbsoluteSessionLifetime" "00:02:00"`, sign in, wait 2+ min, reload, then remove the override | Signed out after the cap | FR-020, SC-009 |
