# Story 0.3: Auth0 tenant provisioning and custom domain

Status: ready-for-dev

## Story

As **Wahid**,
I want an **Auth0 EU free-tier tenant provisioned with custom domain `auth.faktuboh.com`**,
so that **identity is a managed edge service per ADR-021 and NFR-P1 cold-start cost is zero**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. ADR-021 one-way door: changing the custom domain after first-100-users invalidates every issued token. The 2026-04-21 adversarial review dissolved the Keycloak-on-ACA plan because JVM cold-start violated NFR-P1 LCP <2.5s on Slow 4G; Auth0 replaces that path.

**Why this story now:** Story 0.1 + 0.2 established the back-end and front-end shells. Auth0 is the third foundational one-way door (data residency + custom domain + tenant region — rest live with us forever). Story 0.4's RFC 9457 middleware and Story 0.7's ProcessingActivity claims both expect Auth0-issued tokens.

## Acceptance Criteria

### AC1 — Auth0 EU free-tier tenant exists

**Given** a new Auth0 account registered with Wahid's email
**When** tenant creation completes
**Then** the tenant region is `EU` (for GDPR data residency per ADR-021)
**And** tenant name is `faktuboh-prod` (and a second tenant `faktuboh-staging` exists for lower-env callbacks)
**And** the free-tier benefits (7,500 MAU, unlimited logins, MFA, Passwordless) are active on both tenants
**And** tenant region cannot be changed later without re-provisioning — documented in `docs/runbooks/auth0-setup.md` [Source: adrs/adr-021-auth0-identity-provider.md]

### AC2 — Custom domain DNS records provisioned

**Given** Auth0 Custom Domains is enabled on the production tenant
**When** the developer initiates custom-domain setup via Auth0 Dashboard → Branding → Custom Domains
**Then** Auth0 emits two DNS records that must be added at the `faktuboh.com` DNS zone:

```
# CNAME record (for TLS certificate provisioning + login routing)
Type:  CNAME
Name:  auth.faktuboh.com
Value: <tenant>-cd-xxxxxxxx.edge.auth0.com   # exact value comes from Auth0 Dashboard

# TXT record (for domain ownership verification)
Type:  TXT
Name:  _cf-custom-hostname.auth.faktuboh.com   # OR the TXT record name Auth0 provides (varies by version)
Value: <verification-string>                   # from Auth0 Dashboard
```

**And** the DNS records are added at the `faktuboh.com` registrar (Cloudflare or equivalent)
**And** Auth0 verifies ownership within 24 hours (typical: minutes)
**And** the Auth0 Dashboard shows `Verified` + a managed TLS certificate provisions automatically
**And** `https://auth.faktuboh.com/.well-known/openid-configuration` returns the OIDC discovery document [Source: adrs/adr-021-auth0-identity-provider.md + architecture.md §4 D4.5]

### AC3 — Hosted login renders + callback/audience/CORS registered

**Given** the custom domain is verified
**When** a user navigates to `https://auth.faktuboh.com/authorize?client_id=<spa_client>&response_type=code&redirect_uri=https://app.faktuboh.com/callback`
**Then** the Auth0 Universal Login page renders at the custom-domain URL (not `<tenant>.eu.auth0.com`)
**And** the Auth0 application "Faktuboh Owner Workspace SPA" is configured with:
- Application Type: `Single Page Application`
- Token Endpoint Authentication: `None` (SPA)
- Allowed Callback URLs: `https://app.faktuboh.com/callback`, `https://app.faktuboh.com`, `http://localhost:4200/callback`, `http://localhost:4200`
- Allowed Logout URLs: `https://app.faktuboh.com`, `http://localhost:4200`
- Allowed Web Origins: `https://app.faktuboh.com`, `https://statement.faktuboh.com`, `http://localhost:4200`, `http://localhost:4201`
- Allowed CORS Origins: same as Web Origins
**And** a separate Auth0 API resource "Faktuboh Backend API" exists with identifier `https://api.faktuboh.com` (the JWT `aud` claim value)
**And** the backend's `Faktuboh.Api` registers this identifier as the expected audience [Source: architecture.md §4 D4.5 + adrs/adr-021]

### AC4 — Auth0 Action populates custom claims (`tenant` + `ProcessingActivity`-hint placeholder)

**Given** Auth0 Actions are enabled
**When** a user completes login and Auth0 is about to issue the token
**Then** a Post-Login Action named `faktuboh-custom-claims` runs
**And** the Action adds two custom claims to the ID token + access token:
- `https://faktuboh.com/tenant` — value `owner-ledger` (placeholder; multi-tenant is deferred per ADR-002)
- `https://faktuboh.com/processing_activity_hint` — value `OwnerLedger` (placeholder consumed by Story 0.7 for default ProcessingActivity)
**And** the Action JavaScript is committed to `backend/identity/auth0-actions/post-login-custom-claims.js`:

```javascript
// backend/identity/auth0-actions/post-login-custom-claims.js
/**
 * Post-Login Action: adds Faktuboh custom claims to ID and Access tokens.
 * Claim namespace: https://faktuboh.com/
 * - tenant: always "owner-ledger" at MVP (multi-tenancy deferred per ADR-002)
 * - processing_activity_hint: default "OwnerLedger" (consumed by Story 0.7 ambient context)
 */
exports.onExecutePostLogin = async (event, api) => {
  const namespace = 'https://faktuboh.com/';
  api.idToken.setCustomClaim(`${namespace}tenant`, 'owner-ledger');
  api.idToken.setCustomClaim(`${namespace}processing_activity_hint`, 'OwnerLedger');
  api.accessToken.setCustomClaim(`${namespace}tenant`, 'owner-ledger');
  api.accessToken.setCustomClaim(`${namespace}processing_activity_hint`, 'OwnerLedger');
};
```

**And** the Action is attached to the `Login / Post-Login` flow in Auth0 Dashboard
**And** a manual login test shows the JWT contains these custom claims (decode via jwt.io) [Source: adrs/adr-021-auth0-identity-provider.md + architecture.md §4 D4.5 + §3.5 ProcessingActivity]

### AC5 — Passwordless connection enabled for magic-link flows (FR42)

**Given** Auth0 Passwordless connection is available on free tier
**When** Passwordless is enabled via Dashboard → Authentication → Passwordless
**Then** the `email` Passwordless connection is enabled for the SPA application
**And** the magic-link template uses the `noreply@faktuboh.com` from-address (Resend-delivered per ADR-023)
**And** the email branding reflects Faktuboh (placeholder logo + "write it down" tagline per project_faktuboh_rebrand)
**And** a manual Passwordless test flow completes end-to-end (request link → click → authenticated session) [Source: adrs/adr-021 + prd.md FR42]

### AC6 — MFA enabled at tenant level

**Given** Auth0 MFA settings are accessible via Dashboard → Security → Multi-factor Authentication
**When** MFA is enabled
**Then** MFA factors available: One-Time Password (OTP) via Auth0 Guardian or compatible app
**And** MFA policy is set to `Always Require` for the SPA application (MVP posture; can be relaxed per user setting later)
**And** a manual test flow proves MFA enrollment + challenge on login [Source: adrs/adr-021]

### AC7 — Backend configured to validate Auth0-issued JWTs

**Given** `Faktuboh.Api` uses `Microsoft.AspNetCore.Authentication.JwtBearer`
**When** `Program.cs` wires JWT validation
**Then** the authority is `https://auth.faktuboh.com/`
**And** the audience is `https://api.faktuboh.com`
**And** the JWT validation parameters are:

```csharp
// backend/src/Faktuboh.Api/Program.cs (excerpt)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth0:Authority"]; // "https://auth.faktuboh.com/"
        options.Audience = builder.Configuration["Auth0:Audience"];   // "https://api.faktuboh.com"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            NameClaimType = "sub",
            RoleClaimType = "https://faktuboh.com/roles"
        };
    });

builder.Services.AddAuthorization();
```

**And** the backend can verify Auth0-issued tokens against the JWKS at `https://auth.faktuboh.com/.well-known/jwks.json`
**And** an integration test `tests/Faktuboh.Api.Tests/Auth/AuthenticationIntegrationTests.cs` asserts:
- GET `/api/authenticated-placeholder` with no Authorization header returns 401
- GET `/api/authenticated-placeholder` with an invalid token returns 401
- GET `/api/authenticated-placeholder` with a valid Auth0-issued token (via Auth0 Management API test fixture) returns 200 [Source: architecture.md §4 D4.5]

### AC8 — Runbook + secrets stored in Key Vault

**Given** secrets exist: `AUTH0_DOMAIN`, `AUTH0_AUDIENCE`, `AUTH0_SPA_CLIENT_ID`, `AUTH0_MGMT_CLIENT_ID`, `AUTH0_MGMT_CLIENT_SECRET`
**When** Story 0.10 provisions Key Vault
**Then** these secrets are stored in Azure Key Vault under the `auth0/` prefix (names `auth0--domain`, `auth0--audience`, etc.)
**And** `backend/src/Faktuboh.Api/appsettings.json` references config keys `Auth0:Authority`, `Auth0:Audience`, `Auth0:SpaClientId` (empty in source control; populated at deploy time via Key Vault reference)
**And** local dev uses `dotnet user-secrets` with the same keys pointing at the staging tenant
**And** `docs/runbooks/auth0-setup.md` documents:
- DNS records required (CNAME + TXT with exact shape per AC2)
- Application registration procedure
- Action JavaScript deployment procedure
- Secrets mapping to Key Vault
- Tenant-recreation procedure (for DR scenarios) [Source: architecture.md §6.5.6 + adrs/adr-025-key-vault]

## Tasks / Subtasks

### T1 — Create two Auth0 tenants (AC: 1)

- [ ] Register Auth0 account with Wahid's email at https://manage.auth0.com (free tier)
- [ ] Create tenant `faktuboh-prod` in region `EU`
- [ ] Create tenant `faktuboh-staging` in region `EU` (second tenant for staging/dev)
- [ ] Record tenant IDs + default domain names (`faktuboh-prod.eu.auth0.com` etc.) in `docs/runbooks/auth0-setup.md`

### T2 — Provision custom domain (AC: 2)

- [ ] In `faktuboh-prod` Dashboard → Branding → Custom Domains → Add `auth.faktuboh.com` as Auth0-managed custom domain
- [ ] Copy the CNAME target value (format: `<tenant>-cd-xxxxxxxx.edge.auth0.com`) + TXT verification string
- [ ] At `faktuboh.com` DNS registrar, add both records (CNAME + TXT) exactly as Auth0 specified
- [ ] Wait for Auth0 to verify (usually minutes; up to 24h)
- [ ] Confirm TLS cert provisions and `https://auth.faktuboh.com/.well-known/openid-configuration` returns OIDC doc
- [ ] Repeat for staging tenant with `auth-staging.faktuboh.com`

### T3 — Register SPA application + API resource (AC: 3)

- [ ] Dashboard → Applications → Create Application "Faktuboh Owner Workspace SPA", type `SPA`
- [ ] Configure Callback / Logout / Web Origin / CORS URLs per AC3 (production + local `:4200` + local `:4201`)
- [ ] Dashboard → APIs → Create API "Faktuboh Backend API" with identifier `https://api.faktuboh.com`, signing algo `RS256`
- [ ] Enable "Allow Offline Access" on the API (for refresh tokens)
- [ ] Record SPA Client ID + API audience in runbook

### T4 — Create Post-Login Action (AC: 4)

- [ ] Dashboard → Actions → Library → Custom → Create Action "faktuboh-custom-claims", runtime `Node 18`
- [ ] Paste the JS from AC4 verbatim
- [ ] Deploy the Action
- [ ] Dashboard → Actions → Flows → Login → drag `faktuboh-custom-claims` into the flow → Apply
- [ ] Commit `backend/identity/auth0-actions/post-login-custom-claims.js` to the repo as source of truth (the Dashboard is deploy surface; repo is authority)
- [ ] Test: log in via hosted login, decode token at jwt.io, verify custom claims present

### T5 — Enable Passwordless connection (AC: 5)

- [ ] Dashboard → Authentication → Passwordless → enable `email`
- [ ] Configure magic-link template with `from: noreply@faktuboh.com`, subject in Arabic + English (bilingual placeholder)
- [ ] Attach Passwordless connection to the SPA application
- [ ] Test: request magic link, receive email, click link, verify authenticated session

### T6 — Enable MFA (AC: 6)

- [ ] Dashboard → Security → Multi-factor Authentication → enable `One-Time Password` (Auth0 Guardian / TOTP apps)
- [ ] Set policy to `Always Require` for the SPA application
- [ ] Test: enroll MFA on a test account, verify challenge on subsequent login

### T7 — Wire backend JWT validation (AC: 7)

- [ ] Add NuGet package `Microsoft.AspNetCore.Authentication.JwtBearer` to `Faktuboh.Api.csproj`
- [ ] Paste AC7 JWT Bearer configuration into `Program.cs` between `AddServiceDefaults()` and `AddProblemDetails()`
- [ ] Append `app.UseAuthentication(); app.UseAuthorization();` before `app.MapGet("/health", ...)`
- [ ] Create a placeholder authenticated endpoint: `app.MapGet("/api/authenticated-placeholder", [Authorize] () => TypedResults.Ok(new { user = "authenticated" }));`
- [ ] Create `tests/Faktuboh.Api.Tests/Auth/AuthenticationIntegrationTests.cs` with the three assertions from AC7 using `WebApplicationFactory<Program>` + a test JWT fixture

### T8 — Secrets + runbook (AC: 8)

- [ ] Add config keys to `Faktuboh.Api/appsettings.json` (empty values): `Auth0:Authority`, `Auth0:Audience`, `Auth0:SpaClientId`
- [ ] Add same keys to `Faktuboh.Api/appsettings.Development.json` with staging values (okay to commit staging — production values land in Key Vault)
- [ ] Create `docs/runbooks/auth0-setup.md` documenting T1-T7 procedures
- [ ] Flag Story 0.10 (Azure infra) to wire `Auth0__Domain` + `Auth0__Audience` Key Vault references on ACA
- [ ] Local dev: `dotnet user-secrets set Auth0:Authority "https://auth-staging.faktuboh.com/" --project backend/src/Faktuboh.Api`

### T9 — Document Auth0 delete-user integration (AC: 1)

- [ ] Append to `docs/runbooks/auth0-setup.md` the erasure flow stub (per ADR-021 + ADR-004):
  - Owner erasure request → LegalHold check (Epic 3 Story; stub endpoint) → call Auth0 Management API `DELETE /api/v2/users/{id}` → emit `SubjectKeyDestroyed` domain event → proceed with key-shred
  - Management API client credentials (`AUTH0_MGMT_CLIENT_ID` + `AUTH0_MGMT_CLIENT_SECRET`) stored in Key Vault; scoped to `delete:users` only
  - Integration implementation deferred to Epic 3 (first erasure slice); Story 0.3 stubs the config

## Dev Notes

### Critical guardrails (do not violate)

1. **Custom domain is a one-way door after first-100-users.** Subject IDs are Auth0-tenant-scoped; changing the tenant requires permanent subject-ID mapping tables. Confirm `auth.faktuboh.com` is the permanent choice before DNS records propagate. [Source: adrs/adr-021]
2. **Callback URL + audience are part of the deployment checklist.** Adding a new FE origin after launch requires adding the URL to every Auth0 application config; forgetting invalidates the flow. [Source: architecture.md §4 D4.5]
3. **`ProcessingActivity` hint claim is a PLACEHOLDER.** Story 0.7 replaces the default "OwnerLedger" with endpoint-filter-set values; the Auth0 claim is a fallback only, not authoritative. [Source: architecture.md §3.5 + Story 0.7]
4. **Auth0 Action source code lives in the repo.** Dashboard is deploy surface only. Drift between `backend/identity/auth0-actions/*.js` and Dashboard Actions is a review-board issue — git is source of truth. [Source: adrs/adr-021]
5. **Management API client credentials are least-privilege.** Scope to specific endpoints (`delete:users`, `read:users`); never grant `read:client_grants` or similar admin scopes. [Source: architecture.md §4 D4.5]
6. **No Auth0 Rules (legacy).** Use Actions only. Rules are Auth0's legacy system, scheduled for deprecation. [Source: Auth0 roadmap]

### Source tree — files to create

```
backend/
├── identity/
│   └── auth0-actions/
│       └── post-login-custom-claims.js     # NEW (source of truth for the Action)
└── src/Faktuboh.Api/
    ├── Program.cs                          # EDIT (add JWT Bearer auth)
    ├── appsettings.json                    # EDIT (empty Auth0 keys)
    └── appsettings.Development.json        # EDIT (staging Auth0 values)

backend/tests/
└── Faktuboh.Api.Tests/
    └── Auth/
        └── AuthenticationIntegrationTests.cs  # NEW (3 test cases)

docs/
└── runbooks/
    └── auth0-setup.md                      # NEW (complete procedure)
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Integration | `WebApplicationFactory<Program>` + Auth0 test JWT fixture | AC7 401/401/200 assertions |
| Manual | Auth0 Dashboard + browser | AC2 DNS verification, AC3 login flow, AC4 token claims, AC5 magic-link flow, AC6 MFA enrollment |

**Forbidden:**
- Hard-coding the Auth0 domain / audience / client ID in source code (must be config)
- Sharing a single tenant between prod + staging (two-tenant pattern per AC1)
- Granting the Management API client admin scopes (`read:tenant_settings` etc.)

### Project Structure Notes

- **`backend/identity/` is a new top-level folder** under `backend/` (sibling of `src/`, `tests/`). Auth0 Action source + any future identity-adjacent tooling lands here. This is a minor variance from architecture.md §6.3.1 (which shows only `src/` + `tests/`); the rationale is keeping Auth0 Action code out of the .NET compilation graph while still repo-committed.
- **Staging tenant (`faktuboh-staging`) is a TWO tenant approach**, not an Auth0 "environment" (Auth0's per-tenant environment concept is available only on paid tiers). Two full tenants is the free-tier pattern.

### Architecture compliance

- [architecture.md §4 D4.5](../planning-artifacts/architecture.md) — Auth0 identity provider decision
- [architecture.md §4.7 step 3](../planning-artifacts/architecture.md) — Identity implementation sequence
- [architecture.md §3.5](../planning-artifacts/architecture.md) — `ProcessingActivity` ambient context
- [architecture.md §4 D4.9](../planning-artifacts/architecture.md) — Auth0 `sub` claim drives `subject_id` in audit rows
- [adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md) — full ADR content
- [adrs/adr-025-key-vault-standard-for-mvp.md](../planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md) — secrets location
- [architecture.md §7.5.5 UL Mapping ADR-013](../planning-artifacts/architecture.md) — Glossary scope includes Auth0 custom claim names
- [prd.md FR1-FR7, FR42](../planning-artifacts/prd.md) — User Management + Magic Link

### Library/framework requirements

| Layer | Package | Version | Purpose |
|---|---|---|---|
| Identity (backend) | Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.x | JWT validation |
| Identity (backend) | Microsoft.IdentityModel.Protocols.OpenIdConnect | 10.0.x | JWKS resolution |
| Identity (frontend) | `@auth0/auth0-angular` | latest (Angular 21 compatible) | FE login SDK (wired in Epic 1 Story 1.2/1.3) |

**Note:** the frontend Auth0 SDK wire-up is deferred to Epic 1 (identity slice); this story provisions the tenant + backend validation only.

### Testing requirements

- **AC2 DNS verification is manual + time-gated.** Allow up to 24h for certificate provisioning; do not mark the AC done until Auth0 Dashboard shows "Verified".
- **AC4 Action behavior tested by decoding a real JWT** after login, not by unit-testing the JS (Auth0 runtime differs from Node in non-trivial ways; production behavior is the only truth).
- **AC7 integration test uses a real Auth0-issued token** (fetched via a fixture that uses Resource Owner Password Grant against the staging tenant, with a dedicated test user). Do not mock `JwtBearerHandler`.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: `Faktuboh.Api/Program.cs` exists with `AddServiceDefaults()` + `AddProblemDetails()` + `AddOpenApi()` + `/health` endpoint. This story adds auth between `AddProblemDetails()` and the endpoint registration.
- **Story 0.2 (FE workspace)** complete: `app.faktuboh.com` + `statement.faktuboh.com` DNS origins are the FE targets that Auth0's Allowed URLs must include.
- **Stories 0.4+ (RFC 9457, Idempotency, Bitemporal, ProcessingActivity, NetArchTest) have NOT shipped yet.** This story intentionally precedes them per §4.7 implementation sequence — identity is step 3 (after region+vault and database).
- **Story 0.10 (Azure infra)** will consume this story's secrets via Key Vault references.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.3 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.5](../planning-artifacts/architecture.md) — Auth0 decision
- [_bmad-output/planning-artifacts/architecture.md §4.7](../planning-artifacts/architecture.md) — implementation sequence step 3
- [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md) — complete ADR
- [_bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md](../planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md) — secrets policy
- [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) — FR1-FR7, FR42 (magic-link)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
