# Story 1.4: Password reset via Auth0

Status: ready-for-dev

## Story

As a **user who forgot their password**,
I want **to request a password reset via email and set a new password**,
so that **I can regain access to my account (FR3)**.

**Covers requirements:** FR3 (password reset), NFR-S2 (password hashing), NFR-S3 (session tokens).

**Why this story now:** Per the reorder, 1.4 follows 1.3 and closes the core identity triangle. Auth0 owns the email delivery (composed + signed + delivered by Auth0) — no Resend involvement per ADR-023.

## Acceptance Criteria

### AC1 — "Forgot password?" link triggers Auth0 reset flow

**Given** the Auth0 hosted login page (from Story 1.3)
**When** the user clicks "Forgot password?" (localized: "نسيت كلمة السر؟" / "Forgot password?")
**Then** Auth0's hosted password-reset flow opens
**And** the user enters their email address
**And** Auth0 sends a reset-link email — no in-app code, no Resend involvement (ADR-023 confirmed: Resend is for statement-sharing transactional email, not identity)

### AC2 — Reset email in user's locale

**Given** the user initiated the reset from a locale-set context (stored in Auth0 user_metadata during prior login)
**When** Auth0 composes the email
**Then** the email body is in the user's stored locale (Arabic or English)
**And** Auth0 Email Template (configured via Dashboard → Branding → Email Templates → Change Password) has both AR and EN variants
**And** the email sender is `no-reply@auth.faktuboh.com` (verified SPF/DKIM/DMARC per ADR-021)

### AC3 — Reset link opens Auth0 hosted form

**Given** the user clicks the reset link in the email
**When** the browser opens it
**Then** the Auth0 hosted password-change form renders in the user's locale
**And** the form enforces the same password policy from Story 1.2 (bcrypt cost ≥ 12, min length 10, complexity)
**And** the new password must satisfy all NFR-S2 hash parameters

### AC4 — Successful reset redirects to login

**Given** the user submits a valid new password
**When** the reset completes
**Then** Auth0 invalidates all active sessions for the user (all refresh tokens revoked)
**And** the user is redirected to the Auth0 hosted login page (not `/dashboard`) — must re-authenticate with new password
**And** a confirmation message displays in the active locale

### AC5 — Old sessions revoked

**Given** the user had an active session in another browser/device
**When** the password reset completes
**Then** all existing refresh tokens are invalidated via Auth0's built-in session-revocation-on-password-change
**And** on next API call from those sessions, the refresh fails → forced re-login
**And** a `password_reset_completed` telemetry event emits with `{ opaque_user_id }` — no PII

### AC6 — Expired reset link handled gracefully

**Given** a reset link older than the Auth0-default 5-day TTL
**When** the user clicks it
**Then** Auth0's hosted form displays "This link has expired. Request a new one." in the active locale
**And** a link to request a new reset is provided
**And** the user can complete a fresh reset flow

### AC7 — Rate-limiting on reset requests

**Given** Auth0 Attack Protection
**When** a single email receives ≥ 3 reset requests within 15 minutes
**Then** subsequent requests are rate-limited
**And** the user sees a localized "Too many reset requests; try again later" message
**And** no email is sent during the rate-limit window (prevents email bombardment abuse)

### AC8 — Telemetry `password_reset_requested` + `password_reset_completed`

**Given** AR-047 telemetry primitive
**When** a reset is requested + later completed
**Then** `password_reset_requested` emits with `{ opaque_user_id }` — no PII
**And** `password_reset_completed` emits with `{ opaque_user_id }` — no PII
**And** NetArchTest asserts the schemas
**And** neither event contains the email address or IP

### AC9 — No in-app password reset form

**Given** the Angular owner-workspace
**When** code is inspected
**Then** no bespoke password-reset UI exists
**And** the login page's "Forgot password?" link is a thin redirect to the Auth0 hosted reset flow: `authService.loginWithRedirect({ authorizationParams: { screen_hint: 'reset-password' } })` — Auth0 handles navigation from there

### AC10 — Email template brand consistency

**Given** the Auth0 Email Template configuration
**When** the reset email renders in Gmail/Outlook/Apple Mail
**Then** the subject line is "Reset your Faktuboh password" (EN) / "إعادة تعيين كلمة سر Faktuboh" (AR)
**And** the body uses Faktuboh Quiet color tokens (where email clients permit) — teal primary button, white background
**And** accessible: semantic HTML, alt text on logo image, link has meaningful text (not "click here")
**And** renders in dark-mode-capable email clients (Auth0 templates support `@media (prefers-color-scheme: dark)`)

## Tasks / Subtasks

### Auth0 configuration

- [ ] **T1 (AC: 1, 2, 10)** — Configure Auth0 Email Template
  - [ ] Dashboard → Branding → Email Templates → Change Password
  - [ ] Enable + customize HTML + plaintext versions
  - [ ] Arabic + English content variants via Auth0's `{{user.user_metadata.locale}}` Liquid template variables
  - [ ] Sender: `no-reply@auth.faktuboh.com`; subject localized
- [ ] **T2 (AC: 6)** — Reset link TTL
  - [ ] Auth0 default is 5 days; keep default (no change needed)
  - [ ] Verify + document in `docs/auth/auth0-config-checklist.md`
- [ ] **T3 (AC: 7)** — Rate limiting
  - [ ] Dashboard → Attack Protection → Suspicious IP Throttling + Brute-Force Protection
  - [ ] Enable per-user email threshold (3 per 15 min)
  - [ ] Document in `docs/auth/auth0-config-checklist.md`

### Frontend — reset entry

- [ ] **T4 (AC: 1, 9)** — "Forgot password?" link
  - [ ] Auth0 hosted login page already has this link (Auth0 Universal Login default)
  - [ ] Ensure it is enabled in the Universal Login branding config
  - [ ] No bespoke UI in `apps/owner-workspace`
- [ ] **T5 (AC: 4)** — Post-reset redirect handling
  - [ ] Auth0 redirects to login page after reset; existing `login.component.ts` from Story 1.3 handles this
  - [ ] No additional frontend code needed — Auth0 owns the full flow
- [ ] **T6 (AC: 5)** — Session revocation observation
  - [ ] When a session's refresh-token exchange fails (because all sessions revoked), the refresh-proxy (from Story 1.2) returns 401 `faktuboh.auth.session_revoked`
  - [ ] Angular HTTP interceptor catches 401 with this code + clears in-memory state + redirects to login

### Backend

- [ ] **T7 (AC: 5)** — ErrorCatalog entry `faktuboh.auth.session_revoked`
  - [ ] HTTP 401; Transloco key `errors.auth.session_revoked`; AR + EN messages
- [ ] **T8 (AC: 8)** — Telemetry handlers
  - [ ] `password_reset_requested` — emitted via an Auth0 Log Stream webhook that calls `POST /v1/auth/telemetry/password-reset-requested` (signed JWT from Auth0)
  - [ ] `password_reset_completed` — same pattern, different event endpoint
  - [ ] Backend handlers validate the signed JWT + call `TelemetryEmitter.Emit(...)` with `opaque_user_id` only

### Tests

- [ ] **T9 (AC: 1-8)** — Playwright E2E at `apps/owner-workspace/e2e/auth/password-reset.spec.ts`
  - [ ] Happy path: click "Forgot password?" on login → Auth0 reset page opens in active locale
  - [ ] (E2E can't fully test email delivery; use Auth0 test tenant's inspection API or mock)
  - [ ] Expired-link scenario: navigate to stale reset URL → graceful error
  - [ ] Completed reset → redirect to login → re-auth with new password
- [ ] **T10 (AC: 5)** — Integration test for session-revocation interceptor
  - [ ] Mock 401 `session_revoked` response → verify Angular state cleared + redirect
- [ ] **T11 (AC: 8)** — Backend telemetry handler integration tests
  - [ ] Signed-JWT validation + `TelemetryEmitter.Emit` call with correct payload
  - [ ] NetArchTest schema assertion — no PII fields
- [ ] **T12 (AC: 10)** — Manual email template QA
  - [ ] Document in `docs/auth/email-templates-qa.md`
  - [ ] Render tests in Gmail/Outlook/Apple Mail via Auth0's test-send feature
  - [ ] Dark-mode rendering verified
  - [ ] Accessibility checked (alt text, semantic HTML, link text)

## Dev Notes

### Critical guardrails (do not violate)

1. **Auth0 owns the entire password reset flow.** No bespoke UI, no in-app reset form, no Resend involvement.
2. **Reset revokes all sessions.** Any client holding an old refresh token fails gracefully with `faktuboh.auth.session_revoked` + redirect to login. This is Auth0's default behavior — verify it's enabled.
3. **Rate limiting on Auth0 side — not in-app.** Don't try to duplicate the rate limit at the backend level; Auth0 carries the load.
4. **Email template in both locales.** Sending Arabic users an English reset email is a hard failure — visible break of FR4.
5. **No PII in telemetry.** Both `password_reset_requested` and `password_reset_completed` emit only `opaque_user_id`.
6. **Reset link TTL = 5 days** (Auth0 default). Do not shorten — user support cost increases; users reset from work email but check from phone days later.
7. **Email sender must pass SPF/DKIM/DMARC.** Auth0's `no-reply@auth.faktuboh.com` setup covers this; verify in DNS + Auth0 Branding config.

### Source tree — files to create or touch

```
apps/owner-workspace/src/app/features/auth/
└── login.component.ts                              # INHERITED from Story 1.3 (Auth0 link handles reset entry)

libs/core/src/auth/
├── auth.interceptor.ts                             # NEW or MODIFY (401 session_revoked handling)
└── auth.service.ts                                 # INHERITED

backend/src/Faktuboh.Api/Slices/Auth/
├── Telemetry/
│   ├── PasswordResetRequestedHandler.cs            # NEW (webhook endpoint)
│   ├── PasswordResetCompletedHandler.cs            # NEW
│   └── Auth0JwtValidator.cs                        # NEW (verifies signed JWT)
└── Auth.Endpoints.cs                               # MODIFY (register new webhook routes)

backend/src/Faktuboh.Application/Errors/
└── ErrorCatalog.cs                                 # MODIFY (add faktuboh.auth.session_revoked)

docs/auth/
├── auth0-config-checklist.md                       # MODIFY (reset TTL + rate limit docs)
└── email-templates-qa.md                           # NEW (manual email rendering QA)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | 70% | Vitest (FE) + NSubstitute (BE) |
| Integration | Webhook signature validation + telemetry emission | WebApplicationFactory |
| E2E | Happy + expired link | Playwright with Auth0 test tenant |
| Manual | Email rendering QA | Auth0 test-send + live Gmail/Outlook/Apple Mail |

**Forbidden:**
- Bespoke password-reset UI
- Resend involvement in identity email
- Un-localized email content
- PII in password-reset telemetry events
- Shortening the 5-day TTL without explicit justification

### Project Structure Notes

This story adds minimal backend + frontend code. The bulk of work is Auth0 dashboard configuration + email template localization. The frontend interceptor extension and backend telemetry-webhook handlers are the only code additions.

**Detected variances to reconcile:**
1. Auth0 Log Stream → webhook delivery is eventually consistent (~1–2 min). Acceptable for telemetry; do not rely on it for any real-time user-facing behavior.
2. Auth0 Log Stream requires an HTTPS endpoint — the backend webhook must be reachable from Auth0's IP range; document the trust boundary in `docs/auth/auth0-config-checklist.md`.
3. Email template Liquid syntax: `{{user.user_metadata.locale}}` may not exist on first-reset-attempt for a user without prior login — fallback to `{{user.user_metadata.locale | default: "en"}}`.

### Architecture compliance

- **ADR-021** (Auth0) — canonical
- **ADR-023** (Resend transactional email) — explicitly confirms Resend is for statement sharing, NOT identity; identity email is Auth0's responsibility
- **§4.3 D4.5** — Auth0 free tier handles reset flow
- **FR3** — password reset — fully satisfied by this story
- **NFR-S2** — bcrypt cost ≥ 12 already enforced by Story 1.2's Auth0 config; reset inherits

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Auth SDK | @auth0/auth0-angular | same as Story 1.2/1.3 |
| JWT validation (BE) | Microsoft.IdentityModel.Tokens | latest stable |

### Testing requirements

- **Unit**: Auth0 JWT validator, telemetry handler, session-revoked interceptor
- **Integration**: webhook endpoints accept Auth0-signed JWT, reject unsigned
- **E2E**: Playwright happy path with Auth0 test tenant
- **Manual**: email rendering in 3+ email clients

### Previous Story Intelligence

**Prerequisites:**
- **Story 1.2** — Auth0 tenant config, SDK wrapper, refresh proxy
- **Story 1.3** — login flow + callback handler + CSRF middleware

**Downstream:**
- No direct downstream dependencies; closes the identity-management triangle (signup/login/reset)

### Latest Technical Information

**Auth0 Email Templates (Liquid):**
```liquid
{% if user.user_metadata.locale == "ar" %}
  <h1>إعادة تعيين كلمة سر Faktuboh</h1>
  <p>انقر على الرابط أدناه لإعادة تعيين كلمة السر...</p>
{% else %}
  <h1>Reset your Faktuboh password</h1>
  <p>Click the link below to reset your password...</p>
{% endif %}
```

**Auth0 Log Stream webhook signature validation (backend):**
- Auth0 signs log stream payloads with HMAC-SHA256 using a shared secret stored in Key Vault
- Backend validator computes HMAC over raw request body + compares timing-safe to `x-auth0-signature` header
- Reject with 401 on mismatch

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.4
- ADR-021 Auth0 — [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- ADR-023 Resend transactional email — [_bmad-output/planning-artifacts/adrs/adr-023-resend-transactional-email.md](../planning-artifacts/adrs/adr-023-resend-transactional-email.md)
- FR3, NFR-S2 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 1.2 — [_bmad-output/implementation-artifacts/1-2-user-registration-auth0-hosted-flow.md](./1-2-user-registration-auth0-hosted-flow.md)
- Story 1.3 — [_bmad-output/implementation-artifacts/1-3-user-login-language-selection.md](./1-3-user-login-language-selection.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Password reset fully delegated to Auth0 hosted flow — no bespoke UI
- Auth0 owns email composition + delivery; Resend not involved (ADR-023 confirmed)
- Session revocation on reset handled by Auth0 default; frontend interceptor handles 401 session_revoked

### File List

_Populated during implementation — see Source tree section above._
