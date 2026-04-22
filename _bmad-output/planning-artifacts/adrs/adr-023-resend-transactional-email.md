---
id: ADR-023
title: Transactional Email — Resend Free Tier (EU Region)
status: ACCEPTED
date: 2026-04-21
source: Adversarial Review R-03
decision_type: two-way-door
---

## Context

The original architecture stubbed the email provider (§6.5.10 "console log only") despite PRD FR3 (password reset email) and FR42 (20 magic-link emails/user/hour). Adversarial review (AR-05) flagged this as an unresolved blocker: stubbing is not a strategy.

With ADR-021 (Auth0) absorbing the auth-email path (password reset, magic links via Auth0 Passwordless), the remaining surface is **non-auth transactional email** — debt notifications, confirmation receipts, statement-share notifications.

## Decision

Use **Resend** (`resend.com`) on the free tier (3,000 emails/month, 100/day) for all non-auth transactional email. Sending region: **`eu-west-1`** for GDPR data residency.

- Resend is invoked via its .NET client; API key in `RESEND_API_KEY` env var (sourced from Key Vault).
- All email sends route through **Wolverine's outbox pattern** — durable, retried on failure.
- Deliverability hygiene: DKIM and DMARC configured at the `faktuboh.com` domain level during provisioning.

## Rationale

- **Free tier well inside MVP volume** — expected beta-window sends are ~500/month against a 3,000/month cap.
- **EU region** satisfies GDPR data residency on the transactional-email path.
- **Modern DX**: DKIM/DMARC setup in minutes; clean REST API; first-class .NET client.
- **Clean separation of concerns**: Auth0 handles auth emails (ADR-021); Resend handles transactional emails. Each vendor picked for its strength.
- If Resend is down, Wolverine's outbox queues sends and retries — no lost emails.

## Consequences

- New vendor integration, offset by the removal of Keycloak SMS/email SPI (per ADR-021).
- `RESEND_API_KEY` joins the secrets list; rotation cadence quarterly.
- Environment configuration now requires `RESEND_REGION=eu-west-1`.
- Cost impact at MVP: €0/mo.

## Revisit Triggers

- **RT-EMAIL-1:** Monthly volume crosses 2,500 emails (≥83% of free tier). Action: evaluate Resend Pro or AWS SES alternatives.
- **RT-EMAIL-2:** Deliverability rate (measured via Resend dashboard) drops below 95%. Action: investigate DKIM/DMARC/SPF misconfig or provider reputation.

## Links

- ADR-021 (Auth0) — complementary; defines the auth-email path.
- PRD FR3 (password reset) — now delegated to Auth0 per ADR-021.
- PRD FR42 (magic-link rate limit) — now delegated to Auth0 Passwordless per ADR-021; Resend covers only non-auth transactional email.
