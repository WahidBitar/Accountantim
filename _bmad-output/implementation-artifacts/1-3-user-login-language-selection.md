# Story 1.3: User login with language selection

Status: ready-for-dev

## Story

As a **registered user**,
I want **to log in with my email + password and choose Arabic or English**,
so that **I can access my Glance in my preferred language (FR2, FR4)**.

**Covers requirements:** FR2 (user login), FR4 (language preference), NFR-S3 (session tokens), NFR-S5 (rate limiting).

**Why this story now:** Per the reorder, 1.3 follows 1.2 — it reuses the Auth0 SDK wrapper, callback route, and refresh proxy from the registration story.

## Acceptance Criteria

### AC1 — Redirect to Auth0 hosted login

**Given** an unauthenticated user on the landing page or on any route requiring auth
**When** the user clicks "Log in" (localized: "تسجيل الدخول" / "Log in")
**Then** the browser redirects to `https://auth.faktuboh.com/u/login`
**And** the redirect URL includes `redirect_uri=https://app.faktuboh.com/auth/callback`, `audience=https://api.faktuboh.com`, `scope=openid profile email offline_access`, `ui_locales=ar|en`
**And** if the user has a stored `faktuboh.lang`, it is passed as `ui_locales` so the hosted login page matches

### AC2 — Login success lands on stored-preference locale

**Given** a user with stored `locale` in Auth0 user_metadata (set during signup per Story 1.2 AC5)
**When** login succeeds + tokens are exchanged
**Then** the callback handler reads the `https://faktuboh.com/locale` ID-token claim
**And** calls `langBootstrap.setLang(claim)` to update `<html lang dir>` + localStorage + Transloco
**And** the Glance renders at `/dashboard` in the user's stored preference

### AC3 — In-app language toggle persists to profile

**Given** an authenticated user on any route
**When** the user activates the top-app-bar language toggle (from Story 1.1)
**Then** the toggle flips direction ≤ 200ms (unchanged from Story 1.1)
**And** the change persists to the backend via `PATCH /v1/users/me { locale: 'ar'|'en' }` (optimistic UI + server-authoritative)
**And** on next login, the updated preference is fetched via the ID-token claim (Auth0 user_metadata is the source of truth; the backend syncs to Auth0 via the Management API inside the PATCH handler)

### AC4 — Session token security per NFR-S3

**Given** tokens issued by Auth0
**When** the session is active
**Then** the access token lives in memory only (never localStorage/sessionStorage)
**And** the refresh token lives in an httpOnly, Secure, SameSite=Strict cookie at `app.faktuboh.com`
**And** the session idle timeout is 30 minutes — no API activity for 30 min → refresh token exchange fails → user redirected to login
**And** each successful API call extends the session by resetting the refresh-token proxy's `last_seen_at` timestamp

### AC5 — CSRF protection on state-changing requests

**Given** the CSRF double-submit cookie pattern (established in Story 1.2)
**When** the Angular app makes a `POST/PATCH/PUT/DELETE` request
**Then** the HTTP interceptor reads the `XSRF-TOKEN` cookie value and echoes it in the `X-XSRF-TOKEN` header
**And** the backend validates both are present + equal before executing the handler
**And** `GET/HEAD/OPTIONS` requests are exempt from this check (idempotent-safe)

### AC6 — Rate limiting per NFR-S5

**Given** Auth0 Attack Protection from Story 1.2
**When** 10 failed authentication attempts per 15 minutes per IP
**Then** Auth0 rate-limits further attempts, returning HTTP 429 from the hosted login
**And** the user sees a localized "Too many attempts; try again in X minutes" message on the Auth0 hosted page
**And** successful login before the limit resets the counter

### AC7 — Logout flow

**Given** an authenticated user on any route
**When** the user clicks "Log out" in the app shell menu
**Then** the app calls `POST /v1/auth/logout` (clears refresh-token httpOnly cookie)
**And** calls Auth0 SDK `logout({ returnTo: 'https://app.faktuboh.com' })`
**And** Auth0 clears its session cookie + redirects back to the landing page
**And** localStorage `faktuboh.lang` is preserved across logout (user convenience)
**And** in-memory access token is cleared

### AC8 — Telemetry event `user_login`

**Given** AR-047 telemetry primitive
**When** login succeeds
**Then** `user_login` event emits with payload `{ opaque_user_id, locale }`
**And** no PII fields (email, name, IP) appear
**And** NetArchTest asserts the schema

### AC9 — Failed login error surfacing

**Given** Auth0 returns a login error (bad credentials, account locked, MFA required)
**When** the callback receives an error query param
**Then** the error is mapped to an ErrorCatalog code (`faktuboh.auth.invalid_credentials`, `faktuboh.auth.account_locked`, `faktuboh.auth.mfa_required`)
**And** rendered as `<p-message severity="error">` on the login-redirect-origin route
**And** the error message is localized via Transloco

### AC10 — Return-to path preserved

**Given** an unauthenticated user attempts to access `/dashboard/some/deep/route`
**When** the auth guard triggers a login redirect
**Then** the current path is stored in `sessionStorage.faktuboh.return_to`
**And** after successful login, the callback handler redirects to the stored path (not `/dashboard`)
**And** `sessionStorage.faktuboh.return_to` is cleared after consumption

## Tasks / Subtasks

### Frontend — login + session

- [ ] **T1 (AC: 1, 10)** — Add login entry to `apps/owner-workspace/src/app/features/auth/`
  - [ ] `login.component.ts` — same thin redirect component pattern as signup (from Story 1.2)
  - [ ] Reads `faktuboh.lang` + `sessionStorage.faktuboh.return_to`; computes redirect URL
  - [ ] Calls `authService.loginWithRedirect({ authorizationParams: { ui_locales, redirect_uri } })`
- [ ] **T2 (AC: 2)** — Update callback handler (from Story 1.2)
  - [ ] After token exchange, read `https://faktuboh.com/locale` claim from ID token
  - [ ] Call `langBootstrap.setLang(claim)` before navigating away
  - [ ] Consume + clear `sessionStorage.faktuboh.return_to`, redirect there (fallback `/dashboard`)
- [ ] **T3 (AC: 10)** — Create `authGuard` at `libs/core/src/auth/auth.guard.ts`
  - [ ] `CanActivateFn`: if `authService.isAuthenticated$` is false, store `router.url` in sessionStorage + redirect to `/login`
  - [ ] Apply to all `/dashboard`, `/profile`, `/contacts/*` routes in Story 1.5+
- [ ] **T4 (AC: 3)** — Language toggle persistence
  - [ ] `LangBootstrapService.setLang()` — after local update, if authenticated, call `PATCH /v1/users/me { locale }` (fire-and-forget; log errors but don't block UX)
  - [ ] `ProfileService.updateLocale(locale)` wraps the HTTP call
- [ ] **T5 (AC: 7)** — Logout flow
  - [ ] App-shell menu item calls `logout()` on AuthService
  - [ ] Wrapper calls backend `POST /v1/auth/logout` (clears httpOnly cookie) + Auth0 `logout({ returnTo })`
  - [ ] Clear in-memory token; preserve `faktuboh.lang`

### Backend — auth proxy extensions

- [ ] **T6 (AC: 4)** — Refresh-token proxy idle timeout
  - [ ] The refresh proxy from Story 1.2 tracks `last_seen_at` per refresh-token-jti
  - [ ] If `now - last_seen_at > 30 min`, reject the refresh → client redirects to login
  - [ ] Each successful refresh resets `last_seen_at`
- [ ] **T7 (AC: 3)** — `PATCH /v1/users/me` endpoint handles locale updates
  - [ ] Persists `locale` to the backend `User` aggregate
  - [ ] Also syncs to Auth0 user_metadata via Management API (idempotent — update only if changed)
  - [ ] Updates the ID-token claim on next login via the Post-Login Action
- [ ] **T8 (AC: 5)** — CSRF double-submit cookie validation
  - [ ] Middleware in `Faktuboh.Api` reads `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` header on state-changing requests
  - [ ] Rejects with 403 RFC 9457 `faktuboh.auth.csrf_failed` if missing or unequal
  - [ ] Exempts `GET/HEAD/OPTIONS`

### Error surfacing

- [ ] **T9 (AC: 9)** — ErrorCatalog entries for auth errors
  - [ ] `faktuboh.auth.invalid_credentials` (401)
  - [ ] `faktuboh.auth.account_locked` (423)
  - [ ] `faktuboh.auth.mfa_required` (401 with `WWW-Authenticate: MFA`)
  - [ ] `faktuboh.auth.csrf_failed` (403)
  - [ ] Each code has a Transloco key: `errors.auth.invalid_credentials` etc., bilingual

### Telemetry

- [ ] **T10 (AC: 8)** — Emit `user_login` event
  - [ ] Callback handler emits after successful code exchange + claim read
  - [ ] Payload: `{ opaque_user_id, locale }`
  - [ ] NetArchTest schema assertion

### Tests

- [ ] **T11 (AC: 1-10)** — Playwright E2E in `apps/owner-workspace/e2e/auth/login.spec.ts`
  - [ ] Happy path: login → redirect to dashboard in stored locale
  - [ ] Return-to path: visit `/profile` unauthenticated → login → land on `/profile`
  - [ ] Logout: click logout → Auth0 redirect → landing page
  - [ ] Failed login: bad credentials → error message on Auth0 hosted page (test tenant)
  - [ ] In-app lang toggle persists: toggle → reload → preference preserved
  - [ ] axe-core zero violations + LTR/RTL parity
- [ ] **T12 (AC: 3, 4)** — Unit tests for LangBootstrapService + AuthService wrappers
  - [ ] Persistence path calls `PATCH /v1/users/me` only when authenticated
  - [ ] Token storage honors memory-only + httpOnly cookie contract
- [ ] **T13 (AC: 5)** — Integration test for CSRF middleware
  - [ ] POST without XSRF header → 403 with correct RFC 9457 code
  - [ ] POST with matching cookie + header → success
  - [ ] GET without CSRF → success (exempt)

## Dev Notes

### Critical guardrails (do not violate)

1. **No custom login UI in the Angular app.** Reuses Auth0 hosted flow + callback from Story 1.2.
2. **Locale claim from ID token is authoritative on login.** Client-side localStorage is a fallback; the server-managed user_metadata via Auth0 is the source of truth.
3. **CSRF token enforcement is non-optional on state-changing requests.** Middleware rejects before handler execution.
4. **Session idle timeout = 30 minutes.** Refresh proxy enforces this — no client-side timer; server is authoritative.
5. **`faktuboh.lang` persists across logout.** User convenience; does not leak auth state.
6. **`user_login` telemetry is PII-safe.** Same rules as `user_signup` from Story 1.2.
7. **`return_to` uses sessionStorage** — not localStorage — so closing the tab clears it.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/features/auth/
│   ├── login.component.ts                          # NEW
│   ├── login.component.spec.ts                     # NEW
│   └── callback.component.ts                       # MODIFY (locale claim read + return_to consumption)
└── e2e/auth/
    └── login.spec.ts                               # NEW

libs/core/src/auth/
├── auth.guard.ts                                   # NEW
├── auth.service.ts                                 # MODIFY (logout + locale sync)
└── auth.guard.spec.ts                              # NEW

libs/core/src/i18n/
└── lang-bootstrap.service.ts                       # MODIFY (persist to backend on setLang)

backend/src/Faktuboh.Api/
├── Middleware/
│   └── CsrfDoubleSubmitCookieMiddleware.cs         # NEW
├── Slices/Auth/Logout/
│   ├── LogoutHandler.cs                            # NEW
│   └── LogoutResponse.cs                           # NEW
└── Slices/Auth/Refresh/
    └── RefreshHandler.cs                           # MODIFY (idle timeout)

backend/src/Faktuboh.Api/Slices/Users/Patch/
├── PatchUserRequest.cs                             # NEW (locale field)
├── PatchUserHandler.cs                             # NEW (syncs to Auth0 Management API)
└── PatchUserRequestValidator.cs                    # NEW

backend/src/Faktuboh.Application/Errors/
└── ErrorCatalog.cs                                 # MODIFY (add auth error codes)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | 70% | Vitest (FE) + NSubstitute (BE) |
| Integration | CSRF middleware, refresh proxy idle | WebApplicationFactory + Testcontainers Postgres |
| E2E | Happy + return-to + logout + failed + lang persist | Playwright + Auth0 test tenant |
| Manual | MFA challenge flow (if enabled) | Per-release checklist |

**Forbidden:**
- Storing tokens outside memory + httpOnly cookie
- Custom login UI
- Bypassing CSRF on state-changing requests
- Trusting client-side locale preference over ID-token claim

### Project Structure Notes

This story extends Story 1.2's Auth0 integration. No new features; reuses signup patterns. Adds `authGuard`, logout flow, CSRF middleware, and locale-persistence plumbing.

**Detected variances:**
1. Auth0 Management API for user_metadata sync — rate-limited (1000 req/min on free tier); batch syncs if many locale toggles occur. MVP traffic will not hit this.
2. `return_to` URL validation — reject URLs with absolute origins (open-redirect prevention); accept only relative paths starting with `/`.

### Architecture compliance

- **ADR-021** (Auth0) — canonical
- **§4.3 D4.5** — same as Story 1.2
- **FR2, FR4** — login + language preference
- **NFR-S3** — session tokens XSS/CSRF-safe + 30 min idle
- **NFR-S5** — rate limit via Auth0 Attack Protection

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Auth SDK | @auth0/auth0-angular | same as Story 1.2 |
| Management API | auth0 (Node) or REST direct | latest stable |

### Testing requirements

- **Unit**: authGuard, lang persistence, logout flow, token-storage invariant
- **Integration**: CSRF middleware, refresh proxy idle timeout, PATCH user locale → Auth0 Management API
- **E2E**: full login/logout/return-to/lang-toggle happy paths in LTR + RTL

### Previous Story Intelligence

**Prerequisites:**
- **Story 1.1** — shell + lang toggle infrastructure
- **Story 1.2** — Auth0 tenant config, SDK wrapper, callback route, refresh proxy, user bootstrap
- **Story 1.10** — a11y gates
- **Story 1.11** — perf gates

**Downstream:**
- Story 1.4 (password reset) reuses the Auth0 SDK wrapper
- Story 1.5 (profile) consumes `PATCH /v1/users/me` extended here
- Story 1.7 (deletion) consumes logout flow
- Story 1.8 (empty Glance) is the `/onboarding` landing after signup; `/dashboard` after login

### Latest Technical Information

**Auth0 Management API (sync user_metadata):**
```csharp
var client = new ManagementApiClient(token, new Uri($"https://{domain}/api/v2"));
await client.Users.UpdateAsync(userId, new UserUpdateRequest
{
    UserMetadata = new { locale = newLocale }
});
```
Use a short-lived M2M access token; cache for ~23h (Management API tokens default 24h TTL).

**CSRF double-submit cookie:**
- Backend issues `XSRF-TOKEN` cookie (readable by JS) on first GET after auth
- Angular HttpClient has built-in `XSRF` support via `HttpClientXsrfModule` — configure header name `X-XSRF-TOKEN` + cookie name `XSRF-TOKEN`
- Backend middleware validates both present + equal on non-safe methods

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.3
- ADR-021 Auth0 — [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- Architecture §4.3 D4.5 — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.3 D4.5
- FR2/FR4, NFR-S3, NFR-S5 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 1.2 (Auth0 scaffold) — [_bmad-output/implementation-artifacts/1-2-user-registration-auth0-hosted-flow.md](./1-2-user-registration-auth0-hosted-flow.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Login fully delegated to Auth0 hosted flow; reuses callback + refresh proxy from Story 1.2
- Locale ID-token claim is authoritative on login
- CSRF double-submit cookie + 30 min idle timeout enforced server-side

### File List

_Populated during implementation — see Source tree section above._
