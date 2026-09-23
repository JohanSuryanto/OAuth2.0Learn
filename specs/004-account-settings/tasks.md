---

description: "Task list for Account & Security Settings"
---

# Tasks: Account & Security Settings

**Input**: Design documents from `/specs/004-account-settings/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/account-api.md, quickstart.md

**Tests**: Included. plan.md (Testing) and research R12 commit to extending the existing xUnit and Vitest suites, as in features 001–003. Backend DB tests need `TEST_PG_CONNECTION`.

**Organization**: Grouped by user story: US1 user menu, US2 display name, US3 change/set password + re-auth, US4 active sessions, US5 expiry warning.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US5 from spec.md

## Path Conventions

- Backend: `backend/OAuthLearn.Api/`; tests `backend/OAuthLearn.Api.Tests/`
- Frontend: `frontend/src/`
- Endpoints, codes and UI texts: `specs/004-account-settings/contracts/account-api.md` (below: **the contract**)

---

## Phase 1: Setup

- [X] T001 Baseline: run `dotnet test OAuthLearn.sln` (with `TEST_PG_CONNECTION`) and `npm test` in `frontend/`, and record that 107 backend and 111 frontend tests pass before any change

---

## Phase 2: Foundational (Blocking Prerequisites): per-device sessions

**⚠️ CRITICAL**: US1 (logout), US3 (password change), US4 and US5 all depend on this phase.

- [X] T002 Create `backend/OAuthLearn.Api/Data/UserSession.cs` and map it in `backend/OAuthLearn.Api/Data/AppDbContext.cs` as table `user_sessions`:
  - `id` uuid PK (`ValueGeneratedNever`);
  - `user_id` "NOT NULL, FK → users.id ON DELETE CASCADE, indexed";
  - `created_at`, `auth_time`, `last_auth_at`, `last_seen_at`: "NOT NULL" `timestamptz`;
  - `device_label`: "NULL, ≤ 200 chars" (`HasMaxLength(200)`);
  - `ip_masked`: "NULL";
  - `revoked_at`: "NULL" `timestamptz`.
- [X] T003 Generate the migration with `$env:ConnectionStrings__Default='Host=127.0.0.1;Database=design_time_only'; dotnet tool run dotnet-ef migrations add UserSessions --project backend/OAuthLearn.Api --output-dir Migrations`. Verify it only creates `user_sessions` plus its index and FK. Depends on T002
- [X] T004 [P] Create `backend/OAuthLearn.Api/Auth/UserAgentDescriber.cs` (`static string? Describe(string? userAgent)`, returning "{Browser} on {OS}" per research R9, or null when unknown, truncated to 200) and `backend/OAuthLearn.Api/Auth/IpMasker.cs`:
  - `static string? Mask(IPAddress?)`: IPv4 `a.b.c.x`; IPv4-mapped IPv6 treated as IPv4; IPv6 → the first 4 groups + `:…`; null → null.
  
  Add table-driven tests in `backend/OAuthLearn.Api.Tests/DeviceInfoTests.cs`:
  - Chrome/Edge/Firefox/Safari/Opera on Windows/macOS/iOS/Android/Linux;
  - empty or garbage user agent → null;
  - `127.0.0.1` → `127.0.0.x`, `::1` → `0:0:0:0:…`, `::ffff:10.1.2.3` → `10.1.2.x`.
- [X] T005 Add `public const string Sid = "sid"` to `backend/OAuthLearn.Api/Auth/AuthClaims.cs` and change `SessionClaims(User, TimeProvider)` to `SessionClaims(User user, Guid sid, DateTimeOffset authTime)`, emitting `app_user_id`, `session_version`, `auth_time` (from `authTime`) and `sid`. Update `ForUser` accordingly
- [X] T006 Create `backend/OAuthLearn.Api/Data/SessionStore.cs` (scoped; `AppDbContext`, `TimeProvider`):
  - `Task<UserSession> CreateAsync(Guid userId, HttpContext ctx, DateTimeOffset authTime, ct)`: `created_at = last_auth_at = last_seen_at = now`, `auth_time = authTime`, `device_label = UserAgentDescriber.Describe(User-Agent)`, `ip_masked = IpMasker.Mask(RemoteIpAddress)`.
  - `Task<(UserSession? Session, int? SessionVersion)> FindAsync(Guid sid, Guid userId, ct)`: one query joining `users`.
  - `Task TouchAsync(UserSession s, ct)`: sets `last_seen_at` only if it's older than 5 min; 1/100 purge of rows with `auth_time < now − 8h`.
  - `Task<List<UserSession>> ListValidAsync(Guid userId, ct)`: "`revoked_at IS NULL AND last_seen_at > now − 60 min AND auth_time > now − 8 h`".
  - `Task<bool> RevokeAsync(Guid userId, Guid sid, ct)`.
  - `Task RevokeAllExceptAsync(Guid userId, Guid? keepSid, ct)`.
  - `Task MarkAuthenticatedAsync(Guid sid, ct)`: sets `last_auth_at = now`.
  - `bool IsRecentlyAuthenticated(UserSession s)`: `now − last_auth_at ≤ 10 min`.
  
  Register it as scoped in `backend/OAuthLearn.Api/Program.cs` (research R1, R4)
- [X] T007 Update `backend/OAuthLearn.Api/Auth/SessionIssuer.cs`:
  - `SignInAsync(ctx, user)` now calls `SessionStore.CreateAsync(user.Id, ctx, now)` and issues claims with that `sid` and `auth_time = now`.
  - Add `ReissueAsync(HttpContext ctx, User user, Guid sid, DateTimeOffset authTime)`, which re-signs the cookie with the same `sid` and `auth_time` but the current `session_version` and display name (research R3).
  
  Depends on T005, T006
- [X] T008 Update the Google path (research R8):
  - In `backend/OAuthLearn.Api/Data/UserService.cs` `UpsertFromGoogleAsync`: on the **sub-match** and **link** branches set `DisplayName` only when it is currently null. New accounts still use Google's name.
  - In `backend/OAuthLearn.Api/Auth/GoogleAuthEvents.cs` `OnCreatingTicket` (normal sign-in purpose):
    - replace any `ClaimTypes.Name` claim with `user.DisplayName`, or remove it when null;
    - call `SessionStore.CreateAsync(user.Id, ctx, now)`;
    - add `AuthClaims.SessionClaims(user, session.Id, now)`.
  
  Update `backend/OAuthLearn.Api.Tests/UserServiceTests.cs` `Later_upsert_…`: the name now stays "Old", while the email still changes. Depends on T005, T006
- [X] T009 Update `backend/OAuthLearn.Api/Auth/SessionValidator.cs` (research R1, R2):
  - Parse `sid`.
  - **When present**: `SessionStore.FindAsync(sid, userId)`. Reject (`missing_claims` / `revoked` / `db_unavailable`) when:
    - the row is missing;
    - `revoked_at` is set;
    - the version differs from the claim.
    
    Otherwise `TouchAsync`.
  - **When absent (legacy cookie)**: the existing version check. If it passes, create a row with `device_label = NULL`, `created_at = last_auth_at = auth_time` from the claim, and `ip_masked` from the request. Add the `sid` claim via `context.ReplacePrincipal(...)` and `context.ShouldRenew = true`.
  
  Keep the 8 h check first. Depends on T006
- [X] T010 Update logout in `backend/OAuthLearn.Api/Auth/AuthEndpoints.cs`: if the `sid` claim is present, `SessionStore.RevokeAsync(userId, sid)` only; otherwise (legacy) keep `RevokeSessionsAsync` (version bump). Then delete the cookie (FR-015). Update `AccountService.ResetPasswordAsync` in `backend/OAuthLearn.Api/Data/AccountService.cs` to also call `SessionStore.RevokeAllExceptAsync(user.Id, null)` (US4-5). Depends on T006
- [X] T011 [P] Create `backend/OAuthLearn.Api.Tests/SessionsTests.cs` for the foundation (real cookies; `TestAppFactory`; `factory.Time`):
  - password sign-in, verify and Google upsert + `SessionIssuer` each create a `user_sessions` row with `device_label` from the request's User-Agent header;
  - with two clients A and B signed in to one account, logout on A → A's `/me` 401 and B's `/me` 200 (FR-015);
  - revoking B's row directly in the DB → B's next `/me` is 401;
  - password reset → both 401;
  - a legacy cookie (built with the old claim set via `TestAppFactory.Services` + `IDataProtectionProvider`, or by signing in and then deleting the row's sid claim through a test endpoint; choose the simplest) gets a row created and still returns `/me` 200;
  - `last_seen_at` moves only after 5+ minutes (`Time.Advance`).
- [X] T012 [P] Create `frontend/src/auth/accountApi.ts`:
  - export `postJson` (move it from `frontend/src/auth/passwordAuthApi.ts` into a shared `frontend/src/auth/http.ts` and re-import it in both files);
  - export the `SessionInfo` and `ReauthRequired` types from data-model.md;
  - export typed result helpers matching the contract.
  
  Keep `passwordAuthApi` behaviour unchanged (its tests must still pass).

**Checkpoint**: All existing tests pass (after the T008 update); sessions are tracked; logout signs out only the current device.

---

## Phase 3: User Story 1 - User menu in the header (Priority: P1) 🎯 MVP

**Goal**: An accessible menu button (name or email) with the email, Dashboard, Account settings and Log out.

**Independent Test**: Quickstart 1–3 and 9.

- [X] T013 [US1] Create `frontend/src/components/UserMenu.tsx` per research R10 and the contract:
  - **Button**: `aria-haspopup="menu"`, `aria-expanded`, `aria-controls`, labelled `name ?? email` (truncated with CSS).
  - **Menu**: `role="menu"`, with an email line (`role="presentation"`, not focusable) and three `role="menuitem"` elements:
    - `<Link to="/dashboard">Dashboard</Link>`;
    - `<Link to="/settings">Account settings</Link>`;
    - a `<button>` "Log out" that calls `logout()` then `navigate('/')`.
  - **Keys**:
    - Enter, Space or ArrowDown on the button opens and focuses the first item; ArrowUp opens and focuses the last;
    - in the menu, ArrowDown/ArrowUp wrap, Home/End jump, Escape closes and refocuses the button, Tab closes.
  - **Mouse**: `mousedown` outside closes; choosing an item closes.
  - Styles go in `frontend/src/App.css` (`.user-menu`, `.user-menu__list` positioned under the button, right-aligned).
- [X] T014 [US1] In `frontend/src/components/Header.tsx`, render `<UserMenu />` when signed in, replacing the "Hi {email}" span and the Logout button. Keep the logout-error alert next to it, and keep the Login button and loading placeholder unchanged (FR-001, US1-5)
- [X] T015 [P] [US1] Create `frontend/src/components/UserMenu.test.tsx` (`MemoryRouter`; mock `useAuth`):
  - the label uses the name, falling back to the email;
  - a click opens it and the email plus 3 items are visible;
  - Enter/Space/ArrowDown open with the first item focused, ArrowUp opens with the last focused;
  - ArrowDown wraps from last to first, Home/End work;
  - Escape closes and the button has focus;
  - an outside `mousedown` closes it;
  - "Account settings" navigates to `/settings`;
  - "Log out" calls `logout` and ends at `/`.
  
  Update `frontend/src/components/Header.test.tsx`: the signed-in tests now look for the menu button instead of "Hi …"/Logout, and the logout test goes through the menu.

**Checkpoint**: Quickstart 1–3 pass; quickstart 9 passes with Phase 2.

---

## Phase 4: User Story 2 - Change my display name (Priority: P1)

**Goal**: `/settings` with Profile (the email read-only, the name editable) and read-only Sign-in methods.

**Independent Test**: Quickstart 4–6 and 20.

- [X] T016 [P] [US2] Create `backend/OAuthLearn.Api/Data/AccountSecurityService.cs` (scoped; `AppDbContext`, `PasswordHashing`, `CommonPasswords`, `SessionStore`, `AttemptLimiter`, `TimeProvider`, `ILogger`) with `Task<AccountResult> UpdateProfileAsync(Guid userId, string? displayName, ct)`:
  - trim the value; an empty or whitespace-only value becomes null;
  - more than 100 characters → `Invalid("displayName","display_name_too_long")`;
  - save, and log "Profile updated. UserId=…" without the name.
  
  Register it in `Program.cs`
- [X] T017 [US2] Add `POST /api/account/profile` (`ProfileRequest(string? DisplayName)`) to `backend/OAuthLearn.Api/Auth/AccountEndpoints.cs`, with `RequireAuthorization` and `RequireFetchHeaderFilter`:
  - call the service;
  - on success, call `SessionIssuer.ReissueAsync(ctx, user, sid, authTime)` (from the claims) so `/me` returns the new name, and return `200` with the account summary;
  - on `400`, return the errors.
  
  Depends on T007, T016 (FR-005)
- [X] T018 [P] [US2] Add profile tests in `backend/OAuthLearn.Api.Tests/AccountSecurityTests.cs`:
  - save "New Name" → 200, and `/me.name == "New Name"` on the same client without signing in again;
  - "   " → the name becomes null and `/me.name` is null;
  - 101 characters → 400 `display_name_too_long`;
  - without the CSRF header → 400;
  - without a session → 401;
  - a later Google upsert for that user keeps "New Name" (R8).
- [X] T019 [US2] Create `frontend/src/pages/SettingsPage.tsx`:
  - it loads `fetchAccount()` (as the Dashboard does; one fetch, a retry on error) and renders `<ProfileSection>`, `<PasswordSection>` (US3; a placeholder until then), `<SignInMethodsSection>` and `<SessionsSection>` (US4; a placeholder until then);
  - add the route `/settings` → `<RequireSignedIn><SettingsPage/></RequireSignedIn>` in `frontend/src/App.tsx`.
  
  Also create:
  - `frontend/src/components/settings/ProfileSection.tsx`:
    - Email as text + "Your email can't be changed";
    - a Display name `Field`, `maxLength=100`, with client validation;
    - "Save" → `updateProfile()`: on success show `role="status"` "Profile updated.", call `refresh()` from `useAuth` so the header updates, and update the page's account state; on a field error show it under the field.
  - `frontend/src/components/settings/SignInMethodsSection.tsx`: read-only rows "Google – Connected/Not connected" and "Password – Set/Not set" from `account.methods` (FR-020).
  
  Add `updateProfile` to `frontend/src/auth/accountApi.ts`. Styles go in `frontend/src/App.css` (`.settings` with stacked cards).
- [X] T020 [P] [US2] Create `frontend/src/pages/SettingsPage.test.tsx` (mock `accountApi`, `authApi.fetchAccount`, `useAuth`):
  - the email is not an input and "Your email can't be changed" is shown;
  - saving calls `updateProfile` with the trimmed value, then shows "Profile updated." and calls `refresh`;
  - 101 characters show the error and make no call;
  - the sign-in methods for `['google']` → Google Connected, Password Not set.
  
  Add to `frontend/src/routing/guards.test.tsx`: signed out at `/settings` → Home.

**Checkpoint**: Quickstart 4–6 and 20 pass.

---

## Phase 5: User Story 3 - Change or set my password, with re-authentication (Priority: P1)

**Goal**: Change the password (current password required; other devices signed out) or set one (after authentication within the last 10 min, or a password/Google re-auth).

**Independent Test**: Quickstart 10–14.

- [X] T021 [US3] Add to `backend/OAuthLearn.Api/Data/AccountSecurityService.cs`:
  - **`ChangePasswordAsync(userId, sid, current, newPw, ip, ct)`**:
    1. No active hash → `Invalid("currentPassword","no_password")`.
    2. Check the throttle as sign-in does (`IsLockedOutAsync` on `signin_fail_email` for the user's normalized email, and on `signin_fail_ip`) → `TooManyAttempts`.
    3. Verify the current password; a failure records both buckets and returns `Invalid("currentPassword","current_password_incorrect")`.
    4. Validate the new password with `PasswordPolicy` → `Invalid("newPassword", code)`.
    5. On success: set the active hash, clear pending, `SessionVersion++`, `RevokeAllExceptAsync(userId, sid)`, `MarkAuthenticatedAsync(sid)`, clear the email failures, log it, and return `Ok(user)`.
  - **`SetPasswordAsync(userId, sid, newPw, ct)`**:
    1. An active hash exists → `Invalid("newPassword","password_already_set")`.
    2. The session isn't recently authenticated → a new `AccountStatus.ReauthRequired`.
    3. Validate the new password.
    4. Set the active hash, clear pending, `EmailVerifiedAt ??= now`, log it.
  - **`ReauthWithPasswordAsync(userId, sid, password, ip, ct)`**: the same throttle; one hash check against the active hash or the dummy hash; a failure records both buckets → `Invalid("password","current_password_incorrect")`; success → `MarkAuthenticatedAsync(sid)` and clear the email failures.
  - **`CompleteGoogleReauthAsync(Guid userId, Guid sid, string googleSub, ct)`**: the user's `GoogleSubject == googleSub` → `MarkAuthenticatedAsync(sid)`; otherwise throw `SignInRejectedException("reauth_mismatch")`.
  
  (FR-006–FR-012, research R4–R6)
- [X] T022 [US3] Add to `backend/OAuthLearn.Api/Auth/AccountEndpoints.cs` (all `RequireAuthorization` + `RequireFetchHeaderFilter`):
  - `POST /api/account/password/change` → `204`, after `SessionIssuer.ReissueAsync` so this device survives the version bump;
  - `POST /api/account/password/set` → `204`, or `403 { code: "reauth_required", methods: ["google"] }`;
  - `POST /api/account/reauth/password` → `204`.
  
  Map `400`/`429` per the contract. Depends on T021
- [X] T023 [US3] Google re-auth (research R6):
  - In `backend/OAuthLearn.Api/Auth/AuthEndpoints.cs`, add `GET /api/auth/reauth/google` (`RequireAuthorization`). It challenges Google with `AuthenticationProperties { RedirectUri = "/api/auth/popup-complete?result=reauth_ok", Items = { ["purpose"]="reauth", ["uid"]=userId, ["sid"]=sid } }`.
  - In `GoogleAuthEvents` and the `Program.cs` Google options, add:
    - `OnRedirectToAuthorizationEndpoint`: append `&prompt=select_account&max_age=0` when `purpose == reauth`, then `Response.Redirect`;
    - an `OnCreatingTicket` branch: when `purpose == reauth`, call `AccountSecurityService.CompleteGoogleReauthAsync(uid, sid, sub)` and **skip** the upsert and session creation;
    - `OnTicketReceived`: when `purpose == reauth`, `Response.Redirect("/api/auth/popup-complete?result=reauth_ok")` and `HandleResponse()` (no sign-in).
  - Map `SignInRejectedException("reauth_mismatch")` in `OnRemoteFailure` to `popup-complete?error=reauth_mismatch`.
  - Extend `/api/auth/popup-complete` to accept `result=reauth_ok` and `error=reauth_mismatch`, mapping them to `auth-complete.html?result=reauth_ok|reauth_mismatch`, with all other values unchanged.
  - Extend the whitelist in `frontend/public/auth-complete.js` to `success`, `access_denied`, `signin_failed`, `reauth_ok`, `reauth_mismatch`. `reauth_ok` posts `{ type: 'oauth-result', success: true, reauth: true }`; `reauth_mismatch` posts `{ success: false, error: 'reauth_mismatch' }`.
- [X] T024 [P] [US3] Add password and re-auth tests to `backend/OAuthLearn.Api.Tests/AccountSecurityTests.cs`:
  - **change**: correct → 204; a second client signed in to the same account gets 401; this client's `/me` still gives 200; the new password signs in and the old one doesn't;
  - **change**: wrong current → 400 `current_password_incorrect`; 5 wrong → 429, and sign-in is then locked too (shared bucket);
  - **change**: new = common → 400 `password_common`; a Google-only account → 400 `no_password`;
  - **set** (Google-only via `X-Test-User` is not possible, because it needs a real `sid`): create the Google user, then sign it in through a test-only path: `SessionIssuer.SignInAsync` inside a scoped test endpoint registered only in `TestAppFactory` (`/test/sign-in-as/{userId}`, POST, Testing env). Then:
    - within 10 min → 204, and password sign-in now works;
    - after `Time.Advance(11 min)` → 403 `reauth_required` with methods `["google"]`;
    - with an active password → 400 `password_already_set`;
  - **re-auth**: an account with a password after `Time.Advance(11 min)`: `POST reauth/password` wrong → 400, correct → 204, then `/api/account/password/change` still needs the current password;
  - **`CompleteGoogleReauthAsync`**: the same sub → `last_auth_at` updated; another sub → `SignInRejectedException("reauth_mismatch")`;
  - `GET /api/auth/reauth/google` → 302 to Google with `max_age=0` and `prompt=select_account` in the query;
  - `popup-complete?result=reauth_ok` → `auth-complete.html?result=reauth_ok`; `?error=reauth_mismatch` → `result=reauth_mismatch`.
- [X] T025 [P] [US3] In `frontend/src/auth/loginPopup.ts`:
  - `openLoginPopup(onPopupClosed, url = default login URL)`;
  - `LoginResult` gains `{ success: true; reauth?: boolean }` and the error `'reauth_mismatch'`;
  - `parseResult` maps them.
  
  Add `changePassword`, `setPassword`, `reauthWithPassword` and `reauthWithGoogle()` to `frontend/src/auth/accountApi.ts`. `reauthWithGoogle()` opens the popup at `${VITE_API_ORIGIN}/api/auth/reauth/google` and resolves `ok` on `success && reauth`, or `mismatch`, `cancelled` or `blocked`. Update `frontend/src/auth/loginPopup.test.ts` for the new codes and the URL parameter.
- [X] T026 [US3] Create `frontend/src/components/settings/ReauthPanel.tsx`, with props `methods` and `onConfirmed`:
  - heading "Confirm it's you" and "For your security, please confirm your identity to continue.";
  - `'password'` → a `PasswordInput` "Current password" + "Confirm" (calls `reauthWithPassword`, with field and 429 errors);
  - `'google'` → a "Continue with Google" button (calls `reauthWithGoogle`; mismatch → "That Google account isn't the one linked to this account."; cancelled → "Confirmation was cancelled.").
  
  Create `frontend/src/components/settings/PasswordSection.tsx`:
  - if `account.methods` includes `'password'` → a **Change password** form (Current password, New password with the 002 hint, Confirm new password; client validation with `validateNewPassword` + match) → `changePassword` → "Password changed. Other devices have been signed out.";
  - otherwise → a **Set a password** form (New ×2) → `setPassword`; on `reauth_required`, show `ReauthPanel` with the returned methods, then retry once automatically → "Password set. You can now sign in with your email and password." and tell the page to refetch the account.
  
  Map server field codes through `fieldMessage` (add `current_password_incorrect`: "Your current password is incorrect.", `no_password`, `password_already_set` to `frontend/src/components/auth/formMessages.ts`).
- [X] T027 [P] [US3] Create `frontend/src/components/settings/PasswordSection.test.tsx` (mock `accountApi`):
  - the change form is shown for a password account and the set form for a Google-only one;
  - change success message; a wrong current password shows the field error;
  - a mismatched confirmation blocks submit;
  - set → `reauth_required` shows the Confirm-it's-you panel with Google, and a Google `ok` retries `setPassword` and shows success;
  - Google mismatch shows the message and makes no retry;
  - a password re-auth wrong → field error.

**Checkpoint**: Quickstart 10–14 pass.

---

## Phase 6: User Story 4 - See and end my active sessions (Priority: P2)

**Goal**: "Where you're signed in" with per-device and all-others sign-out.

**Independent Test**: Quickstart 7–9.

- [X] T028 [US4] Add to `backend/OAuthLearn.Api/Auth/AccountEndpoints.cs` (`RequireAuthorization`; POSTs with `RequireFetchHeaderFilter`):
  - `GET /api/account/sessions` → `SessionStore.ListValidAsync`, projected to `{ id, device, ipMasked, createdAt, lastSeenAt, current }` (current = the claim `sid`), with the current one first and the rest by `lastSeenAt` descending;
  - `POST /api/account/sessions/{id:guid}/revoke` → `400 { errors: { id: "use_logout" } }` for the current one, `404` when it isn't this user's valid session, else `204`;
  - `POST /api/account/sessions/revoke-others` → `RevokeAllExceptAsync(userId, sid)`, `SessionVersion++`, `SessionIssuer.ReissueAsync`, `204`.
  
  Log the session-ended events (FR-022). Depends on Phase 2
- [X] T029 [P] [US4] Add to `backend/OAuthLearn.Api.Tests/SessionsTests.cs`:
  - A and B signed in with different `User-Agent` headers → the list from A has 2 rows, A first with `current: true` and device labels set, `ipMasked` like `10.x.y.x`;
  - A revokes B → 204, B's `/me` is 401, and A's list has 1 row;
  - revoking A's own id → 400 `use_logout`; a random id → 404; another user's session id → 404;
  - revoke-others with A, B and C → only A remains and B and C are 401, while A's `/me` is 200 (cookie re-issued);
  - a session idle for more than 60 min (`Time.Advance`) isn't listed.
- [X] T030 [US4] Create `frontend/src/components/settings/SessionsSection.tsx`:
  - it fetches `listSessions()` on mount;
  - rows show the device ("Unknown device" when null), a "This device" badge, ipMasked ("Unknown" when null), "Signed in {toLocaleString}" and "Last active {toLocaleString}";
  - "Sign out" on non-current rows → `revokeSession(id)`, removing the row on success;
  - "Sign out all other devices" (shown only when there are more than 1 row) → `revokeOtherSessions()`, then reload the list;
  - errors → "Couldn't update your sessions. Please try again."
  
  Add `listSessions`, `revokeSession` and `revokeOtherSessions` to `frontend/src/auth/accountApi.ts`, and render the section in `SettingsPage`.
- [X] T031 [P] [US4] Create `frontend/src/components/settings/SessionsSection.test.tsx` (mock `accountApi`):
  - the current row is first with "This device" and has no Sign out button;
  - Sign out removes that row;
  - the all-others button is hidden with 1 row and calls the API then reloads with more than 1;
  - a null device shows "Unknown device".

**Checkpoint**: Quickstart 7–9 pass.

---

## Phase 7: User Story 5 - Warn me before my session ends (Priority: P2)

**Goal**: A 2-minute warning dialog on any signed-in page, with "Stay signed in" (idle limit) or "Sign out"/"OK" (absolute limit).

**Independent Test**: Quickstart 15–18.

- [X] T032 [US5] Add `POST /api/auth/session/extend` (`RequireAuthorization` + `RequireFetchHeaderFilter`) to `backend/OAuthLearn.Api/Auth/AuthEndpoints.cs`:
  - load the user, then `SessionIssuer.ReissueAsync(ctx, user, sid, authTime)` (research R7);
  - return `200` with the same body as `/me`, with timing computed for the **new** ticket: `IssuedUtc = now`, `ExpiresUtc = now + 60 min`, and the absolute limit unchanged.
  
  Legacy cookie without a `sid` → `400`.
- [X] T033 [P] [US5] Add to `backend/OAuthLearn.Api.Tests/SessionTimingTests.cs`:
  - after `Time.Advance(50 min)`, extend → `idleSecondsLeft` is about 3600, and the next `/me` agrees;
  - `absoluteSecondsLeft` is unchanged by extend;
  - without the CSRF header → 400;
  - after 7 h 59 min (kept alive), extend → the absolute limit is still about 60 s (it can't pass 8 h).
- [X] T034 [US5] Add `extendSession(): Promise<boolean>` to `frontend/src/auth/AuthContext.tsx` and the `AuthContextValue` in `frontend/src/auth/useAuth.ts`:
  - `accountApi.extendSession()` → on 200, store the returned user and session like `refresh()` does (a new `receivedAt`);
  - on failure → `refresh()`.
  
  Update the test mocks that build `AuthContextValue` objects (add `extendSession: vi.fn()`).
- [X] T035 [US5] Create `frontend/src/components/SessionExpiryWarning.tsx` and mount it once in `frontend/src/App.tsx` after `<main>`:
  - It reads `state.session` when signed in and ticks every 1 s with the same maths as `SessionCountdown` (extract `secondsLeft`/`formatDuration` to `frontend/src/components/sessionTime.ts` and reuse them in both).
  - When `min(idle, absolute) ≤ 120 s` and it hasn't been dismissed for this `receivedAt`, it shows a `role="alertdialog"` modal titled "You'll be signed out in m:ss".
    - **Idle ends first**: "Stay signed in" (primary, autofocus) → `extendSession()`, then close; and "Sign out" → `logout()` + `navigate('/')`.
    - **Absolute ends first**: the text "Your session can't be extended any further." with "Sign out" and "OK" (OK dismisses until the next `receivedAt`).
  - It makes **no** requests unless a button is clicked (FR-019).
  
  Styles reuse `.modal-backdrop`/`.modal`.
- [X] T036 [P] [US5] Create `frontend/src/components/SessionExpiryWarning.test.tsx` (fake timers + a mocked `performance.now`, as in `SessionCountdown.test.tsx`):
  - it's hidden at 3 min left and appears at 2:00, counting down to 1:59;
  - "Stay signed in" calls `extendSession` once and hides it;
  - the absolute-first case shows the text + Sign out + OK, with no Stay button;
  - OK hides it;
  - "Sign out" calls `logout`;
  - `fetch` is never called while it's showing.
  
  Confirm `frontend/src/components/SessionCountdown.test.tsx` still passes after the helper extraction.

**Checkpoint**: Quickstart 15–18 pass.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T037 [P] Update `README.md`:
  - a "Account settings" section (the menu, display name, password change/set with "confirm it's you", sessions, the expiry warning);
  - in "Session security": per-device sessions, logout = this device, password change/reset effects, the 10-minute rule, and Google re-auth with `max_age=0`;
  - note the R8 change (Google no longer overwrites your chosen name).
- [X] T038 Run `dotnet build`, `dotnet test OAuthLearn.sln` (with `TEST_PG_CONNECTION`), and `npm test`, `npx tsc -b`, `npm run lint`, `npm run build` in `frontend/`. Also scan `frontend/dist` for `GOCSPX-`. Fix any failures or warnings
- [ ] T039 Run the 21 manual scenarios in `specs/004-account-settings/quickstart.md` with two browsers and note the results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2: sessions)** → stories
- **US1 (Phase 3)**: after Phase 2 (logout semantics); frontend only
- **US2 (Phase 4)**: after Phase 2 (`ReissueAsync`); independent of US1
- **US3 (Phase 5)**: after Phase 2 and US2 (it uses `SettingsPage` and `AccountSecurityService`)
- **US4 (Phase 6)**: after Phase 2 and US2 (the `SettingsPage` shell)
- **US5 (Phase 7)**: after Phase 2 (`ReissueAsync`); independent of US1–US4
- **Polish (Phase 8)**: after the stories

### Key Task Dependencies

- T002 → T003; T005 + T006 → T007 → T017, T022, T028, T032
- T006 → T008, T009, T010
- T016 → T017, T021 → T022 → T024; T021 → T023
- T012 → T019, T025, T030; T025 → T026 → T027
- T034 → T035 → T036

### Parallel Opportunities

- Phase 2: T004 and T012 alongside the data/session work; T011 after T007–T010
- US1 (frontend) in parallel with US2's backend (T016–T018)
- US5 in parallel with US3/US4 once Phase 2 is done
- Test tasks marked [P] within each story

---

## Parallel Example: after Phase 2

```text
Task: "T013–T015 [US1] UserMenu (frontend)"
Task: "T016–T018 [US2] profile service + endpoint + tests (backend)"
Task: "T032–T033 [US5] extend endpoint + tests (backend)"
```

---

## Implementation Strategy

### MVP First

1. Phase 1 + Phase 2 (per-device sessions): check that the existing suites stay green and logout is now per device.
2. US1 (menu) + US2 (display name): **validate with quickstart 1–6 and 20.** This is the part the user asked for most directly.
3. US3 (password + re-auth), then US4 (sessions list), then US5 (warning), then Polish.

### Notes

- Every new POST uses `RequireFetchHeaderFilter`; re-auth failures share the sign-in throttle.
- Never log names, passwords, tokens or raw IPs; store only the masked IP.
- The expiry dialog and countdowns never call the server on their own.
