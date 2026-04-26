# Story 1.2: User registration via Auth0 hosted flow

Status: ready-for-dev

## Story

As a **new visitor**,
I want **to register an account with email + password via Auth0's hosted flow**,
so that **I can access Faktuboh's authenticated experience (FR1)**.

**Covers requirements:** FR1 (user registration), NFR-S2 (password hashing bcrypt cost ≥ 12), NFR-S3 (session tokens XSS/CSRF-safe), AR-047 (telemetry).

**Why this story now:** Per the reorder, 1.2 lands after 1.11 (perf gates) — the shell + components + CI gates are in place, now real auth flows activate. Registration is the first identity touchpoint; login (1.3) + password reset (1.4) build on it.

## Acceptance Criteria

### AC1 — Redirect to Auth0 hosted sign-up

**Given** an unauthenticated user on the landing page `/`
**When** the user clicks "Sign up" (localized: "إنشاء حساب" / "Sign up")
**Then** the browser redirects to `https://auth.faktuboh.com/u/signup` (custom domain per ADR-021)
**And** the redirect URL includes `redirect_uri=https://app.faktuboh.com/auth/callback`, `audience=https://api.faktuboh.com`, `scope=openid profile email offline_access`, `ui_locales=ar|en`
**And** no bespoke registration form exists in `apps/owner-workspace` — all registration UX is Auth0-hosted

### AC2 — Auth0 hosted UI renders in user's active locale

**Given** the user's `faktuboh.lang` localStorage value (from Story 1.1)
**When** the redirect URL is constructed
**Then** `ui_locales=ar` (or `en`) is passed as a query parameter
**And** Auth0 tenant has Arabic + English translations configured for the hosted sign-up page
**And** the form fields, buttons, legal disclaimer, password policy text all render in the active locale
**And** visual QA against Auth0's hosted UI confirms Tajawal (Arabic) / Inter (English) font choice matches the in-app shell (Auth0 custom branding allows font CDN override)

### AC3 — Password policy enforces bcrypt cost ≥ 12 per NFR-S2

**Given** Auth0 tenant configuration
**When** a user registers
**Then** Auth0 stores the password with bcrypt cost factor 12 (configured via Auth0 Dashboard → Authentication → Database → Password Policy)
**And** the minimum password length is ≥ 10 characters
**And** the policy requires: ≥ 1 lowercase, ≥ 1 uppercase, ≥ 1 number, ≥ 1 special character
**And** the Auth0 password dictionary blocks common passwords (Auth0's built-in HIBP check)

### AC4 — Successful registration redirects to `/onboarding` with valid tokens

**Given** the user submits valid registration details on the Auth0 hosted page
**When** registration succeeds
**Then** Auth0 redirects to `https://app.faktuboh.com/auth/callback?code=...&state=...`
**And** the callback route in `apps/owner-workspace` exchanges the code for access + refresh + ID tokens via the Auth0 SDK
**And** the user is redirected to `/onboarding` — the empty Glance landing page (wired to empty state in Story 1.8)
**And** session state is persisted per NFR-S3 (see AC6)

### AC5 — Auth0 Action populates custom claims

**Given** the Auth0 Post-Login Action
**When** a newly registered user logs in for the first time
**Then** custom claims are injected into the ID + access tokens:
- `https://faktuboh.com/tenant_id` — the user's internal tenant identifier
- `https://faktuboh.com/processing_activity_hint` = `"OwnerLedger"` (per architecture §3.5)
- `https://faktuboh.com/locale` = `"ar"` or `"en"` (from user metadata)
**And** the Action creates the initial `User` domain aggregate row in the backend database via a webhook to `POST /v1/users/me/bootstrap` (idempotent — re-runs safely)
**And** the Action logs claim injection to Auth0's log stream (not to app logs)

### AC6 — Session token storage per NFR-S3

**Given** the token exchange succeeds
**When** tokens are stored client-side
**Then** the access token is stored in memory only (never in localStorage/sessionStorage — XSS protection)
**And** the refresh token is stored in an httpOnly, Secure, SameSite=Strict cookie at `app.faktuboh.com` (domain-scoped; no cross-origin leak)
**And** CSRF protection: the backend validates the `SameSite=Strict` cookie plus a double-submit cookie pattern on state-changing requests
**And** session timeout: 30 minutes of inactivity → refresh token fails → user prompted to re-login (auto-extension on API activity)
**And** the Auth0 Angular SDK (`@auth0/auth0-angular`) handles the memory storage; httpOnly cookie is managed by a backend callback-proxy endpoint

### AC7 — Telemetry event `user_signup`

**Given** AR-047 telemetry primitive is wired (Epic 0 Story 0.14)
**When** registration completes successfully (callback → onboarding redirect)
**Then** a `user_signup` event emits to App Insights with payload: `{ opaque_user_id, signup_source: 'hosted_ui', locale: 'ar'|'en' }`
**And** no PII fields appear in the event (no email, name, IP)
**And** NetArchTest asserts PII fields are not in the event schema

### AC8 — Duplicate email registration renders friendly RFC 9457

**Given** a user attempts to register with an email already registered
**When** Auth0 returns an error
**Then** the Auth0 hosted page displays a localized error message (AR/EN)
**And** if the error propagates back to the Angular app (rare — Auth0 handles this before redirect), the callback handler surfaces it as `<p-message severity="error">` via the ErrorCatalog code `faktuboh.auth.email_already_registered` + Transloco key

### AC9 — Legal disclaimer shown on registration form

**Given** the Auth0 hosted sign-up page
**When** the form renders
**Then** the legal disclaimer text (from Story 1.6) is shown as a non-dismissible notice above the submit button
**And** the disclaimer text is the same in both Auth0 hosted UI and in the post-login authenticated shell
**And** a checkbox "I have read and understood" is required to enable submit (AR: "قرأت الشروط وفهمتها")

### AC10 — Rate limiting per NFR-S5

**Given** Auth0 tenant Attack Protection settings
**When** 10 failed authentication attempts occur per 15 minutes per IP
**Then** Auth0 rate-limits further attempts (brute-force protection default)
**And** suspicious-IP protection is enabled (reports to Auth0 dashboard)
**And** no additional app-level rate limiting is required at MVP (Auth0 carries the load)

## Tasks / Subtasks

### Auth0 tenant configuration

- [ ] **T1 (AC: 1, 2, 3, 10)** — Configure Auth0 tenant
  - [ ] Custom domain `auth.faktuboh.com` (verified via DNS CNAME — Epic 0 Story 0.3 should have set this up)
  - [ ] Application: `apps/owner-workspace` → Regular Web Application → Allowed Callback URLs: `https://app.faktuboh.com/auth/callback`, `http://localhost:4200/auth/callback`
  - [ ] Database connection: Username-Password-Authentication → password policy: bcrypt 12, min length 10, complexity enforced
  - [ ] Hosted page templates: customize sign-up page with Tajawal/Inter fonts + Faktuboh Quiet tokens
  - [ ] Universal Login: enable Arabic + English translations
- [ ] **T2 (AC: 5)** — Create Auth0 Post-Login Action
  - [ ] Action script injects custom claims: `tenant_id`, `processing_activity_hint`, `locale`
  - [ ] Action calls `POST /v1/users/me/bootstrap` webhook (idempotent, authenticated via Auth0-signed JWT)
  - [ ] Deploy to Production flow

### Frontend — registration entry + callback

- [ ] **T3 (AC: 1, 2)** — Create `apps/owner-workspace/src/app/features/auth/`
  - [ ] `auth.routes.ts` — routes: `/login`, `/signup`, `/auth/callback`, `/auth/logout`
  - [ ] `signup.component.ts` — a thin component that computes the Auth0 redirect URL + performs `window.location.href = url`
  - [ ] `callback.component.ts` — handles Auth0 code exchange + redirect to `/onboarding`
- [ ] **T4 (AC: 4, 6)** — Wire `@auth0/auth0-angular` SDK
  - [ ] Add to `libs/core/src/auth/` as a thin wrapper (avoid leaking SDK to feature code)
  - [ ] Configure with: domain `auth.faktuboh.com`, clientId from env, audience `https://api.faktuboh.com`, scope `openid profile email offline_access`
  - [ ] Access token stored in memory via SDK default; refresh token via httpOnly cookie (SDK `useRefreshTokensFallback: true` + backend proxy)
- [ ] **T5 (AC: 6)** — Backend refresh-token proxy
  - [ ] `POST /v1/auth/refresh` endpoint in a slim `Auth` slice — exchanges refresh token from httpOnly cookie for a new access token
  - [ ] Sets refreshed cookie with `HttpOnly; Secure; SameSite=Strict; Path=/`
  - [ ] CSRF protection: double-submit cookie pattern — the Angular app reads a `XSRF-TOKEN` cookie + echoes it in `X-XSRF-TOKEN` header

### Frontend — UX polish

- [ ] **T6 (AC: 9)** — Legal disclaimer in Auth0 hosted page
  - [ ] Add localized disclaimer text to Auth0 Universal Login customization (via Auth0 Dashboard → Branding → Universal Login → Custom Text)
  - [ ] Required checkbox enables submit button
- [ ] **T7 (AC: 8)** — RFC 9457 error surfacing
  - [ ] If callback receives an error param, parse it; map Auth0 error codes to ErrorCatalog codes
  - [ ] Register `faktuboh.auth.email_already_registered` in `ErrorCatalog`
  - [ ] Render via `<p-message severity="error">` with Transloco key

### Telemetry

- [ ] **T8 (AC: 7)** — Emit `user_signup` event
  - [ ] Callback handler calls `TelemetryEmitter.emit('user_signup', { opaque_user_id, signup_source: 'hosted_ui', locale })` after successful code exchange
  - [ ] `opaque_user_id` is `sha256(sub_claim_from_token)` — not the raw Auth0 sub
  - [ ] NetArchTest backend asserts no PII in the event schema

### Tests

- [ ] **T9 (AC: 1-10)** — Playwright E2E in `apps/owner-workspace/e2e/auth/signup.spec.ts`
  - [ ] Click "Sign up" → assert redirect URL matches AC1 pattern
  - [ ] Mock Auth0 hosted page with test tenant; complete signup flow; assert redirect to `/onboarding`
  - [ ] Axe-core zero WCAG 2.1 AA violations on `/signup` + `/auth/callback` routes
  - [ ] Run in LTR + RTL (via `testInBothDirections` helper from Story 1.10)
- [ ] **T10 (AC: 4, 6)** — Unit tests for callback handler
  - [ ] Code exchange success → token storage + redirect
  - [ ] Code exchange failure → error surfacing
  - [ ] CSRF double-submit cookie validation — test the HTTP interceptor
- [ ] **T11 (AC: 7)** — Telemetry assertion tests
  - [ ] Integration test mocks App Insights client; asserts event shape (`opaque_user_id` hashed, no PII)
- [ ] **T12 (AC: 3, 10)** — Manual Auth0 dashboard verification (documented in `docs/auth/auth0-config-checklist.md`)
  - [ ] bcrypt cost verified in Auth0 Dashboard
  - [ ] Attack Protection settings verified

## Dev Notes

### Critical guardrails (do not violate)

1. **No bespoke auth UI in the Angular app.** Registration, login, password reset all live in Auth0 hosted flow. The Angular app only renders callback-handling + logout screens.
2. **Access token in memory only.** Never localStorage, never sessionStorage — XSS leak surface. The Auth0 Angular SDK default behavior handles this.
3. **Refresh token via httpOnly cookie only.** Managed by backend proxy; Angular never touches the raw refresh token.
4. **Custom claims use namespaced URIs.** Auth0 requires claims under a namespace (`https://faktuboh.com/*`) — using unnamespaced custom claims is silently stripped.
5. **Auth0 password policy = bcrypt 12, ≥ 10 chars, complexity.** NFR-S2 requires cost ≥ 12. Never lower — adds ~150ms to registration but is the standing fintech hygiene floor.
6. **`processing_activity_hint=OwnerLedger` claim is informational.** The backend re-establishes `ProcessingActivity.OwnerLedger` at every endpoint filter regardless — do not trust the claim as the source of truth for authorization.
7. **`user_signup` telemetry event must be PII-safe.** `opaque_user_id = sha256(sub)`; no email, no name, no IP. NetArchTest enforces.
8. **Legal disclaimer required at signup.** FR7 + regulatory posture. The checkbox "I have read and understood" is non-optional.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/features/auth/
│   ├── auth.routes.ts                               # NEW
│   ├── signup.component.ts                          # NEW
│   ├── signup.component.spec.ts                     # NEW
│   ├── callback.component.ts                        # NEW
│   └── callback.component.spec.ts                   # NEW
└── e2e/auth/
    └── signup.spec.ts                               # NEW

libs/core/src/auth/
├── auth.config.ts                                   # NEW (Auth0 SDK wrapper config)
├── auth.service.ts                                  # NEW (thin wrapper over @auth0/auth0-angular)
├── csrf.interceptor.ts                              # NEW (double-submit cookie)
└── auth.service.spec.ts                             # NEW

backend/src/Faktuboh.Api/Slices/Auth/
├── Auth.Endpoints.cs                                # NEW
├── Refresh/
│   ├── RefreshRequest.cs                            # NEW
│   ├── RefreshResponse.cs                           # NEW
│   └── RefreshHandler.cs                            # NEW
└── Bootstrap/
    ├── BootstrapRequest.cs                          # NEW
    ├── BootstrapResponse.cs                         # NEW
    └── BootstrapHandler.cs                          # NEW (called by Auth0 Action webhook)

docs/auth/
└── auth0-config-checklist.md                        # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | 70% of tests | Vitest + Angular Testing Library; NSubstitute on backend |
| Integration | Token exchange + bootstrap webhook | WebApplicationFactory + Testcontainers Postgres + Auth0 test tenant |
| E2E | Happy path + error path | Playwright with Auth0 test tenant credentials |
| Manual | Auth0 dashboard checklist | Documented in `docs/auth/auth0-config-checklist.md` |

**Forbidden:**
- Storing access or refresh tokens in localStorage/sessionStorage
- Bespoke registration forms in `apps/owner-workspace`
- Un-namespaced Auth0 custom claims
- PII fields in `user_signup` telemetry event

### Project Structure Notes

This story uses Auth0 as the identity provider per ADR-021. The Angular app's only auth surfaces are the redirect-out and callback-in routes. A small backend `Auth` slice handles refresh-token proxying + user-bootstrap webhook from the Auth0 Action.

**Detected variances to reconcile:**
1. Auth0 Action webhook authentication — use a signed JWT (Auth0 → backend) with a shared secret in Key Vault; verify signature in the bootstrap endpoint.
2. Test Auth0 tenant — separate dev tenant with test credentials for E2E. Avoid hitting production Auth0 from CI.
3. Angular SDK `useRefreshTokensFallback: true` — verify this is supported on `@auth0/auth0-angular` version pinned for Angular 21.

### Architecture compliance

- **ADR-021** (Auth0 identity provider) — canonical reference for this story
- **§4.3 D4.5** (Auth0 free tier, custom domain `auth.faktuboh.com`) — all configuration follows this
- **§3.5 Cross-Cutting** — `ProcessingActivity.OwnerLedger` stamped at backend entry; `processing_activity_hint` claim is informational
- **NFR-S2** (password hash bcrypt cost ≥ 12) — Auth0 dashboard config
- **NFR-S3** (session tokens XSS/CSRF-safe) — access token in memory + refresh httpOnly cookie + CSRF double-submit
- **NFR-S5** (rate limit) — Auth0 Attack Protection
- **AR-047** (telemetry primitive) — consumed for `user_signup` event

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Auth SDK | @auth0/auth0-angular | latest stable (Angular 21 compatible) |
| Backend | Microsoft.AspNetCore.Authentication.JwtBearer | 10 |
| Config | Auth0 Management API | latest stable |

### Testing requirements

- **Unit**: callback handler, CSRF interceptor, bootstrap endpoint handler
- **Integration**: backend bootstrap webhook receives Auth0-signed JWT, creates User aggregate row
- **E2E**: Playwright with Auth0 test tenant — happy path + duplicate-email + rate-limit
- **Manual**: Auth0 dashboard config checklist; bcrypt verification; Attack Protection settings

### Previous Story Intelligence

**Prerequisites:**
- **Epic 0 Story 0.3** — Auth0 tenant live, custom domain `auth.faktuboh.com` verified
- **Epic 0 Story 0.14** — AR-047 telemetry primitive
- **Story 1.1** — localStorage `faktuboh.lang` read for `ui_locales` query parameter
- **Story 1.10** — a11y gates active; Auth0 hosted page passes Lighthouse a11y from the outside
- **Story 1.11** — perf budget includes the Auth0 SDK in the 250 KB gzipped envelope

**Downstream:**
- Story 1.3 (login) reuses the callback + token exchange + refresh proxy
- Story 1.4 (password reset) triggers Auth0 password-reset flow from the same SDK wrapper
- Story 1.5 (profile) reads tokens from the auth service
- Story 1.8 (empty Glance) is the `/onboarding` landing surface after successful signup

### Latest Technical Information

**@auth0/auth0-angular v2+ on Angular 21:**
- `provideAuth0({ domain, clientId, authorizationParams: { audience, scope, redirect_uri } })` in app.config
- `AuthService.loginWithRedirect({ authorizationParams: { screen_hint: 'signup', ui_locales: 'ar' } })` triggers redirect to hosted sign-up
- `AuthService.handleRedirectCallback()` in the callback route
- Use `@auth0/auth0-angular`'s `AuthHttpInterceptor` for attaching access tokens to outgoing API calls

**Auth0 Post-Login Action:**
```js
exports.onExecutePostLogin = async (event, api) => {
  const ns = 'https://faktuboh.com/';
  api.idToken.setCustomClaim(ns + 'tenant_id', event.user.app_metadata.tenant_id);
  api.idToken.setCustomClaim(ns + 'processing_activity_hint', 'OwnerLedger');
  api.idToken.setCustomClaim(ns + 'locale', event.user.user_metadata.locale || 'ar');
};
```

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.2
- ADR-021 Auth0 identity provider — [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- Architecture §4.3 D4.5 — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.3 D4.5
- Architecture §3.5 Cross-Cutting (ProcessingActivity) — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5
- FR1, NFR-S2, NFR-S3, NFR-S5 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Identity + Security
- Story 1.1 — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Registration fully delegated to Auth0 hosted flow — no bespoke UI in the Angular app
- Access token in memory; refresh token via httpOnly cookie through backend proxy; CSRF double-submit cookie
- `user_signup` telemetry PII-safe per AR-047

### File List

_Populated during implementation — see Source tree section above._
