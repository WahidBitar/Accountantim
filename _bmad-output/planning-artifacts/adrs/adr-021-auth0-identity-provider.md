---
id: ADR-021
title: Identity Provider — Auth0 Free Tier for MVP (Custom Domain auth.faktuboh.com)
status: ACCEPTED
date: 2026-04-21
supersedes: [ADR-009]
source: Adversarial Review R-01
decision_type: two-way-door (one-way-door after first 100 users)
---

## Context

The original decision (ADR-009 / D4.5) chose self-hosted Keycloak on Azure Container Apps for long-term cost predictability. Adversarial review (AR-06) found a blocking conflict: Keycloak's JVM cold-start (20–40s on scale-to-zero ACA) violates PRD NFR-P1 (`/public-statement` LCP <2.5s on Slow 4G) and degrades the login flow. The original decision also required a Day 0 deployability spike that the solo-dev schedule (Day 35 dogfood) cannot absorb if it fails.

## Decision

Use **Auth0 free tier** (7,500 MAU, unlimited logins) as Faktuboh's MVP identity provider. Keycloak is removed from scope entirely.

- Tenant region: EU (for GDPR data residency).
- **Custom domain**: `auth.faktuboh.com` (Auth0 Custom Domains feature). Hosted login served from this host; all callback URLs, token audiences, and CORS origins must be registered against it from day one.
- MFA enabled; password-reset flow delegated to Auth0.
- Magic-link (FR42) delivered via Auth0 Passwordless — removes the need for a separate email-link implementation.
- OIDC integration via `Microsoft.AspNetCore.Authentication.OpenIdConnect` in the .NET 10 backend.
- Custom claims (tenant, `ProcessingActivity` hints) populated via Auth0 Actions.

## Rationale

- Eliminates the cold-start vs. LCP conflict — Auth0 is a managed edge service.
- Reclaims one container slot on ACA, easing the free-tier footprint.
- No MVP feature requires a self-hosted IDP; Auth0 free tier covers OIDC, MFA, password reset, passwordless.
- Removes the FR3/FR42 "stub email" blocker — auth emails are Auth0's responsibility.
- Keycloak was a DiTracker carryover; the cost-curve argument is real but premature given B2C MAU won't cross 7,500 inside MVP + beta window.

## Consequences

- New third-party dependency on Auth0. Acceptable risk at MVP scale.
- DNS setup required at tenant provisioning (CNAME + TXT records for `auth.faktuboh.com`).
- Callback URL and audience registration becomes part of the deployment checklist — changing these later invalidates all issued tokens.
- Keycloak-adjacent commitments dissolved: no Keycloak Postgres DB, no Vault SPI integration, no SMS OTP SPI, no monthly Keycloak patch window.
- Cost impact: Auth0 free at MVP volume; estimated €0/mo through first 7,500 MAU. Keycloak-on-ACA slot (~€15/mo per original cost table) reclaimed.

## Revisit Triggers

- **RT-AUTH-1:** MAU crosses 5,000 (approaching 7,500 cap). Action: evaluate Auth0 Essentials pricing vs. re-hosting on Keycloak or migrating to a managed alternative (Zitadel, Logto).
- **RT-AUTH-2:** Auth0 Essentials pricing at projected 12-month MAU exceeds €25/month. Action: same as above.
- **RT-AUTH-3:** Regulatory requirement emerges for EU-resident control plane (not just data plane). Action: migrate to self-host.

## Supersession Notes

- **ADR-009 (Keycloak self-host on ACA)** is SUPERSEDED. Architecture.md §4 D4.5 block is rewritten to reference this ADR.
- Day 0 Gate 2 (Keycloak-on-ACA spike) is DISSOLVED — no longer required.
- RT-39 (Keycloak-on-ACA Day 0 gate timebox) is DISSOLVED.
- ADR-020 (secrets rotation runbook) retains the Key Vault KEK rotation clause but drops the Keycloak signing-key rotation clause.

## Links

- PRD NFR-P1 (LCP budget) — constraint driving this change.
- ADR-023 (Transactional Email — Resend) — complements this ADR by handling non-auth transactional email.
