---
stepsCompleted: ["step-01-extract-requirements", "step-02-design-epics", "step-03-create-stories", "step-04-final-validation"]
inputDocuments:
  - prd.md
  - architecture.md
  - ux-design-specification.md
  - adrs/adr-021-auth0-identity-provider.md
  - adrs/adr-022-application-level-bitemporal.md
  - adrs/adr-023-resend-transactional-email.md
  - adrs/adr-024-minimal-apis-framework.md
  - adrs/adr-025-key-vault-standard-for-mvp.md
  - adr-resolution-draft.md
lastEdited: 2026-04-22
---

# Faktuboh - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for **Faktuboh** — a personal debt ledger replacing scattered spreadsheets with a single, clear system for tracking who owes what to whom across multiple currencies and precious metals. It decomposes the requirements from the PRD, UX Design Specification, and Architecture (including ADR-021…ADR-025) into implementable stories.

**Project classification:** Greenfield web application · Fintech (personal debt ledger) · High complexity · Bilingual AR-RTL + EN-LTR · Azure Europe (Germany West Central).

## Requirements Inventory

### Functional Requirements

**User Management**

- **FR1:** User can register a new account with email and password.
- **FR2:** User can authenticate using their credentials.
- **FR3:** User can reset their password via email. _Delivery: Auth0 hosted flow (ADR-021) — email composed, signed, and sent by Auth0; satisfied by configuration, not in-app code._
- **FR4:** User can set their preferred language (Arabic or English).
- **FR5:** User can view and edit their profile information.
- **FR6:** User can delete their account and all associated data (GDPR right to deletion).
- **FR7:** User can view a legal disclaimer explaining that tracked debts are user records, not legally binding contracts.

**Contact Management**

- **FR8:** User can add a new contact with a name and optional details (phone, email, notes).
- **FR9:** User can edit an existing contact's information.
- **FR10:** User can delete a contact (with warning if outstanding balances exist).
- **FR11:** User can view a list of all their contacts with summary balances per denomination.
- **FR12:** User can search and filter contacts by name.

**Debt Management**

- **FR13:** User can record a new debt (lent or borrowed) with a contact, specifying amount, denomination (currency or precious metal), date, and optional story/context.
- **FR14:** User can edit an existing debt entry (with audit trail preserved).
- **FR15:** User can delete a debt entry (with audit trail preserved).
- **FR16:** User can view the full transaction history with a specific contact, with the ability to filter by date range. When a date filter is applied, the system rolls up all transactions before the filter start date into a single "previous balance" per denomination.
- **FR17:** User can record debts in any supported denomination — debts are preserved in their original denomination and never auto-converted.

**Currency & Exchange Operations**

- **FR18:** System fetches exchange rates for all supported currencies and metals every hour, using gold as the internal base unit for rate calculation.
- **FR19:** User can perform a currency/metal exchange operation between two denominations for a specific contact. The system auto-populates the exchange rate from the latest hourly feed, and the user can override it manually. The rate is locked at the time of the transaction.
- **FR20:** User can view the exchange rate used for any historical exchange operation.
- **FR21:** System preserves denomination integrity — no implicit or automatic conversions between denominations.
- **FR22:** System stores historical exchange rate data for reference and audit purposes.
- **FR23:** User can view the current system exchange rate between any two supported denominations before confirming an exchange operation.

**Debt Transfer**

- **FR24:** User can transfer all or part of a debt from one contact to another via a journal entry.
- **FR25:** System records both sides of the transfer as linked transactions with a clear audit trail.
- **FR26:** User can view the transfer history showing the origin and destination of transferred debts.

**Account Statements & Dashboard**

- **FR27:** User can view a dashboard showing an overview of all contacts with their balances per denomination.
- **FR28:** User can view a per-contact account statement with full transaction history and current balance per denomination.
- **FR29:** User can generate an account statement report for a specific contact.
- **FR30:** User can export their data as CSV (all contacts, all transactions).
- **FR31:** Dashboard displays the user's overall financial position across all contacts and denominations without cross-currency aggregation.
- **FR32:** Dashboard displays a bar chart showing debt distribution across contacts.
- **FR33:** Dashboard displays a pie chart showing debt distribution across denominations.

**Statement Sharing & Confirmation**

- **FR34:** User can generate a unique shareable link for any contact's account statement.
- **FR35:** Statement recipient can view the shared statement in a mobile-optimized page without creating an account.
- **FR36:** Statement recipient can confirm the statement by tapping a "Confirm" button, recording the confirmation date.
- **FR37:** User can see the last confirmation date for each contact's statement.
- **FR38:** Shared statement pages display Faktuboh branding and a sign-up CTA.
- **FR39:** Shared statement pages display the legal disclaimer and a watermark indicating it is a user-generated record.
- **FR40:** User can manage (view and revoke) their generated statement links.
- **FR41:** System generates cryptographically random, non-guessable tokens for statement links.
- **FR42:** System limits statement link generation to 20 links per user per hour. _Magic-link delivery for statement recipients (when a link expires and a fresh token is requested): Auth0 Passwordless (ADR-021) — rate limit enforced at Auth0's tenant level in addition to our per-user cap._
- **FR52:** System prompts the user to share a statement at activation moments that drive the viral loop: (a) after each of the first 3 debts recorded per contact, (b) when a contact has recorded-but-unshared transactions more than 7 days old, (c) when a contact's balance crosses a material threshold (configurable; default: any denomination with >€50 equivalent). Nudges are non-blocking and dismissible; a user who dismisses 3 consecutive nudges for a contact is not re-nudged for that contact for 30 days.
- **FR53:** Shared statement pages display a "Report as fraudulent or harassing" affordance visible to recipients. Reports queue to the admin review flow (FR46) with context: link ID, recipient IP-hash, report reason, free-text optional. Reports do not auto-revoke the link — admin review (FR47) decides. Recipients who report see a neutral confirmation page without feedback on the link owner's identity.

**Administration & Platform Operations**

- **FR43:** Admin can view a dashboard showing platform metrics (active users, registrations, statement links generated, confirmation rates).
- **FR44:** Admin can configure freemium thresholds (transaction count, contact count).
- **FR45:** Admin can manage grandfathering rules for existing users when transitioning to freemium.
- **FR46:** Admin can investigate and flag accounts for potential abuse.
- **FR47:** Admin can temporarily suspend statement link generation for a flagged account.
- **FR48:** System enforces freemium limits for new users based on configured thresholds. _Ships configured-but-disabled at MVP; activated only by the Revenue-readiness trigger in Business Success (500+ users hit threshold + 40%+ willingness-to-pay)._
- **FR49:** System logs all debt operations (create, edit, delete, transfer, exchange) in an immutable audit trail.

**Real-Time Notifications**

- **FR50:** User receives a real-time notification when a counterparty confirms their statement.
- **FR51:** System maintains real-time push connection with automatic reconnection on network interruption.

_Gap note: PRD jumps from FR42 → FR52 by design — FR43–FR51 belong to Admin + Notifications (documented above)._

### NonFunctional Requirements

**Performance**

- **NFR-P1 (public-statement bundle):** initial JS < 180 KB gzipped, LCP < 2.5 s on Slow 4G. CI bundle-graph gate per ADR-024 cascade.
- **NFR-P1 (owner auth-shell bundle):** initial JS < 250 KB gzipped. Design-system data-grid / form-control overhead allowed within budget.
- **NFR-P2:** Landing pages FCP < 1.5s, LCP < 2.5s.
- **NFR-P3:** SPA initial load TTI < 3s.
- **NFR-P4:** Debt recording operation < 500ms end-to-end.
- **NFR-P5:** Shared statement page load < 2s.
- **NFR-P6:** Dashboard rendering (with charts) < 2s.
- **NFR-P7:** Balance calculation (per contact) < 200ms including date-range rollup.
- **NFR-P8:** Exchange rate feed processing < 30s per hourly cycle.
- **NFR-P9:** Real-time notification delivery < 1s from event.
- **NFR-P10:** CSV export generation < 10s for accounts with up to 10,000 transactions.
- **NFR-P11:** 500 simultaneous users sustained while maintaining all performance targets.

**Security**

- **NFR-S1:** All data encrypted at rest using provider-managed keys and in transit (TLS 1.2+).
- **NFR-S2:** User passwords hashed using bcrypt (cost ≥ 12) OR Argon2id (memory ≥ 64 MiB, iterations ≥ 3, parallelism ≥ 1). Hash parameters reviewed every 24 months against current OWASP ASVS. Passwords never stored in plaintext.
- **NFR-S3:** Session management with session tokens stored using browser security attributes preventing XSS and CSRF access. Session timeout after 30 minutes of inactivity with auto-extension on activity.
- **NFR-S4:** Statement link tokens are cryptographically random (min 128-bit entropy), non-sequential, and non-guessable.
- **NFR-S5:** Rate limiting: max 10 failed authentication attempts per 15 minutes per IP; max 20 statement links per user per hour; max 60 public statement page views per link per minute.
- **NFR-S6:** Audit trail is append-only and immutable — no API or UI path allows modification or deletion of audit entries.
- **NFR-S7:** GDPR compliance: user data deletion anonymizes audit records (retains transaction structure without PII), exports all user data on request, and provides consent management.
- **NFR-S8:** Shared statement pages expose no internal IDs, user credentials, or data beyond the specific contact's statement.
- **NFR-S9:** Input validation on all user-facing endpoints — protection against XSS, SQL injection, and CSRF.
- **NFR-S10:** Admin operations require separate elevated authentication and are logged in the audit trail.
- **NFR-S11:** Statement links default to 30-day expiration from generation unless the owner explicitly extends (Phase 2 feature). Expired links return a neutral "link expired" page without exposing prior statement data, recipient identity, or link ownership. Expiration enforced server-side.

**Scalability**

- **NFR-SC1:** System supports 10,000 registered users with up to 500 concurrent active users at MVP launch.
- **NFR-SC2:** System handles 10x user growth (100,000 users) with infrastructure scaling and < 10% performance degradation.
- **NFR-SC3:** Database schema supports queries completing within performance targets when filtered by user, contact, date, and denomination across large transaction histories.
- **NFR-SC4:** Statement link pages handle traffic spikes (viral sharing) independently of the main SPA. Sustained capacity commitment: 5,000 requests/minute (~83 rps) with no increase in SPA response times. (Architecture designs against 500 rps burst for cache sizing — not a PRD SLO.)
- **NFR-SC5:** Exchange rate feed processing scales independently; during feed processing, user-facing API times remain within targets with zero error-rate increase.
- **NFR-SC6:** Infrastructure supports horizontal scaling of web tier and vertical scaling of database tier as needed.

**Accessibility**

- **NFR-A1:** WCAG 2.1 AA compliance across all user-facing pages (SPA, landing pages, shared statement pages).
- **NFR-A2:** Full keyboard navigation — all interactive elements reachable and operable without a mouse.
- **NFR-A3:** Screen reader compatibility with ARIA labels compliant with WCAG 2.1 SC 1.3.1 (Info and Relationships) and SC 4.1.2 (Name, Role, Value), semantic HTML5 landmarks, ARIA live regions for real-time notifications.
- **NFR-A4:** Color contrast ratios: 4.5:1 minimum for normal text, 3:1 for large text (AA baseline). **AAA contrast (7:1 / 4.5:1) on financial surfaces** — amounts, confirmations, statements.
- **NFR-A5:** RTL accessibility — bidirectional text handled correctly by screen readers; logical reading order preserved.
- **NFR-A6:** Touch targets minimum 44×44 px on mobile for all interactive elements.
- **NFR-A7:** Form validation errors associated with their fields and announced to assistive technology.
- **NFR-A8:** Focus management during SPA route changes, modal dialogs, and dynamic content updates.
- **NFR-A9:** RTL quality acceptance gate — before MVP ships, six named scenarios must pass visual QA on at least two RTL-native reviewer devices: (a) mixed AR + Latin-numeral transaction stories with bidi isolation; (b) user-configurable Latin vs Eastern-Arabic numerals; (c) bidi isolation in transaction notes; (d) RTL dashboard bar/pie charts; (e) RTL CSV export (column order reversed; UTF-8 with BOM; currency symbol placement); (f) right-aligned date-range picker with optional Hijri-calendar toggle (Hijri read-only at MVP).

**Integration**

- **NFR-I1:** Exchange rate API feed must support gold, silver, and major world currencies (USD, EUR, GBP, SAR, AED, TRY, at minimum).
- **NFR-I2:** Exchange rate feed runs on hourly schedule with retry logic on failure (max 3 retries with exponential backoff).
- **NFR-I3:** System gracefully degrades if exchange rate feed is unavailable — uses last known rates and displays a "rates as of [timestamp]" indicator. **Rate freshness SLA:** >4h old → non-dismissible freshness banner on any rate-displaying screen; >24h old → exchange operations (FR19) disabled with explanation; viewing historical rates and balances remains permitted.
- **NFR-I4:** Real-time connection handles network interruptions with automatic reconnection within 5 seconds.
- **NFR-I5:** CSV export format compatible with common spreadsheet applications (Excel, Google Sheets) with proper UTF-8 encoding for Arabic content.
- **NFR-I6:** Rate feed redundancy — gold-base pricing has two independent providers configured by MVP day 1 (one primary, one warm secondary with automatic failover within 60 minutes). Non-gold currency rates may share a single provider at MVP.

**Reliability**

- **NFR-R1:** 99.9% uptime target (< 8.76 hours downtime per year).
- **NFR-R2:** Zero data loss for committed transactions — every recorded debt, transfer, or exchange is durably persisted before confirming to the user.
- **NFR-R3:** Automated database backups with point-in-time recovery capability (minimum 30-day retention).
- **NFR-R4:** Health monitoring and alerting for critical system components (web tier, database, real-time notification service, exchange rate feed).
- **NFR-R5:** Graceful error handling — UI error messages include problem description + suggested corrective action; detailed error context in server logs; no silent failures.
- **NFR-R6:** Database integrity checks to detect and alert on balance calculation inconsistencies (sum differs from stored balance by more than smallest unit of denomination). Checks run daily; alerts within 1 hour.

### Additional Requirements

_Technical / infrastructure requirements from Architecture (§3-§7) and ADR-021…ADR-025 that shape Epic 1 (foundation) and inform downstream epics._

**Project scaffold & workspace (ADR-001, ADR-002, §3)**

- **AR-001:** Greenfield repo (no DiTracker port). Single git repo with `backend/` + `frontend/` + `infra/` + `docs/` + `contracts/` + `.github/workflows/` top-level layout.
- **AR-002:** Angular 21 workspace with **two build targets** in one workspace: `apps/owner-workspace` (authenticated SPA) + `apps/public-statement` (unauthenticated, token-scoped). Shared `libs/design-system`, `libs/domain-ui`, `libs/core`. Compile-boundary separation enforced by Angular project graph + ESLint boundary rule — public bundle cannot import from owner workspace (bundle-graph leak = build failure).
- **AR-003:** Angular 21 zoneless with Signals default; Signal Forms; PrimeNG v21 Aura preset ("Faktuboh Quiet" custom preset); Tailwind 4 layout-only; Transloco for i18n (AR + EN only); Tajawal + Inter self-hosted woff2.
- **AR-004:** Backend solution rooted in Aspire 13 with: `Faktuboh.Api` (Minimal APIs), `Faktuboh.Application`, `Faktuboh.Domain` (zero-dep, with `LegalHold` aggregate + bitemporal primitives), `Faktuboh.Infrastructure`, `Faktuboh.Contracts`. Tests: `Domain.Tests`, `Application.Tests`, `Api.Tests`, `Infrastructure.Tests` (Testcontainers Postgres), `ArchitectureTests` (NetArchTest).
- **AR-005:** Backend framework: **ASP.NET Core Minimal APIs + FluentValidation + source-generated OpenAPI** with problem-details handled by .NET 10 `AddProblemDetails()` + `IExceptionHandler` (ADR-024). Vertical-slice architecture: one folder per action — endpoint, DTOs, validator, handler, domain events, slice-private helpers.
- **AR-006:** Messaging / outbox: Wolverine with Postgres-backed transport (same-transaction outbox); 3 retries + exponential backoff; dead-letter Postgres table with replay tooling.
- **AR-007:** ORM: EF Core 10 + EFCore.NamingConventions (snake_case); Mapperly for DTO mapping; `ProcessingActivity` ambient context (`OwnerLedger` / `DebtorConfirmation` / `SystemBackground`) propagated via `AsyncLocal<T>` to logs, OTel spans, audit rows, and Wolverine message headers — enforced by NetArchTest.
- **AR-008:** TypeScript contracts: `contracts/` placeholder directory reserved; ESLint rule `no-handwritten-shared-contracts`; day-one **OpenAPI-drift gate** (Schemathesis or openapi-diff) comparing running API spec to committed `openapi.json` snapshot. Generator (NSwag / openapi-typescript / Kiota / Aspire-native) deferred to Step 5/6.

**Data & persistence (D4.1, D4.3, ADR-022)**

- **AR-009:** Database: PostgreSQL 16+ on Azure Database for PostgreSQL Flexible Server (Germany West Central; zone-redundant HA at production tier).
- **AR-010:** **Application-level bitemporal storage** (ADR-022) via EF Core `SaveChanges` interceptor + `<entity>_history` companion tables. Entities opt in via `IBitemporal` marker interface (`ValidFrom`, `ValidTo`, `RecordedAt`). Interceptor registered on every `DbContext`. Repositories expose `AsOf(DateTimeOffset)` query seam. CI-wired "audit round-trip" fitness test asserts every `IBitemporal` mutation produces exactly one history row.
- **AR-011:** Migration tooling: EF Core 10 migrations + Aspire-orchestrated idempotent **MigrationService** that installs/verifies bitemporal triggers + Postgres extensions on every cold start.
- **AR-012:** No Redis at MVP; Postgres holds idempotency keys, capability tokens, sessions. Public-statement edge caching via AFD (or SWA free tier per §7.4.1).
- **AR-013:** No formal CQRS at MVP; EF Core projections in Application-layer query handlers serve all reads.
- **AR-014:** Immutable append-only **audit log** with 7-year retention; anonymization on GDPR erasure preserves financial integrity (irreversible PII replacement with opaque IDs).

**Authentication & secrets (ADR-021, ADR-025, D4.5-D4.8)**

- **AR-015:** Identity provider: **Auth0 free tier** (EU tenant) on custom domain `auth.faktuboh.com` (ADR-021). CNAME + TXT DNS records; Auth0-managed certificate. OIDC integration via `Microsoft.AspNetCore.Authentication.OpenIdConnect`. MFA enabled. Password reset (FR3) and magic links (FR42) delegated to Auth0 Passwordless. Custom claims populated via Auth0 Actions. All callback URLs / token audiences / CORS origins registered at day one.
- **AR-016:** Public-statement token scheme: 128-bit opaque token in URL (base64url ≈22 chars). Server-side `capability_tokens` Postgres table. Edge cache keyed per-token. **Constant-time response** on unknown/expired tokens (no existence oracle). Token stripped from browser history via `history.replaceState` after first navigation. Tokens never logged (NetArchTest + Serilog/OTel redaction). In-band version byte for scheme migration.
- **AR-017:** Rate limiting: `Microsoft.AspNetCore.RateLimiting` middleware with `public-token` policy (per-token + per-IP-hash) + `owner-api` policy (per-authenticated-user). AFD WAF Standard ruleset additionally enforces: 120 req/min on `app.*`, 600 req/min on `statement.*/t/*`, OWASP core rules, block malformed capability-token probe shapes.
- **AR-018:** Envelope encryption (D4.8 per ADR-025): **Key Vault Standard** (FIPS 140-2 L1) stores master KEK in Germany West Central; per-subject DEK in `subject_keys` table wrapped by KEK; per-row IV. Quarterly KEK rotation. Erasure = destroy subject's DEK row (ciphertext becomes irrecoverable; audit math preserved). All access via Managed Identity — no service-principal secrets stored.
- **AR-019:** API error model (D4.9): RFC 9457 Problem Details with extensions (`traceId`, `code` in form `faktuboh.<slice>.<condition>`, `errors[]` with JSON Pointers). `ErrorCatalog` registry maps every domain code to `type` URI, HTTP status, Transloco translation key. NetArchTest asserts every `throw DomainException` references an ErrorCatalog code.
- **AR-020:** API versioning (D4.10): `/v1/` URL-path prefix from day one via `Asp.Versioning`; 12-month deprecation window; max 2 concurrent versions; **public-statement path pinned at `/v1/` forever** (URL is bookmarkable and lives in WhatsApp threads).
- **AR-021:** Idempotency (D4.11): `Idempotency-Key` HTTP header middleware (UUID v4 per form render; 24h TTL; row-level lock on handler enter) + per-endpoint natural-key DB constraints (domain-idempotent vs domain-creative classification). Returns 409 on key conflict mid-execution; cached response on key-replay-after-success.

**Frontend patterns (D4.13-D4.15)**

- **AR-022:** State management: Angular Signals + plain services + `rxResource()` / `resource()` by default. **NgRx Signal Store** permitted as explicit per-slice escape hatch when (a) derived state spans ≥2 slices, (b) paginated stale-while-revalidate exceeds `rxResource()`'s ergonomics, or (c) optimistic-update flows need `withEntities`. Slice adopting Signal Store documents the trigger in its slice README.
- **AR-023:** Forms (D4.14): Signal Forms exclusively. Validation layered — client hints + server-authoritative RFC 9457 `errors[]` walked onto Signal Forms nodes via `applyServerErrors(form, problemDetails)` helper. `FormMutationConfig` seam (`generateIdempotencyKey`, `onReset`, `onSuccess`). `ValidationCatalog` Transloco registry (sibling of ErrorCatalog).
- **AR-024:** Offline: No PWA at MVP (D4.15). Lightweight offline banner (`navigator.onLine`, RTL-aware). Service worker + manifest deferred to post-v1.

**Infrastructure & deployment (D4.16-D4.20, §7.4.1)**

- **AR-025:** Hosting: Azure Container Apps in Germany West Central. Production: `minReplicas=1`; Staging/dev: scale-to-zero. KEDA HTTP scaler tuned against 83 rps sustained SLO + cache-miss headroom. Container image: build once, promote staging→prod via ACA revision.
- **AR-026:** CI/CD: GitHub Actions with three workflows (`ci.yml` on every PR; `deploy-staging.yml` on merge to main; `deploy-prod.yml` on manual trigger or git tag with environment-protection required-reviewer + 10-min wait timer). `azd` drives deploys. **OIDC federated credential** on Entra ID workload identity — zero long-lived Azure credentials in GitHub. SHA-pinned actions.
- **AR-027:** CDN + WAF + static hosting (D4.19 — §7.4.1 revision pending): Azure Front Door Standard profile fronts both FE apps (Azure Static Web Apps) + ACA backend. Topology: `app.faktuboh.com` → owner SPA; `app.faktuboh.com/api/*` → ACA; `statement.faktuboh.com` → public-statement SPA; `statement.faktuboh.com/t/{token}` → cached per-token with ACA backend on miss. _§7.4.1 proposes Azure Static Web Apps free tier replaces AFD for €0 burn — cascade via `bmad-correct-course` before implementation._
- **AR-028:** Observability: Azure Monitor + Application Insights via Aspire 13 OTel auto-instrumentation. Adaptive sampling (5 items/sec per type); ingestion sampling 20%; force-keep all exceptions, 5xx, upstream sampled traces, outbox failures, auth failures. RFC 9457 `traceId` correlates FE+BE via W3C traceparent. Four dashboards (Golden signals / Outbox health / AFD+WAF / Business liveness). Four alerts (backend p95 >1s; error rate >2%; DLQ >50 rows; AFD cache-hit <90%). 30-day log retention. Audit-log seam separate — append-only Postgres trigger + quarterly archive to immutable Blob.
- **AR-029:** Transactional email (ADR-023): **Resend free tier**, region `eu-west-1` (GDPR residency). Routed through Wolverine outbox (no lost sends on Resend outage). Auth emails handled by Auth0.
- **AR-030:** Cost discipline: €0/month free-tier-only stack at MVP (per §7.4.1); €100/month ceiling during Months 1-12 Azure free-tier runway; €135/month ceiling at 10K users / €250/month at 100K users post-Month-13 cliff.

**Implementation patterns & conventions (§5)**

- **AR-031:** Database identifiers: snake_case via `EFCore.NamingConventions`; tables plural; FK pattern `<ref>_id`; indexes `ix_<table>_<cols>`; migrations `YYYYMMDDHHMMSS_<PascalCase>`. CI schema-audit test asserts identifier regex + Postgres reserved-word check.
- **AR-032:** REST endpoint naming: `/v{N}/<plural-kebab>` (e.g., `/v1/journal-entries`); noun sub-resources for actions with lifecycle, verb sub-paths for one-shot mutations. Spectral lint rule in CI.
- **AR-033:** JSON wire format: `camelCase` globally via `JsonNamingPolicy.CamelCase` (single `ConfigureJsonGlobally` extension). `[JsonPropertyName]` forbidden outside RFC-mandated allowlist. Per-endpoint Verify snapshot tests + Spectral OpenAPI lint.
- **AR-034:** Dates/times: `DateTimeOffset` in C#; `timestamptz` in Postgres; ISO-8601 with `Z` on the wire. `DateOnly` ↔ `date`; `TimeOnly` ↔ `time`. **Forbidden:** `DateTime`, `timestamp without time zone`, server-side date formatting for display. Statement period boundaries computed via `MonthBoundary(userTz, year, month)` helper.
- **AR-035:** Money: JSON `{ "amount": "1234.56", "currency": "EUR" }` — decimal-string + ISO 4217 uppercase. C# `Money` value object. Postgres user-facing `numeric(19,4)`; internal FX pivots `numeric(28,8)` (dual-precision rule per R-10). Frontend math via `libs/core/money` (wraps `decimal.js`) — raw arithmetic on `Money` fields is ESLint-forbidden.
- **AR-036:** Error code taxonomy: `faktuboh.<slice>.<condition>` (snake_case segments). Each code has unique GUID `id` (immutable cross-version anchor), HTTP status, Transloco key, `is_externally_visible` flag, predecessor `aliases[]`. NetArchTest + catalog snapshot test.

**Architectural fitness tests (§7.5.3)**

- **AR-037:** Four CI-wired NetArchTest invariants: (1) Shared Kernel purity — no dep on `Faktuboh.Api`; (2) Slice isolation — no cross-slice `Domain/` deps; (3) Cross-slice communication only via `Faktuboh.Domain.Events.IntegrationEvents`; (4) Shared Kernel holds no `IAggregateRoot` types. **CI-gate-first rule:** each test's CI wiring lands before the test content is marked done.
- **AR-038:** Glossary executable contract — `Glossary.cs` in Shared Kernel + string-ban fitness scanner covering three scaffold-time surfaces: API routes, Auth0 custom claim names, Postgres column names. UL mapping (ADR-013): Debt↔JournalEntry, Contact↔Account, Statement↔Statement, User↔Owner. Public-facing surfaces follow PRD; internal code follows architecture.

**Compliance & domain (§2.8-§2.9, D4.18)**

- **AR-039:** Data residency: Azure Europe (Germany West Central primary, Germany North paired for DR).
- **AR-040:** Legal disclaimer (FR7 / FR39) on registration AND every shared statement page — "User record, not a legally binding document." Watermark on PDFs when Phase 2 PDF export lands.
- **AR-041:** `LegalHold` as first-class Domain aggregate with state machine (Issued → Active → Released); blocks erasure pathways while Active; precedence rule resolves overlapping holds; persisted to audit log.
- **AR-042:** Cryptographic erasure per data class: transactional rows → key-shred pseudonymization (per-subject DEK destroy); derived aggregates → k-anonymity (k ≥ 5); contact PII → tombstone (hard delete + FK token).
- **AR-043:** Minimum-k policy on aggregate / export queries — refuse queries producing unique rows at freemium cardinality.

**Operational rehearsals (§2.10, §7.5.8)**

- **AR-044:** Pre-launch rehearsals required before first paying user: (1) restore-under-pressure tabletop (anonymization log replays as step zero); (2) cross-jurisdictional legal-hold drill; (3) rate-feed blackout + divergence drill (45-min outage, 2× spike, duplicate timestamps); (4) skeleton-to-authoritative CI synthetic (Slow 4G + 400ms RTT); (5) **public-statement throughput fitness test** (k6 166 rps for 10 min — 2× PRD headroom; pass = p95 <300ms, p99 <1.5s, zero 5xx, error <0.1%); (6) cache-sizing storm drill (500 rps burst for 30s — validates cache-sizing posture, not SLO).
- **AR-045:** Dogfood seed-data artifact authored before Day 25; `docs/development/dogfood-seed.md` + hand-authored seed SQL / Wolverine seed job / dev-mode endpoint.
- **AR-046:** Backup/restore rehearsal (RT-46) including KMS-encrypted field round-trip; secrets rotation dry-run (ADR-020) — Auth0 client-secret + KEK — quarterly cadence rehearsed end-to-end before first paying user.

### UX Design Requirements

_Actionable design work items derived from the UX Specification. Each UX-DR is specific enough to generate a story with testable acceptance criteria._

**Design tokens & theming**

- **UX-DR1:** Implement "Faktuboh Quiet" custom PrimeNG theme preset derived from Aura, exposing the complete light-mode token set (`--primary` Deep Teal `#0F766E`, `--accent-gold` Warm Gold `#B45309`, surface/text/success/warning/danger/divider tokens per Step 8 table) as CSS custom properties. Dark mode **deferred to v2** per Step 13 — do not define `@media (prefers-color-scheme: dark)` overrides at MVP.
- **UX-DR2:** Typography system: self-hosted Tajawal (Arabic, 300/400/500/600/700) + Inter (Latin) woff2 preloaded in `<head>` with `font-display: swap`. 9-level type scale (`--text-xs` 12px through `--text-5xl` 48px) as CSS custom properties. `font-feature-settings: "tnum" 1, "lnum" 1` on all numeric contexts (`AmountDisplay`, `ReconciliationMeta`, numeric columns).
- **UX-DR3:** Spacing/radius/shadow scale: 4px base unit; scale `0,2,4,8,12,16,20,24,32,40,48,64,96`; radius `6px` default / `8px` cards / `12px` modals / `9999px` pills; shadows minimal (cards `0 1px 2px rgba(0,0,0,0.06)`, modals `0 10px 38px rgba(0,0,0,0.18)`).
- **UX-DR4:** Hard **Brand vs System color separation**: logo orange `#ff8100` is logo-only, never referenced in any UI token, component, state, or copy. Enforced by ESLint + stylelint rule.

**Custom components — Phase 1 (MVP-critical)**

- **UX-DR5:** `AmountDisplay` standalone signal-based component — renders money with denomination badge, role-colored (receivable/payable/neutral), 4 sizes (sm/md/lg/xl=72px desktop/48px mobile), wraps numeric in `<output>`, `aria-label` reads naturally in active locale. Uses `Intl.NumberFormat` with active locale; Latin digits default in Arabic locale with user toggle for Eastern-Arabic (NFR-A9b).
- **UX-DR6:** `ContactAvatar` component wrapping PrimeNG `Avatar` — deterministic tint from 10-color palette (`--contact-bg-1..10` + matching `--contact-fg-*`) via hash of contact ID; grapheme-aware first-character extraction (handles Arabic combining marks, surrogate pairs). 4 sizes (xs/sm/md/lg = 24/32/40/56px).
- **UX-DR7:** `DebtRow` component — atomic row for the Glance; leading `ContactAvatar` + primary line (contact name) + secondary line (due date / note) + trailing `AmountDisplay` + optional status `Tag`. Fixed height 64px desktop / 72px mobile (every Glance Stage). Rendered as `<button>` with composed `aria-label`. Logical properties only. States: default / pressed / stale / overdue / settled.
- **UX-DR8:** `HeroTile` component — wraps PrimeNG `Card` with role-labeled header ("لي" / "عليّ"), `AmountDisplay` xl primary sum, per-denomination secondary sums (max 3 before "+N more" popover), debt count (Stage 2+). 4px `border-inline-start` role stripe flips per direction. States: default / empty / loading (skeleton).
- **UX-DR9:** `GlanceShell` composite page component implementing **Direction 6 Segmented Summary** layout — two HeroTiles at top + two parallel DataView lists + collapsed `Accordion` "المُسدَّدة (N)" footer. Responsive: hero side-by-side at all breakpoints (per breakpoint-independent invariant); SpeedDial FAB on mobile only (hidden ≥1024px); two inline "+" buttons replace FAB on desktop. Supports three Glance Maturity stages (Stage 1 hides sort/filter; Stage 2 shows sort Popover; Stage 3 shows sort + filter chips + paginator/virtualScroller on 51+ rows).
- **UX-DR10:** `DenominationPicker` component wrapping PrimeNG `Select` — two-section dropdown (Recent up to 3 + All alphabetical) with inline icons. States: default / open / disabled / limited (filtered for Exchange form compatibility). Model signal two-way binding.

**Custom components — Phase 2 (MVP-activation & sharing)**

- **UX-DR11:** `ContextualNudge` component wrapping PrimeNG `Message variant="simple"` — single mutating DOM region with 4 built-in kinds (`add-first-debt`, `share-first-statement`, `pending-confirmation`, `offline`). Fixed `--nudge-min-height: 56px` to prevent CLS; text swaps in place without transition. `role="status"` + `aria-live="polite"`. **Exactly one nudge at a time.**
- **UX-DR12:** `ReconciliationMeta` component — passive "Last reconciled: [date]" affordance with `<time datetime>` + tooltip showing absolute date. Three states: reconciled / never (renders null) / pending ("Shared, awaiting confirmation").
- **UX-DR13:** `StatementView` full-page component for public route `/s/:token` — minimal shell (no sidebar/app bar), lender `ContactAvatar` + name, parties strip, per-denomination xl `AmountDisplay`, read-only transaction list, confirmation CTA "تأكيد", signup CTA (post-confirmation only), legal footer. States: viewable / confirming (disabled with skeleton) / confirmed (CTA replaced by `ReconciliationMeta`) / revoked ("This statement is no longer available") / error. Locale toggle switches both `dir` and `lang` on `<html>`.
- **UX-DR14:** `GlanceFab` — canonical PrimeNG `SpeedDial` configuration (not a new component) for mobile add-debt FAB. Two MenuItems (لي with `pi-plus`, عليّ with `pi-minus`); `type="linear"`, `direction="up"`, `mask=true`, `hideOnClickOutside=true`. Hidden ≥1024px; pinned at `inset-inline-end: 24px; inset-block-end: 24px`.

**Custom components — Phase 3 (domain operations)**

- **UX-DR15:** `AuditBadge` component — clickable pill linking two records involved in Transfer or Exchange. Anchor/button wrapping PrimeNG `Tag`. 4 `kind` values (`transfer-from` / `transfer-to` / `exchange-prior` / `exchange-derived`). Direction-neutral icons (replace "↔" / "→" / "←" with `pi-link` to avoid RTL/LTR confusion). States: default / hover-focus / broken (defensive strikethrough).

**RTL / bilingual pipeline**

- **UX-DR16:** RTL-first CSS — logical properties exclusively in feature code (`margin-inline-*`, `padding-block-*`, `border-inline-start`, `inset-inline-*`, `text-align: start/end`). **Stylelint rule errors on physical properties** (`left`, `margin-left`, etc.).
- **UX-DR17:** `<html lang dir>` set per locale (`lang="ar" dir="rtl"` for Arabic; `lang="en" dir="ltr"` for English). Inline language switches use `<span lang=".." dir="..">` for mixed-direction content (English contact names inside Arabic layout). `dir="auto"` on root is forbidden.
- **UX-DR18:** Icon mirroring rule — semantic arrows (`pi-arrow-right`, `pi-chevron-right`) mirrored automatically in RTL via global CSS rule (`:where([dir="rtl"]) .pi-arrow-right { transform: scaleX(-1); }`). Checkmarks, plus/minus, stars, hearts NOT mirrored.
- **UX-DR19:** Numeral rendering: Latin (0-9) default in Arabic locale (per regional fintech convention); user-toggle to Eastern-Arabic (٠-٩). Grouping separator follows locale (1,234.56 in en / ١٬٢٣٤٫٥٦ in ar). Consistent across screens — mixed rendering within the same screen is an anti-pattern.

**Accessibility & preferences**

- **UX-DR20:** Automated a11y CI gate — `@axe-core/playwright` runs on every route-level E2E test; **zero violations at `wcag21aa`**, zero critical at `wcag21aaa`. Lighthouse CI thresholds: a11y ≥95, performance ≥85 desktop / ≥75 mobile throttled. `@angular-eslint/template-a11y` + custom stylelint logical-property rule.
- **UX-DR21:** Manual a11y test matrix per release candidate: VoiceOver Arabic (iOS 17+), TalkBack Arabic (Android 13+), NVDA Arabic (Win 11 + Firefox), VoiceOver English (macOS 14+), JAWS English. Primary Glance flow (create debt → view Glance → open detail → mark settled) must pass under VO-AR and TB-AR at minimum.
- **UX-DR22:** `prefers-reduced-motion: reduce` — global rule shortens all animations/transitions to 0.01ms; drawer slide / dialog fade / toast slide / skeleton shimmer / hover lift / tab underline all respect it.
- **UX-DR23:** `prefers-contrast: more` — body text → `#000` on `#fff`; borders 1px → 2px; role icons gain 1px outline; role-colored text uses `-800` variants; focus ring 3px; skeleton shimmer disabled.
- **UX-DR24:** Focus indicators use `:focus-visible` (not `:focus`); token `--focus-ring: 2px solid var(--primary); outline-offset: 2px`; never suppressed. Keyboard shortcut set (minimal v1): Esc closes overlays; Tab / Shift+Tab; Enter / Space activates; Arrow keys in menus; `/` focuses search on Stage 3 Glance only. No custom shortcuts in v1.
- **UX-DR25:** Touch-target minimum 44×44 CSS pixels globally (WCAG 2.5.5 AAA commitment). No hover-only behavior — every hover reveal has a long-press (500ms) or Popover equivalent on touch.

**Interaction patterns (Step 12)**

- **UX-DR26:** Button hierarchy — four tiers (Primary filled / Secondary outlined / Tertiary text / Destructive filled-danger or text-danger); **exactly one Primary per screen region**; `severity="success"` reserved for audit/confirmation surfaces only; `severity="warn"` forbidden on buttons; brand orange forbidden on buttons.
- **UX-DR27:** Feedback patterns — 4 channels: Silent success (routine CRUD, no toast) / Inline validation (p-invalid + `<small>`) / ContextualNudge (inline hints) / Toast (errors only; audit confirmations only). **No success toast for routine CRUD.** **No info toast ever** (use ContextualNudge).
- **UX-DR28:** Form patterns — Label strategy responsive (IftaLabel ≥1024px, stacked floating <1024px via responsive CSS class, no component swap). No red asterisks — required is default; optional fields labeled "(optional)". Client validation on-blur first then on-keystroke. Drawer bottom fullScreen <768px; side Dialog ≥768px. Numeric keyboards via `inputmode`.
- **UX-DR29:** Overlay rules — Dialog for irreversible confirmations only (Delete/Transfer/Send statement/Confirm received); Drawer for mobile nav/mobile forms/desktop contextual panels; Popover for contextual menus/row actions/small filter panels; Tooltip for icon-label/abbrev only (disabled on touch). **No nested Dialog.** **No Popover→Popover.**
- **UX-DR30:** Empty states — three archetypes (first-use / filtered / settled-column). **No decorative illustrations anywhere.** Quran slogan appears only on first-use empty state and signup page. First-use CTA is first focusable element after page load.
- **UX-DR31:** Loading states — 3 archetypes (skeleton matching resting geometry / inline button spinner with frozen width / passive refresh indicator 2px indeterminate progress at top-edge). **No full-screen blocking spinner ever.** Skeleton parent `aria-busy="true"`. Optimistic updates for debt create/edit/mark-settled with rollback on server reject.
- **UX-DR32:** Error recovery — 4 classes (offline via ContextualNudge severity=warn / retryable server error via inline Message + Toast retry / 409 conflict via inline warn with Reload / validation inline under field). All mutations carry idempotency key (ULID) so retry is safe. **`window.alert/confirm/prompt` never used.**
- **UX-DR33:** Share flow — softened default (single "Share statement" action, pre-filled culturally-appropriate Arabic message); relational recipient-page language ("Hi [Name], [Sender] shared a record of your shared transactions" — never "You have an outstanding balance"); no multi-tone picker at MVP; advanced custom-message option secondary.

**Responsive strategy (Step 13)**

- **UX-DR34:** Breakpoint system — Tailwind defaults + 3 product-critical thresholds: 360px minimum supported; 768px shell transition (drawer→sidebar collapsed); 1024px sidebar expand (240px). Segmented Summary two-column layout **never collapses to single stack** across breakpoints.
- **UX-DR35:** Per-surface responsive adaptation per Step 13 table — Glance / Debt Detail / Debt Form (drawer bottom fullScreen <768 / dialog 560px ≥768) / Contact Form / Statement View (centered 720px ≥768) / Signup / Settings / Onboarding.
- **UX-DR36:** Typography scaling — rem-based; no fixed-pixel font sizes; layout functional at 200% zoom without horizontal scroll; prose max-width 65-75ch; AR body `line-height: 1.75`; no Arabic italics; no full justification.

**Performance budgets (UX-correlated)**

- **UX-DR37:** Performance budget gates in CI: LCP <2.5s Slow 4G; INP <200ms (good threshold, <500ms fail); CLS <0.1; FCP <1.8s Slow 4G; TTFB <800ms; initial JS <180KB gz (public-statement); route-level lazy <60KB gz each; font total <120KB (Tajawal + Inter subset). Zoneless Angular mandatory.
- **UX-DR38:** Skeleton budget — skeletons replaced by real data within 1500ms on 4G; if >3000ms, swap skeleton for ContextualNudge ("Still loading — check your connection").

**Visual regression / RTL parity**

- **UX-DR39:** Visual regression suite — every new screen tested in both LTR and RTL via screenshot diff per CI; RTL parity verified per flow (columns flip, icons mirror correctly, logical properties honored). Component tests validate at Stage 1 (1 item), Stage 2 (10 items), Stage 3 (100 items) for data-bearing components.

### FR Coverage Map

| FR | Epic | Brief |
|---|---|---|
| FR1 | Epic 1 | Register account (Auth0 hosted) |
| FR2 | Epic 1 | Authenticate credentials |
| FR3 | Epic 1 | Password reset (Auth0 hosted flow) |
| FR4 | Epic 1 | Set preferred language (AR/EN) |
| FR5 | Epic 1 | View/edit profile |
| FR6 | Epic 1 | Delete account (GDPR erasure + anonymization) |
| FR7 | Epic 1 | View legal disclaimer |
| FR8 | Epic 2 | Add contact (first story bundles opening-balance per §7.5.2) |
| FR9 | Epic 2 | Edit contact |
| FR10 | Epic 2 | Delete contact (guarded on outstanding balances) |
| FR11 | Epic 2 | List contacts with per-denomination balance summary |
| FR12 | Epic 2 | Search/filter contacts by name |
| FR13 | Epic 2 & 3 | First instance in Epic 2 (opening-balance on Contacts/Create slice §7.5.2); general debt-recording flow lives in Epic 3 |
| FR14 | Epic 3 | Edit debt (audit preserved) |
| FR15 | Epic 3 | Delete debt (audit preserved) |
| FR16 | Epic 3 | View per-contact history + date-range filter + previous-balance rollup |
| FR17 | Epic 3 | Denomination preservation (no auto-conversion) |
| FR18 | Epic 6 | Hourly rate feed (gold as base) |
| FR19 | Epic 6 | Perform exchange with locked rate + override |
| FR20 | Epic 6 | View historical exchange rate |
| FR21 | Epic 6 | Denomination integrity enforcement |
| FR22 | Epic 6 | Store historical rates |
| FR23 | Epic 6 | View current system rate pre-confirmation |
| FR24 | Epic 5 | Transfer debt (full or partial) |
| FR25 | Epic 5 | Linked audit trail for transfer |
| FR26 | Epic 5 | View transfer history |
| FR27 | Epic 3 | Dashboard overview (contacts × denominations) |
| FR28 | Epic 3 | Per-contact statement with history + balance |
| FR29 | Epic 4 | Generate statement report (HTML view is the artifact; rich PDF = Phase 2) |
| FR30 | Epic 3 | CSV export of all data |
| FR31 | Epic 3 | Dashboard shows overall position without cross-currency aggregation |
| FR32 | Epic 3 | Dashboard bar chart (distribution across contacts) |
| FR33 | Epic 3 | Dashboard pie chart (distribution across denominations) |
| FR34 | Epic 4 | Generate shareable statement link |
| FR35 | Epic 4 | Recipient views statement without account (public SPA) |
| FR36 | Epic 4 | Recipient confirms statement (date recorded) |
| FR37 | Epic 4 | Owner sees last-confirmation date per contact |
| FR38 | Epic 4 | Shared page shows branding + signup CTA |
| FR39 | Epic 4 | Shared page shows disclaimer + watermark |
| FR40 | Epic 7 | Manage + revoke generated links (deferred from E4 per Victor's hypothesis-cut: <100 users in Phase A can be served by manual DB ops + server-side kill switch) |
| FR41 | Epic 4 | Cryptographic token generation (capability-token scheme AR-016) |
| FR42 | Epic 4 | Rate-limit: 20 links/user/hour (Auth0 Passwordless for magic-link refresh) |
| FR43 | Epic 8 | Admin dashboard (metrics) |
| FR44 | Epic 8 | Admin configures freemium thresholds |
| FR45 | Epic 8 | Admin manages grandfathering rules |
| FR46 | Epic 8 | Admin investigates + flags accounts (consumes FR53 fraud-report queue) |
| FR47 | Epic 8 | Admin suspends statement link generation |
| FR48 | Epic 8 | System enforces freemium limits (ships disabled at MVP) |
| FR49 | Epic 3 | Immutable audit log on debt operations (append-only storage primitive seeds in Epic 0; user-visible surfacing in Epic 3) |
| FR50 | Epic 7 | Real-time notification on counterparty confirmation (deferred from E4 per Murat + Victor: retention/trust signal, not viral-mechanism. Epic 4 MVP shows owner's contact list with "new confirmations" badge on next session open; SignalR infrastructure lands in E7) |
| FR51 | Epic 7 | SignalR push connection with auto-reconnect (deferred from E4: ~3-5 eng-days of hub wiring + reconnection + outage fallback that doesn't test viral-coefficient hypothesis) |
| FR52 | Epic 7 | Viral-loop share nudges |
| FR53 | Epic 7 | Recipient fraud-report affordance |

**FR spans** (annotated per Mary's traceability audit):

- **FR13** — Epic 2 (opening-balance-on-create per §7.5.2 first slice) + Epic 3 (general record-debt flow)
- **FR49** (audit log) — Epic 0 (append-only storage primitive, immutability DB constraint, `IBitemporal` interceptor) + Epic 3 (user-visible audit surface on edit/delete views)
- **FR2** (language switching) — Epic 1 (owner-app locale toggle + `<html lang dir>`) + Epic 4 (public-SPA locale toggle — independent build target needs its own i18n wiring)
- **FR7** (legal disclaimer) — Epic 1 (registration surface + owner-app disclaimer page) + Epic 4 (public-statement page footer disclaimer)

Cross-cutting instrumentation requirement: every epic delivering Phase-A-metric-relevant behavior must emit its corresponding telemetry event, verified by integration test (see AR-047 below).

**All 53 FRs mapped.** FR13 is the single shared entry (Epic 2's first story exercises it via opening-balance; Epic 3's flow is the general debt-recording home — see story-level notes in Step 3).

## Epic List

### Epic 0: Platform Foundation

**Goal:** Stand up the greenfield scaffold — backend solution, Angular two-target workspace, CI gates, Auth0 tenant, Postgres with application-level bitemporal interceptor, Wolverine outbox, and the minimum set of hard gates that MUST hold before Epic 2's first slice merges. Contains **no user-visible FRs**; every story here is AR-coverage. Exists as a distinct epic (not merged into Epic 1) because ~14 scaffold stories deliver no user-facing value and would crowd out the shell epic and blow the Day 35 dogfood target.

**Murat's hard-gate split** (party-mode review): Epic 0 ships only the items that must be green before any slice can merge. The remaining items ship incrementally during Epic 2/Epic 3 as they become testable against real slices. This cuts Epic 0 from ~10 engineer-days to ~6 without losing any invariant.

**Hard gates — MUST ship in Epic 0 (blocking E2 first-slice merge):**

- **AR-037a:** NetArchTest #1 — Shared Kernel purity (load-bearing; violation in E2 = untangling Epic 5)
- **AR-037c:** NetArchTest #3 — Cross-slice communication only via IntegrationEvents (the compound E2 slice emits `ContactCreated`; can't assert boundary without this gate)
- **AR-019:** RFC 9457 Problem Details middleware + `ErrorCatalog` registry (E2 slice's negative-path tests require this; shipping without it = happy-path-only confidence)
- **AR-021:** Idempotency-Key middleware + row-level locking + per-endpoint natural-key support (opening-balance-on-create is the canonical idempotency test case)
- **AR-010 + AR-011:** `IBitemporal` interceptor + MigrationService + audit round-trip fitness test against a stub entity
- **AR-007:** `ProcessingActivity` ambient context via `AsyncLocal<T>` (required if audit assertions are in E2 ACs)
- **AR-015:** Auth0 tenant provisioned + custom domain `auth.faktuboh.com` DNS (CNAME + TXT) + callback URLs registered
- **AR-004 + AR-005 + AR-006 + AR-009:** Backend solution structure (Api / Application / Domain / Infrastructure / Contracts) + Aspire AppHost + Minimal APIs + FluentValidation + source-gen OpenAPI + Wolverine Postgres transport + Postgres Flex Server provisioned
- **AR-001 + AR-002 + AR-003:** Repo layout + Angular two-target workspace + compile-boundary separation + bundle-graph CI gate + zoneless + PrimeNG v21 Aura + Transloco + Tailwind layout-only + Tajawal + Inter self-hosted
- **AR-026:** GitHub Actions three-workflow CI/CD (ci.yml + deploy-staging + deploy-prod) + OIDC federated credential
- **AR-025:** ACA environment (Germany West Central) + SWA provisioned for both FE targets
- **AR-028:** App Insights wiring + Aspire OTel auto-instrumentation + `traceId` correlation
- **NFR-A9 RTL acceptance gate — definition:** the six-scenario gate is **defined in Epic 0** as a CI fitness test harness (list of testable scenarios + dual-device reviewer matrix documented); compliance ACs are required per-surface in Epics 1 / 3 / 4. Ship-blocker for MVP.
- **NFR-SC4 load test baseline harness:** k6 scaffolding + initial no-op script committed; endpoints added per epic as they land.
- **AR-047 (NEW): Phase-A telemetry primitive** — App Insights event-emission SDK + PII-safe event schema + integration-test helper asserting events fire. Every epic delivering Phase-A-metric-relevant behavior inherits the AC to emit its events (E1: `user_signup`; E3: `first_debt_recorded`, `debt_recorded`; E4: `statement_shared`, `statement_viewed`, `viewer_signup_from_share`). Without this, viral coefficient cannot be measured.
- **AR-048 (NEW): Mutation testing on domain layer** — Stryker.NET on `Faktuboh.Domain` (Money VO, bitemporal interceptor when moved, capability-token validator, rate-feed reconciler). Runs weekly; 1 day initial setup + 2h/week triage budget. Mechanical substitute for the solo-dev no-pair-review gap.
- **AR-049 (NEW): Compound-Journal-Entry interface seam** — abstraction for multi-leg balanced atomic journal writes defined at Domain layer; consumed by Epic 5 (Transfer) and Epic 6 (Exchange) unchanged. Decouples the E5→E6 dependency. Stub implementation only in E0; real usage in E5.
- **AR-030:** €0/month free-tier discipline baseline + €50/mo spend alert wired in App Insights

**Deferred to E2/E3 (layer in as stories when testable against real slices):**

- AR-037b NetArchTest #2 slice isolation (AR-037c catches 80% of violations; add in E3 when second slice exists)
- AR-037d NetArchTest #4 no-IAggregateRoot-in-SK (low violation probability first two slices)
- AR-038 Glossary.cs ships as file in E0; string-ban scanner as PR-check in E2
- AR-008 OpenAPI-drift gate (needs a baseline; E2 first real endpoint)
- AR-018 envelope encryption seam (no PII in Contacts-with-opening-balance; layer in E3 when debt notes land)
- AR-045 dev seed-data (hand-rolled fixtures suffice E1; proper seed ergonomics in E3)
- Bitemporal migration rollback automation (document runbook in E0; rehearse via CI dry-run job in E3)
- AR-012 idempotency schema-audit gates (layer incrementally; interceptor is the gate that matters first)
- AR-032 / AR-033 REST/JSON Spectral lint (add when first real endpoint lands in E2)
- AR-036 error code taxonomy catalog snapshot (baselines in E2)
- AR-020 `/v1/` URL versioning (exercised in E2; setup in E0)
- AR-022 / AR-023 Angular state + Signal Forms `FormMutationConfig` scaffolding (E1 exercises; E3 standardizes pattern)
- AR-024 offline banner (E1 lands the primitive)
- AR-027 full AFD topology (E4 when public-statement goes live; E0 provides SWA baseline)
- AR-029 Resend integration (E1 when password-reset flow exercises; Auth0 handles auth-email)
- AR-031 / AR-034 / AR-035 full CI-wired enforcement (scaffold in E0; enforce when exercised)
- AR-039 (region already set per AR-025); AR-040 (disclaimer copy E1); AR-041 (`LegalHold` domain stub E0, full state machine when needed); AR-042 (per-class erasure E1 account-delete)
- **AR-044 operational rehearsals → Epic 8** (rehearsals need real production data; can't rehearse restore against nothing)
- **AR-046 rotation dry-run → Epic 8** (same rationale)

**Dependency:** None. **Exit criterion:** all hard gates above green in CI; Auth0 tenant reachable at `auth.faktuboh.com`; MigrationService idempotent across cold restarts; audit round-trip fitness test passes against a stub `IBitemporal` entity; telemetry helper emits an assertable event.

**Schedule discipline (Murat):** **RT-44 descope trigger moves to Day 30** (leading indicator, not Day 42 trailing). A **pre-committed scope-cut list** sits in a dormant PR branch at Day 0: if the Day 30 checkpoint is red, merging that PR applies the cut and restores dogfood by Day 38-40 with invariants intact. Pre-committed cuts: further Epic 4 reductions (cost-circuit-breaker simplification + any Phase-2 leakage in E4 scope).

---

### Epic 1: First-Run Shell & Identity

**Goal:** A new visitor can land on Faktuboh, register with email + password, log in via Auth0, choose Arabic or English (RTL flips), read the legal disclaimer, view and edit their profile, and request account deletion. They land on an empty Glance skeleton — the "لي" / "عليّ" hero tiles reading zero, two empty column placeholders, and a single contextual nudge prompting "add your first debt." This epic is the product's first impression and the empty-state checkpoint before any real data flows.

**FRs covered:** FR1, FR2 (owner-app portion; public-SPA portion spans to E4), FR3, FR4, FR5, FR6, FR7 (owner-app portion; public-SPA portion spans to E4).

**Component API ownership (Mary's single-owner rule):** Epic 1 is the authoritative owner of all Phase-1 UX component APIs (UX-DR5…UX-DR10) and the `ContextualNudge` component API (UX-DR11). Epics 3 / 4 / 7 consume these components; if they need new `kind` values or variants, those land as PRs against the components' home in `libs/design-system` / `libs/domain-ui`, not as rewrites.

**Primary AR / UX-DR coverage:** AR-015 (Auth0 integration — callback URLs, token validation, hosted-login redirect, Passwordless for Phase-2 magic-link), AR-017 (rate limiting middleware baseline for auth endpoints), AR-023 (Signal Forms for registration/login/profile), AR-024 (offline banner skeleton), AR-031…AR-036 (conventions exercised on first user-visible endpoints), AR-040 (legal disclaimer surfaced on registration), AR-042 (key-shred erasure on account delete via ADR-004), AR-047 (emits `user_signup` telemetry event), UX-DR1…UX-DR4 (Faktuboh Quiet theme preset, typography, spacing, brand-orange separation), UX-DR5–UX-DR10 (**APIs owned here**; Phase-1 components AmountDisplay, ContactAvatar, DebtRow, HeroTile, GlanceShell, DenominationPicker — wired to empty/mock data so they exist, styled, RTL-correct, a11y-tested), UX-DR11 (**API owned here**; ContextualNudge with only `add-first-debt` kind wired; other kinds added in consuming epics as additive PRs), UX-DR16…UX-DR19 (RTL pipeline — logical properties, lang/dir, icon mirroring, numeral rendering), UX-DR20…UX-DR25 (a11y gates — axe CI, manual screen-reader matrix, reduced-motion, contrast-more, focus-visible, touch-target minimums), UX-DR26…UX-DR32 (interaction patterns: button hierarchy, feedback, forms, overlays, empty states, loading states, error recovery — instantiated on the shell surfaces), UX-DR34…UX-DR36 (responsive breakpoints + per-surface adaptation + typography scaling), UX-DR37 (perf budget CI gates wired), UX-DR38 (skeleton budget), UX-DR39 (visual regression suite lit up for LTR + RTL on every shell screen).

**NFR-A9 compliance AC:** all Epic 1 surfaces (registration, login, profile, password reset, empty Glance, disclaimer) pass the six-scenario RTL acceptance gate defined in Epic 0. Ship-blocker for MVP.

**Dependency:** Epic 0.

---

### Epic 2: Contacts — Know Your Counterparties (First Slice)

**Goal:** The user can add counterparties — optionally with an opening balance recorded atomically in the same operation — then edit, delete (guarded on outstanding balances), list, and search them with per-denomination balance summaries per contact. **The first story in this epic IS the §7.5.2 Contacts/Create-with-opening-balance slice** — the deliberately designed first feature slice that exercises Money VO + `Idempotency-Key` middleware + natural-key constraint + bitemporal write + RFC 9457 emission + cross-slice integration event (`ContactCreated` → no-op Debts projection handler) end-to-end. Fitness test #3 is validated here for the first time against real cross-slice traffic.

**FRs covered:** FR8, FR9, FR10, FR11, FR12, FR13 (first instance via opening-balance).

**Primary AR / UX-DR coverage:** AR-021 (Idempotency-Key fully exercised with financial consequence), AR-035 (Money value object + dual-precision first landed on wire), AR-037 (fitness tests #1-4 assertable against a real slice), UX-DR6 (ContactAvatar real data binding), UX-DR28 (Contact form — Drawer mobile / Dialog desktop), UX-DR29 (ConfirmDialog pattern on delete-with-balance warning), UX-DR33 not yet relevant.

**Dependency:** Epic 0, Epic 1.

---

### Epic 3: Record Debts, See the Glance & Export

**Goal:** The user can record debts lent or borrowed with any contact in any supported denomination (currency or precious metal) with optional story/context, edit and delete them with preserved audit trail, view per-contact full history with date-range filter + previous-balance rollup, see the full Glance populated at every Maturity Model stage with Direction 6 Segmented Summary + per-denomination hero sums + distribution charts, and export all their data as CSV. Audit log surface becomes user-visible. This is the core product-value delivery.

**FRs covered:** FR13 (general debt-recording flow — spans E2 for opening-balance variant), FR14, FR15, FR16, FR17, FR27, FR28, FR30 (CSV export — per party-mode consensus), FR31, FR32, FR33, FR49 (user-visible audit surface — spans E0 for storage primitive).

**Primary AR / UX-DR coverage:** AR-010 (bitemporal on every debt mutation), AR-014 (audit log on every mutation), AR-035 (Money across every codepath), AR-037b + AR-037d (layering NetArchTest #2 slice-isolation + #4 no-IAggregateRoot-in-SK when second/third real slices exist), AR-038 (Glossary string-ban scanner activated), AR-008 (OpenAPI-drift gate baselines once first real endpoints ship), AR-018 (envelope encryption seam lit up when debt notes land), AR-047 (emits `first_debt_recorded` + `debt_recorded` telemetry events), UX-DR5 (AmountDisplay all sizes in production), UX-DR7 (DebtRow all states — default/pressed/stale/overdue/settled), UX-DR8 (HeroTile all states — default/empty/loading), UX-DR9 (GlanceShell Stages 1→3 with progressive disclosure of sort + filter + paginator/virtualScroller), UX-DR10 (DenominationPicker with Recent/All sections — consumed from E1's component API), UX-DR14 (GlanceFab SpeedDial on mobile), UX-DR22…UX-DR23 (reduced-motion, contrast-more honored on live data surfaces), UX-DR27 (silent success pattern — no toast on routine CRUD), UX-DR31 (three loading archetypes + optimistic updates with rollback), UX-DR38 (skeleton budget on Glance), UX-DR39 (stage-1 / stage-2 / stage-3 visual regression).

**NFR-A9 compliance AC:** CSV export passes scenario (e) — RTL column order reversed for Arabic locale; UTF-8 with BOM; currency symbols positioned per-convention. Dashboard charts pass scenario (d) — legends, axes, tooltips flow right-to-left. Ship-blocker for MVP.

**Dependency:** Epic 2.

---

### Epic 4: Viral Loop MVP — Statement, Share & Confirm

**Goal:** Close the viral loop end-to-end at its thinnest viable shape that validates the Phase-A viral-coefficient hypothesis (≥0.3): the owner can generate a cryptographically-unique shareable link for any contact's statement; the recipient can open the link on any device without an account, see a bilingual read-only statement with legal disclaimer + sign-up CTA designed as the primary UX, and tap "Confirm" to timestamp agreement (the PRD-named viral moment); the owner sees last-confirmation dates passively (non-real-time) on their contact list. The full viral funnel is instrumented (link opened → scrolled past fold → CTA viewed → CTA clicked → signup completed → first debt recorded). Architectural commitments (capability-token scheme, 180KB public-statement bundle budget, constant-time-response on unknown/expired tokens, two-target compile-boundary enforced by CI bundle-graph gate, `apps/public-statement` as a distinct Angular build target) all land here atomically. **The scope is deliberately cut to 9 FRs, not 12** — SignalR (FR50/FR51) and owner-side link management (FR40) deferred to Epic 7 per party-mode consensus: retention/polish features that don't test the viral hypothesis.

**FRs covered:** FR29 (HTML statement; rich PDF = Phase 2), FR34, FR35, FR36, FR37 (passive display of last-confirmation date — no real-time wiring), FR38 (**elevated to primary UX focus**; funnel-instrumented; signup page pre-fills recipient name from statement), FR39 (one-line disclaimer; watermark deferred), FR41, FR42.

**FR spans:** FR2 (language toggle on public-SPA build target); FR7 (disclaimer rendered on statement page).

**Primary AR / UX-DR coverage:** AR-016 (capability-token scheme — Postgres `capability_tokens`, AFD cache-key, URL-rotation, constant-time response, token stripped from history), AR-017 (rate limiting on public-token route — 20 links/hour/user cap + AFD edge rule 600 req/min per token), AR-027 (CDN topology — `statement.faktuboh.com` fronting `apps/public-statement` SPA), AR-050 (NEW — Epic 4 cost circuit-breaker: per-token day-view cap at 1000 views/token/day → static "high volume" page beyond; daily spend alert at €50/month trigger paging owner; hard kill-switch env flag disabling public statement access in 60 seconds; AFD aggressive 24h cache TTL on statement content with cache-bust on owner edit), AR-047 (emits viral-funnel events: `statement_shared`, `statement_viewed`, `cta_viewed`, `cta_clicked`, `viewer_signup_from_share`), UX-DR9 (GlanceShell surfaces last-confirmation via ReconciliationMeta), UX-DR11 (ContextualNudge adds `pending-confirmation` kind for owner side — PR against E1's component API), UX-DR12 (ReconciliationMeta on contact detail + statement page), UX-DR13 (StatementView full-page component — the public-statement SPA page, **designed as landing page not document viewer**: promised debt-summary value above-the-fold; Faktuboh framing + signup CTA pre-filled with recipient name below-the-fold), UX-DR33 (softened share flow with culturally-appropriate Arabic default message).

**Murat's additional test stories (budget ~10.5 engineer-days):**
- Constant-time-response timing test (statistical p99 comparison across 10k valid-vs-invalid token requests) — silent-failure landmine for timing oracles
- k6 public-statement throughput harness — 166 rps sustained for 10 minutes (2× PRD headroom), pass: p95 <300ms, p99 <1.5s, zero 5xx, error <0.1%
- k6 cache-sizing storm drill — 500 rps burst for 30s, verify cache-hit >95% at AFD edge + constant-time-response holds + origin doesn't melt
- Capability-token property-based tests (FsCheck) on adversarial token shapes (truncated, tampered, replay, expired-by-1ms, wrong-audience)
- Capability-token-replay-across-cache-boundary test (staged against deployed AFD environment) — catches Vary-header misconfiguration silent failures
- WAF smoke-test suite (10-15 malicious-pattern probes post-deploy)

**NFR-A9 compliance AC:** StatementView passes scenarios (a) mixed-script bidi isolation, (b) numeral rendering, (c) transaction-note bidi isolation on RTL-native reviewer devices. Ship-blocker.

**Entry criterion (per Winston's caveat):** Pre-commit fallback strategy documented — if Angular 21 zoneless two-target compile-boundary cannot hit 180KB gz budget, a server-rendered-HTML public route is the pre-agreed fallback, scoped and estimated before Epic 4 stories start.

**Dependency:** Epic 3.

---

### Epic 5: Debt Transfer Between Parties

**Goal:** The user can transfer all or part of an outstanding debt from one contact to another in a single atomic journal entry — one debt closes on the source, an equivalent one opens on the target, both sides carry bidirectional audit badges the user can tap to trace the chain. Partial transfers supported (per PRD Journey 2: Karim transferring $200 of a $500 debt). Review step mandatory — the one routine action that shows a confirmation dialog.

**FRs covered:** FR24, FR25, FR26.

**Primary AR / UX-DR coverage:** AR-010 (bitemporal write on two linked aggregates in one transaction), AR-021 (idempotency-key on the compound operation), AR-049 (Compound-Journal-Entry interface seam from Epic 0 — **first real consumer**; Epic 6 consumes unchanged), UX-DR15 (AuditBadge component — the Phase-3 custom component; API owned by E5 and consumed by E6), UX-DR29 (Transfer review Dialog — the single routine action with a review step).

**Dependency:** Epic 3 + Epic 0 (AR-049). **Rationale for ordering (per party-mode consensus):** simpler than Exchange — single transaction, no external feed integration, no rate-lock semantics. Ships first to prove the dual-entry pattern that Exchange will reuse. **Schedule-crunch cut option (Mary):** if Epic 5 is cut, FR24-26 fall out cleanly (feature-tier cut, not ship-blocker); Epic 6 consumes AR-049 directly and ships unchanged because the abstraction is stable in Epic 0.

---

### Epic 6: Currency & Metal Exchange with Locked Rates

**Goal:** The user can convert a debt from one denomination to another at a locked rate — system proposes the current hourly rate, user can override, the rate stays bound to the transaction forever. The original denomination is preserved in the audit trail (Journey 5). Rates refresh hourly with graceful degradation: >4h stale → non-dismissible banner; >24h stale → FR19 exchange operations disabled while viewing historical rates/balances remains permitted (NFR-I3). Dual-provider redundancy for gold-base pricing.

**FRs covered:** FR18, FR19, FR20, FR21, FR22, FR23.

**NFR ownership (Mary's clarification):** Epic 6 owns both the NFR-I3 **background freshness worker** (scheduled rate-feed ingestion + freshness signal emission) and the NFR-I3 **UX behaviors** (banner at >4h, disable at >24h). Epic 0 provides only the generic `Wolverine` scheduled-job primitive; Epic 6 owns the rate-freshness domain logic. Epic 6 also owns NFR-I6 (dual-provider redundancy) end-to-end with integration-test fixture for provider failover.

**Primary AR / UX-DR coverage:** AR-006 (Wolverine scheduled rate-feed ingestion job + outbox safety on feed failure), AR-010 (bitemporal write on compound exchange entry), AR-035 (dual-precision rule exercised — internal FX pivots `numeric(28,8)`, user-facing `numeric(19,4)` with rounding-at-edge), AR-049 (Compound-Journal-Entry interface — consumed from E0 without modification), AR-044 item 3 (rate-feed blackout + divergence drill — 45-min outage, 2× spike, duplicate timestamps — rehearsed before Epic 6 exits; stays in E6 because it's feed-specific, not post-production operational), UX-DR10 (DenominationPicker `limited` variant — target denominations filtered for compatibility), UX-DR15 (AuditBadge for `exchange-prior` / `exchange-derived` linked records — consumed from E5 component API), UX-DR29 (Exchange rate-lock Dialog).

**Murat's test commitments:** WireMock.NET cassette harness for 8-12 feed scenarios (blackout, partial blackout, divergence above/below threshold, duplicate timestamp, out-of-order timestamp, stale 4h / 24h, schema drift, auth failure) — never against live providers; Pact consumer-side drift tests (1 day per provider); property-based tests on the dual-provider reconciliation arbiter (1.5 eng-days); numeric(28,8) round-trip precision property tests across currency conversion chains of length 1–5.

**Dependency:** Epic 3, Epic 0 (AR-049). Epic 5 is **preferred predecessor but not required** — the compound-journal-entry interface is stable in Epic 0, so Epic 6 can ship without Epic 5 if schedule forces the cut.

---

### Epic 7: Real-Time Notifications, Share Nudges, Fraud Reporting & Link Management

**Goal:** Amplify the viral loop that Epic 4 proved. Four deferred-from-E4 capabilities land here because they're retention/polish rather than hypothesis-test: (1) SignalR real-time push notifying the owner the moment a recipient confirms (upgrading Epic 4's passive next-session badge); (2) owner-side link management + revocation UI (Epic 4 served <100 Phase-A users via manual server-side kill switch); (3) contextual share-nudges driving the 30%+ viral signal (prompts after first 3 debts per contact, after 7-day unshared staleness, on material-threshold balance crossings, with dismiss-cooldown); (4) recipient fraud-report affordance queuing to admin review.

**FRs covered:** FR40 (manage + revoke links), FR50 (real-time notification on counterparty confirmation), FR51 (SignalR push with auto-reconnect), FR52 (share nudges), FR53 (fraud-report affordance).

**Primary AR / UX-DR coverage:** SignalR infrastructure (hub wiring, automatic reconnection within 5 seconds per NFR-I4, auth context on the socket, outage fallback), `AR-050 cost-circuit-breaker graduated` (SignalR concurrent-connection caps), UX-DR11 (ContextualNudge gains remaining kinds: `share-first-statement`, staleness-reminder, threshold-crossing — PRs against E1's component API), UX-DR27 (feedback pattern reinforcement — report confirmation is neutral to protect recipient from retaliation signal), UX-DR12 (ReconciliationMeta state upgraded from `pending` to real-time-reactive when SignalR event arrives).

**Dependency:** Epic 4.

---

### Epic 8: Platform Operations, Admin & Operational Safety Jobs

**Goal:** The operations team member (a single-person role at MVP — Wahid himself) can monitor platform health, investigate abuse flagged from fraud-reports (FR46 consumes the Epic 7 queue), suspend a user's statement-link generation, configure freemium thresholds and grandfathering rules (thresholds ship disabled at MVP per PRD Revenue-readiness gate; enforcement UX already implemented per UX-DR from Epic 0/3; this epic flips the switch when the Revenue-readiness trigger fires), and view platform metrics (active users, signups, statement-link generation rate, confirmation rates). Admin-by-direct-Postgres-query is an acceptable MVP pattern per PRD P1 tier — this epic formalizes what becomes dashboard-backed after first-50-user scale. Epic 8's charter is expanded per Mary's audit to include **operational safety jobs** that can only land post-production-data (balance-integrity checks, backup/restore rehearsals, secrets rotation dry-runs).

**FRs covered:** FR43, FR44, FR45, FR46, FR47, FR48.

**Primary AR / UX-DR coverage:** AR-028 (App Insights dashboards reused as admin data source), AR-043 (minimum-k policy on any aggregate admin queries), **AR-044 (all operational rehearsals — moved from Epic 0 per Mary's audit: restore-under-pressure tabletop, cross-jurisdictional legal-hold drill, backup/restore with KMS-field round-trip, public-statement throughput fitness test re-run at scale)**, **AR-046 (secrets rotation dry-run — Auth0 client-secret + KEK — moved from Epic 0 per Mary's audit: rotation requires real secrets in real environments)**, **AR-051 (NEW: NFR-R6 daily balance-integrity job — scheduled Wolverine job comparing sum(transaction amounts) vs stored balance per contact-denomination pair; alert within 1 hour of detection via admin dashboard; prior "orphan NFR" per Mary's audit)**, **NFR-SC4 full load test execution** at production-representative scale (harness committed in Epic 0; Epic 8 executes and triages).

**Dependency:** Epic 4, Epic 7 (admin investigates FR53 fraud-report queue).

---

## Epic Summary

| # | Epic | FRs covered | Dependencies | User-visible |
|---|---|---|---|---|
| 0 | Platform Foundation | — (AR-only; hard gates) | — | No |
| 1 | First-Run Shell & Identity | FR1–7 (7; FR2, FR7 span to E4) | E0 | Yes |
| 2 | Contacts — First Slice | FR8–12, FR13 opening-balance (5+1 shared) | E0, E1 | Yes |
| 3 | Record Debts, See the Glance & Export | FR13, 14–17, 27–28, 30, 31–33, 49 (11; FR13 spans from E2, FR49 spans from E0) | E2 | Yes |
| 4 | Viral Loop MVP | FR29, 34–39, 41–42 (9) | E3 | Yes |
| 5 | Debt Transfer Between Parties | FR24–26 (3) | E3, E0 (AR-049) | Yes |
| 6 | Currency & Metal Exchange | FR18–23 (6) | E3, E0 (AR-049) | Yes |
| 7 | Real-Time + Nudges + Fraud + Link Mgmt | FR40, 50–53 (5) | E4 | Yes |
| 8 | Platform Ops + Admin + Operational Safety | FR43–48 (6) | E4, E7 | Operator-facing |
| | **Total distinct FRs covered** | **53** | | |

**Critical path for Day 35 dogfood:** E0 → E1 → E2 → E3 → E4. Shipping this chain tests the core hypothesis (record debt + share link + recipient signs up → viral coefficient). Epics 5/6/7/8 are layered on top and do not block dogfood validation.

**Schedule reality (Murat):** Estimated engineer-days: E0 ~6 (post-hard-gate-split) + E1 ~5 + E2 ~5 + E3 ~10 + E4 ~12 (post-SignalR-defer and cost-circuit-breaker scope) = ~38 days. Day 35 is tight but not impossible. **RT-44 descope trigger moves to Day 30** (leading indicator); pre-committed scope-cut PR sits dormant from Day 0 with further E4 cuts ready to merge if Day 30 checkpoint is red. Mutation testing (Stryker.NET) on domain layer runs weekly as the mechanical substitute for no-pair-review solo-dev context.

**Deliberate structural decisions captured from party-mode rounds (both rounds):**

1. **Epic 0 split from Epic 1** (Amelia) — scaffold stories as a distinct epic with no user FRs prevents the "3-week slog with no user-visible progress" failure mode.
2. **Epic 0 scope split into hard gates vs deferred** (Murat) — only 14 items block E2 first slice; others layer in during E2/E3 as testable-against-real-slices opportunities arise.
3. **Viral loop pulled forward to Epic 4** (Sally + John) — the product's differentiator cannot ship sixth when the Phase-A success metric depends on measuring it.
4. **Epic 4 scope cut from 12 to 9 FRs** (Murat + Victor convergence) — SignalR (FR50/FR51) and owner-side link management (FR40) deferred to Epic 7; they're retention/polish, not hypothesis-test. Epic 4 MVP uses passive next-session badge for confirmation feedback instead of real-time push.
5. **FR38 signup CTA elevated to primary UX focus** (Victor) — designed as the statement-page's landing-page primary conversion moment with funnel-instrumented A/B harness, not bullet-10-of-12.
6. **Cost circuit-breaker lands in Epic 4** (Victor) — AR-050: per-token day-view cap + daily spend alert + hard kill-switch + AFD 24h TTL. Shipping without this = one Twitter moment = cost incident.
7. **Transfer before Exchange** (unanimous consensus) — Transfer is structurally simpler and proves the dual-entry pattern Exchange will reuse.
8. **Compound-Journal-Entry interface stabilized in Epic 0** (Mary, AR-049) — decouples E5→E6 hard dependency; Epic 6 can ship without Epic 5 if schedule forces the cut.
9. **CSV export in Epic 3, not Epic 8** (unanimous) — GDPR right-to-export is a user right tied to debt data, not an admin feature.
10. **§7.5.2 first slice honored atomically** (Amelia) — Epic 2's first story IS Contacts/Create-with-opening-balance, not split across Epic 2 (contact) + Epic 3 (debt).
11. **Epic 4 entry criterion requires bundle-budget fallback** (Winston) — if 180KB gz + LCP <2.5s can't be hit on the two-target compile-boundary, SSR-rendered HTML for the public route is the pre-agreed fallback, scoped before stories begin.
12. **Audit log split** (Winston) — append-only storage primitive in Epic 0; user-visible audit surface in Epic 3.
13. **UX-DR11 ContextualNudge single-owner rule** (Mary) — Epic 1 owns the component API; Epic 4 / Epic 7 add new `kind` values via PRs against the component's home, not rewrites.
14. **Orphan NFRs given homes** (Mary):
    - NFR-A9 RTL ship-blocker gate **defined in Epic 0**; compliance ACs required per-surface in Epics 1/3/4.
    - NFR-SC4 load test **baseline harness in Epic 0**; endpoints added per epic; full execution in Epic 8.
    - NFR-R6 balance-integrity check **scheduled job in Epic 8** via new AR-051.
    - NFR-I3 (rate freshness) entirely in Epic 6 (worker + UX, not double-claimed).
15. **AR-044 / AR-046 moved to Epic 8** (Mary) — operational rehearsals and rotation dry-runs require real production data/environments; Epic 0 provides tooling only.
16. **Phase A telemetry instrumentation owned** (Mary, AR-047) — Epic 0 ships the primitive; each FR-delivering epic inherits AC to emit its events.
17. **Mutation testing on domain layer** (Murat, AR-048) — Stryker.NET weekly as solo-dev correctness substitute.
18. **FR13, FR49, FR2, FR7 annotated as spanning epics** (Mary) — explicit span markers in the coverage map prevent "whose story is this" drift during Step 3.
19. **Descope trigger moved to Day 30 with pre-committed cut PR** (Murat) — leading indicator with mechanically-executable fallback instead of Day 42 trailing indicator that fires too late.

**New AR items introduced in party-mode review:**

- **AR-047:** Phase-A telemetry emission primitive + per-epic event-emission ACs (Epic 0 owns primitive)
- **AR-048:** Mutation testing on Domain layer via Stryker.NET (Epic 0)
- **AR-049:** Compound-Journal-Entry interface seam (Epic 0 defines; Epic 5 consumes first; Epic 6 consumes unchanged)
- **AR-050:** Epic 4 cost circuit-breaker (per-token day-view cap, spend alert, hard kill-switch, AFD 24h TTL) — Epic 4 scope
- **AR-051:** NFR-R6 daily balance-integrity scheduled Wolverine job + 1-hour admin alert (Epic 8 scope; prior orphan)

**Explicit deferrals to Phase 2** (not in any MVP epic):
- Digital social proof PDF (rich format)
- Advanced link controls (custom expiration windows, per-recipient revocation, access logs)
- Push notifications beyond statement confirmation
- CSV import from spreadsheets (MVP exports only)
- Hijri calendar writes (Hijri read-only at MVP per NFR-A9f)
- PWA / offline capability
- Dark mode (per UX-DR1 Step 13 explicit deferral)

---

## Epic 0: Platform Foundation

Stand up the greenfield scaffold so that every subsequent feature slice lands into a pre-proven platform. No user-visible FRs; every story is AR-coverage. Hard gates only — deferred items layer in during Epic 2/3 when testable against real slices.

### Story 0.1: Backend solution scaffold

As a developer,
I want a greenfield .NET 10 solution rooted in Aspire 13 with the canonical project graph (Api + Application + Domain + Infrastructure + Contracts + Tests),
So that every feature slice has a stable home and the Domain layer starts with zero dependencies.

**Acceptance Criteria:**

**Given** an empty repository
**When** the solution is scaffolded
**Then** `Faktuboh.sln` exists with projects `Faktuboh.Api`, `Faktuboh.Application`, `Faktuboh.Domain`, `Faktuboh.Infrastructure`, `Faktuboh.Contracts`, plus test projects `Domain.Tests`, `Application.Tests`, `Api.Tests`, `Infrastructure.Tests`, `ArchitectureTests`
**And** Aspire AppHost + ServiceDefaults projects exist and orchestrate local dev
**And** `Faktuboh.Domain` has zero package references (verified by NetArchTest assertion)
**And** `dotnet build` passes with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + nullable enabled via `Directory.Build.props`
**And** Minimal APIs + FluentValidation + source-generated OpenAPI are wired in `Faktuboh.Api` per ADR-024

### Story 0.2: Angular two-target workspace with compile-boundary separation

As a developer,
I want an Angular 21 workspace with two build targets (`apps/owner-workspace` + `apps/public-statement`) sharing `libs/design-system` + `libs/core` + `libs/domain-ui`,
So that the public-statement bundle cannot import from the owner workspace as a property the compiler enforces (ADR-001).

**Acceptance Criteria:**

**Given** the Angular workspace is scaffolded per architecture §3.4
**When** `ng build public-statement` runs
**Then** no module from `apps/owner-workspace` or forbidden `libs/core` subtrees appears in the public-statement bundle graph
**And** attempting to import a forbidden module fails the build with a clear ESLint boundary-rule error
**And** both apps are zoneless (no `zone.js` in polyfills; verified by scanning `angular.json` + bundle output)
**And** PrimeNG v21 Aura theme + "Faktuboh Quiet" custom preset + Tailwind 4 layout-only + Transloco + Tajawal + Inter self-hosted woff2 are configured and loadable

### Story 0.3: Auth0 tenant provisioning and custom domain

As Wahid,
I want an Auth0 EU free-tier tenant provisioned with custom domain `auth.faktuboh.com`,
So that identity is a managed edge service per ADR-021 and NFR-P1 cold-start cost is zero.

**Acceptance Criteria:**

**Given** an Auth0 EU tenant
**When** DNS records (CNAME + TXT) are provisioned for `auth.faktuboh.com`
**Then** Auth0-managed TLS certificate provisions successfully
**And** hosted login renders at `https://auth.faktuboh.com/login`
**And** callback URLs, token audiences, and CORS origins for `app.faktuboh.com` + `statement.faktuboh.com` are registered against the custom domain
**And** Auth0 Actions populate custom claims (`tenant`, `ProcessingActivity`-hint placeholder)
**And** Auth0 Passwordless connection is enabled for future magic-link flows
**And** MFA is enabled at tenant level

### Story 0.4: RFC 9457 Problem Details middleware and ErrorCatalog

As a developer,
I want every API error response to follow RFC 9457 with domain error codes in the form `faktuboh.<slice>.<condition>`,
So that error semantics are a first-class wire contract that the Angular frontend can walk onto Signal Forms (D4.9).

**Acceptance Criteria:**

**Given** a slice throws `DomainException` with code `faktuboh.contacts.duplicate_email`
**When** the endpoint handles the exception via `IExceptionHandler`
**Then** the HTTP response has `Content-Type: application/problem+json`
**And** the body contains `type`, `title`, `status`, `detail`, `instance`, `traceId`, `code`, and `errors[]` with JSON Pointers for field-level violations
**And** the `type` URI is `https://faktuboh.com/errors/faktuboh.contacts.duplicate_email`
**And** `traceId` matches `Activity.Current?.TraceId` (W3C traceparent)
**And** NetArchTest asserts every `throw DomainException` references a code registered in `ErrorCatalog`
**And** attempting to throw a code not in `ErrorCatalog` fails the build

### Story 0.5: Idempotency-Key middleware

As a developer,
I want every mutation endpoint to respect the `Idempotency-Key` HTTP header with 24h TTL and row-level locking,
So that form-resubmission and double-tap scenarios return the cached response instead of creating duplicate records (D4.11).

**Acceptance Criteria:**

**Given** the middleware is wired for POST endpoints
**When** a client submits the same idempotency key twice concurrently
**Then** only one handler execution completes; the second request blocks on the row-level lock and returns the cached response
**And** a subsequent replay after success returns the same cached response with `returnedExisting: true` header
**And** a conflict mid-execution returns HTTP 409 with RFC 9457 code `faktuboh.common.duplicate_idempotency_key`
**And** the `idempotency_keys` Postgres table has 24h TTL sweep handled by Wolverine
**And** the TTL sweeper skips in-flight rows (`WHERE last_seen_at < now() - interval '5 min'`)
**And** a concurrent-double-submit integration test asserts single execution

### Story 0.6: IBitemporal interceptor and MigrationService

As a developer,
I want every entity implementing `IBitemporal` to automatically write history rows on UPDATE/DELETE via an EF Core `SaveChanges` interceptor, with the MigrationService ensuring triggers and extensions are idempotently installed on every cold start,
So that ADR-022's application-level bitemporal storage is enforced by infrastructure, not discipline.

**Acceptance Criteria:**

**Given** a stub entity `TestBitemporal` implements `IBitemporal { ValidFrom, ValidTo, RecordedAt }`
**When** an UPDATE or DELETE occurs on `test_bitemporals`
**Then** exactly one row is inserted into `test_bitemporals_history` with pre-mutation state + `RecordedAt` stamped
**And** an "audit round-trip" fitness test asserts one-history-row-per-mutation invariant in CI
**And** the MigrationService boots idempotently — running twice produces identical schema state
**And** the interceptor is registered on every `DbContext`; a unit test asserts this on boot
**And** repositories expose `AsOf(DateTimeOffset transactionTime, DateTimeOffset? validTime = null)` query extension

### Story 0.7: ProcessingActivity ambient context

As a developer,
I want `ProcessingActivity` (`OwnerLedger` / `DebtorConfirmation` / `SystemBackground`) propagated via `AsyncLocal<T>` from HTTP entry point through every layer,
So that every audit row, log record, OTel span, and Wolverine message carries unambiguous authorization + intent context (ADR-004).

**Acceptance Criteria:**

**Given** a slice sets `ProcessingActivity.OwnerLedger` at its `MapGroup` endpoint filter
**When** the handler calls into Application + Infrastructure + Wolverine
**Then** every Serilog log entry includes `ProcessingActivity` as a structured property
**And** every OTel span includes `ProcessingActivity` as a span attribute
**And** every audit row has a non-null `ProcessingActivity` column
**And** outgoing Wolverine messages include `ProcessingActivity` as a header, re-established on the receiving side
**And** NetArchTest asserts every endpoint and message handler sets or inherits a `ProcessingActivity`
**And** uninitialized access throws at runtime (no ambient default permitted)

### Story 0.8: NetArchTest fitness tests (hard gates)

As a developer,
I want NetArchTest fitness tests for Shared Kernel purity (#1) and cross-slice-communication-only-via-IntegrationEvents (#3) wired as CI required checks,
So that the E2 first-slice compound invariants are mechanically asserted before any feature slice can merge.

**Acceptance Criteria:**

**Given** `tests/Faktuboh.ArchitectureTests/` is scaffolded
**When** a slice's `Domain/` references another slice's `Domain/` directly (not via `IntegrationEvents`)
**Then** the NetArchTest for fitness test #3 fails with a clear message naming both slices
**And** `Faktuboh.Domain.Events.IntegrationEvents` namespace contains only record types with `public init` properties (Domain-internal types excluded)
**And** Shared Kernel (`Faktuboh.Domain`) has no dependency on `Faktuboh.Api` (fitness test #1)
**And** both tests run as required GitHub Actions checks on every PR
**And** CI-gate-first rule honored: each test's CI wiring lands before the test content is marked done

### Story 0.9: GitHub Actions CI/CD with OIDC

As a developer,
I want three GitHub Actions workflows (`ci.yml` on PRs, `deploy-staging.yml` on main merge, `deploy-prod.yml` on tag with required-reviewer + 10-min wait timer) using OIDC federated credentials with SHA-pinned actions,
So that no long-lived Azure credentials exist in GitHub and deploys have a pre-agreed safety gate (D4.17).

**Acceptance Criteria:**

**Given** GitHub repository + Entra ID workload identity
**When** a PR is opened
**Then** `ci.yml` runs lint + typecheck + Vitest + Playwright (affected projects only; path-filtered) + backend tests
**And** on merge to `main`, `deploy-staging.yml` deploys via `azd` to staging ACA with no approval gate
**And** on `v*.*.*` git tag, `deploy-prod.yml` requires reviewer approval + enforces 10-minute wait timer even for solo-dev
**And** all action references are SHA-pinned (not tag-pinned)
**And** no Azure secret exists in GitHub Secrets; authentication uses `azure/login` with OIDC federation
**And** same container image promotes staging → prod via ACA revision; never rebuilt

### Story 0.10: Azure infrastructure provisioned

As Wahid,
I want Azure Container Apps environment + Azure Static Web Apps for both FE targets + Postgres Flexible Server + Azure Key Vault Standard all provisioned in Germany West Central on the €0 free-tier stack (§7.4.1),
So that the platform is deployable from Day 1 with zero idle burn.

**Acceptance Criteria:**

**Given** Azure subscription with 12-month free-tier benefits intact
**When** `azd provision` runs against `infra/azure/*.bicep`
**Then** ACA environment is created in Germany West Central with `minReplicas=0` (staging/dev) and `minReplicas=1` (prod) configured per environment
**And** Azure Static Web Apps provision two sites: `app.faktuboh.com` + `statement.faktuboh.com` with free-tier SKU
**And** Postgres Flex Server Burstable B1ms provisions with zone-redundant HA at prod tier
**And** Key Vault Standard provisions per-environment (dev / staging / prod) with Managed Identity access
**And** App Insights pay-as-you-go provisions with 20% ingestion sampling + adaptive sampling enabled
**And** €50/mo budget alert fires to Wahid's email (50% of free-tier ceiling)

### Story 0.11: App Insights + OTel traceId correlation

As a developer,
I want Aspire 13 OTel auto-instrumentation wired to App Insights with W3C traceparent correlation between Angular frontend and backend (D4.20),
So that one Log Analytics KQL query joins FE + BE events by `traceId` — the operational reason the D4.9 domain code taxonomy pays off.

**Acceptance Criteria:**

**Given** the four dashboards (Golden signals / Outbox health / AFD+WAF / Business liveness) are provisioned via ARM/Bicep
**When** a 4xx or 5xx response returns from backend
**Then** the Problem Details `traceId` matches `Activity.Current?.TraceId`
**And** the Angular HTTP interceptor logs the response `traceId` to FE App Insights
**And** a KQL query joins FE and BE events by `traceId` and returns matched pairs within 5 seconds
**And** force-keep rules never sample: all exceptions, all 5xx, all upstream `Sampled=1` traces, all outbox failures, all auth failures
**And** the four alerts fire (backend p95 >1s / error rate >2% / DLQ >50 rows / AFD cache-hit <90%)

### Story 0.12: NFR-A9 RTL acceptance gate harness

As Wahid,
I want the six-scenario RTL acceptance gate defined as a CI fitness-test harness that surface-owning epics must satisfy,
So that the ship-blocker bilingual commitment has a single authoritative specification (NFR-A9).

**Acceptance Criteria:**

**Given** the gate harness exists in `tests/Rtl.Tests/`
**When** the harness runs in CI
**Then** it enumerates six named scenarios: (a) mixed AR + Latin-numeral bidi isolation, (b) Latin/Eastern-Arabic numeral toggle, (c) transaction-note bidi isolation, (d) RTL dashboard charts, (e) RTL CSV export, (f) right-aligned date-range picker with Hijri toggle
**And** each scenario maps to a Playwright + axe-core + screenshot-diff test that surface-owning epics (1, 3, 4) hook into
**And** a reviewer-device matrix lists the two RTL-native reviewer devices (one mobile / one desktop) each release candidate must pass
**And** violation of any scenario fails the release build

### Story 0.13: NFR-SC4 load test baseline harness

As a developer,
I want a k6 load-test harness committed with an initial no-op script that feature epics hook endpoints into,
So that NFR-SC4 (83 rps sustained / 500 rps cache-sizing posture) has a home that's impossible to orphan.

**Acceptance Criteria:**

**Given** `tests/Load/` is scaffolded
**When** `k6 run tests/Load/baseline.js` executes
**Then** the script runs a placeholder 1-rps curl against `/health` and exits with 0
**And** a README documents how epic owners add endpoints (target rps, pass/fail thresholds: p95 <300ms, p99 <1.5s, zero 5xx, error <0.1%)
**And** the harness runs nightly against staging (not PR-blocking; trend-tracked)
**And** Epic 4 adds the public-statement throughput scenario; Epic 8 adds the full production-representative execution

### Story 0.14: Phase-A telemetry primitive (AR-047)

As Wahid,
I want an App Insights event-emission SDK + PII-safe event schema + integration-test helper asserting events fire,
So that Phase-A success metrics (40% activation, viral coefficient ≥0.3, 5 signups/week) are instrumentable from Day 1 rather than discovered missing two weeks post-launch.

**Acceptance Criteria:**

**Given** the telemetry primitive is wired in `Faktuboh.Infrastructure/Telemetry/`
**When** a slice emits `TelemetryEmitter.Emit("user_signup", new { user_id = "..." })`
**Then** the event reaches App Insights with PII-safe schema (no email, no names — only opaque user IDs)
**And** an integration-test helper `AssertEventEmitted("user_signup")` asserts the event fired during a test run
**And** every FR-delivering epic (E1: `user_signup`; E3: `first_debt_recorded`, `debt_recorded`; E4: full viral funnel) inherits the AC "emit X telemetry events, verified by integration test"
**And** NetArchTest asserts no PII fields appear in any telemetry event type (compile-time checked via attribute scan)

### Story 0.15: Compound-Journal-Entry interface seam (AR-049)

As a developer,
I want a `ICompoundJournalEntry` Domain-layer abstraction representing multi-leg balanced atomic journal writes,
So that Epic 5 (Transfer) and Epic 6 (Exchange) consume a stable interface that decouples their ordering.

**Acceptance Criteria:**

**Given** `Faktuboh.Domain.JournalEntries.ICompoundJournalEntry` is defined
**When** an implementation writes N legs
**Then** the interface guarantees atomicity (all-or-none transaction wrapper)
**And** balance invariant is asserted at commit time (sum of debits == sum of credits per denomination)
**And** each leg carries a `CorrelationId` linking it to the compound operation
**And** a stub implementation lands in Epic 0 with a fitness test validating the balance invariant
**And** Epic 5's Transfer feature consumes it unchanged; Epic 6's Exchange consumes it unchanged

### Story 0.16: Mutation testing on Domain layer (AR-048)

As Wahid,
I want Stryker.NET configured against `Faktuboh.Domain` running weekly with a triage workflow,
So that solo-dev no-pair-review doesn't leak correctness bugs on Money VO, bitemporal interceptor, capability-token validator, or rate-feed reconciler.

**Acceptance Criteria:**

**Given** Stryker.NET is configured in `stryker-config.json`
**When** `dotnet stryker` runs against `Faktuboh.Domain`
**Then** mutation score ≥ 90% on the canonical `DomainMath` module (post-Epic-6)
**And** a weekly scheduled GitHub Action runs Stryker and posts the score to a status badge
**And** the report surfaces surviving mutants with file/line references
**And** a triage workflow (2h/week budget) documents how to kill mutants by adding tests
**And** initial setup (1 day) is complete and the first weekly run has passed

### Story 0.17: Cost circuit-breaker baseline

As Wahid,
I want a daily spend alert at €50/month trigger (50% of free-tier ceiling) + a hard kill-switch env flag (`FAKTUBOH_EMERGENCY_SHUTDOWN`) that disables public-facing routes in 60 seconds,
So that a misconfiguration or viral overshoot can't silently burn the free-tier runway (AR-030).

**Acceptance Criteria:**

**Given** App Insights daily-spend metric is wired with budget alert at €50
**When** the daily projection exceeds €50
**Then** Wahid receives an email alert within 10 minutes
**And** setting `FAKTUBOH_EMERGENCY_SHUTDOWN=true` on the ACA env var causes public routes (`/statement.*/*`) to return a static "temporarily unavailable" HTML page within 60 seconds of restart
**And** authenticated owner routes continue to serve normally during emergency shutdown (kill-switch is scoped to public surface)
**And** a runbook documents the emergency-shutdown procedure in `docs/runbooks/emergency-shutdown.md`

---

## Epic 1: First-Run Shell & Identity

A new visitor can land on Faktuboh, register, authenticate, choose Arabic or English, read the legal disclaimer, view/edit their profile, delete their account, and land on an empty Glance skeleton ready for their first debt.

### Story 1.1: Bilingual shell with design tokens and RTL pipeline

As a new visitor,
I want Faktuboh to render in Arabic (RTL) or English (LTR) with a visual language consistent with the Faktuboh Quiet design system,
So that the product feels native to my reading direction from the first moment I load it.

**Acceptance Criteria:**

**Given** a fresh browser session on `https://app.faktuboh.com`
**When** the user selects Arabic as the language
**Then** `<html lang="ar" dir="rtl">` is set before first paint (no FOUC)
**And** all spacing uses logical properties (`margin-inline-*`, `padding-block-*`) — stylelint blocks physical properties in feature code
**And** Tajawal + Inter woff2 fonts are preloaded in `<head>` with `font-display: swap`
**And** design tokens (`--primary`, `--accent-gold`, surface/text/success/warning/danger tokens) are applied via CSS custom properties scoped to `[data-theme="light"]`
**And** toggling to English flips direction within 200ms without losing state (no full page reload)
**And** brand orange `#ff8100` is never used in any UI token (enforced by ESLint rule `no-brand-orange-in-ui`)

### Story 1.2: User registration via Auth0 hosted flow

As a new visitor,
I want to register an account with email + password via Auth0's hosted flow,
So that I can access Faktuboh's authenticated experience (FR1).

**Acceptance Criteria:**

**Given** the unauthenticated user clicks "Sign up" from the landing page
**When** the user is redirected to `https://auth.faktuboh.com/u/signup`
**Then** Auth0 hosted sign-up UI renders in the user's active locale (AR or EN)
**And** password policy enforces bcrypt cost ≥ 12 per NFR-S2 (configured in Auth0)
**And** on successful registration, the user is redirected back to `https://app.faktuboh.com/onboarding` with valid session tokens
**And** telemetry event `user_signup` is emitted per AR-047
**And** the Auth0 Action populates custom claims (`tenant_id`, `processing_activity_hint=OwnerLedger`)
**And** duplicate-email registration returns a friendly RFC 9457 error rendered inline on the form

### Story 1.3: User login with language selection

As a registered user,
I want to log in with my email + password and choose Arabic or English,
So that I can access my Glance in my preferred language (FR2, FR4).

**Acceptance Criteria:**

**Given** a registered user with stored language preference
**When** the user logs in via Auth0 hosted flow
**Then** session tokens are issued and stored per NFR-S3 (XSS/CSRF-safe browser security attributes; 30-min inactivity timeout with auto-extension on activity)
**And** the Glance renders in the user's stored language preference
**And** a language toggle in the top app bar lets the user switch AR↔EN; the choice persists to the user's profile
**And** 10 failed authentication attempts per 15 minutes per IP trigger rate-limit response per NFR-S5

### Story 1.4: Password reset via Auth0

As a user who forgot their password,
I want to request a password reset via email and set a new password,
So that I can regain access to my account (FR3).

**Acceptance Criteria:**

**Given** a registered user with a valid email on file
**When** the user clicks "Forgot password?" on the login page
**Then** Auth0 sends a reset-link email composed + signed + delivered by Auth0 (no in-app code, no Resend involvement)
**And** clicking the link in the email opens an Auth0 hosted form in the user's active locale
**And** setting a new password succeeds and redirects to login
**And** the new password satisfies the NFR-S2 hash parameters

### Story 1.5: Profile view and edit

As an authenticated user,
I want to view and edit my profile information (name, language preference, timezone),
So that Faktuboh displays dates and content correctly for me (FR5).

**Acceptance Criteria:**

**Given** an authenticated user navigates to `/profile`
**When** the profile page loads
**Then** current name, email (read-only — changes happen via Auth0), language, and timezone are displayed
**And** editing name + language + timezone persists via `PATCH /v1/users/me` with optimistic UI + server-authoritative confirmation
**And** the form uses Signal Forms with `FormMutationConfig` (IftaLabel desktop / floating mobile per UX-DR28)
**And** the per-endpoint idempotency-key middleware accepts the form's UUID v4
**And** validation errors surface inline under fields via RFC 9457 `errors[]` walked onto Signal Forms nodes

### Story 1.6: Legal disclaimer display

As a new or existing user,
I want to see a clear legal disclaimer explaining that tracked debts are personal records (not legally binding contracts),
So that I understand the product's positioning and regulatory scope (FR7).

**Acceptance Criteria:**

**Given** a user on the registration page OR on their account settings
**When** the page renders
**Then** the disclaimer text is present and readable in the active locale (AR or EN)
**And** the disclaimer explicitly states "User record — not a legally binding document"
**And** the disclaimer appears in the footer of the authenticated owner shell
**And** the disclaimer passes WCAG 2.1 AA contrast checks

### Story 1.7: Account deletion with GDPR erasure

As a user who wants to leave the service,
I want to delete my account and have my personal data cryptographically erased while transaction-audit structure is preserved,
So that I exercise my GDPR right to erasure without compromising the ledger's audit integrity (FR6, NFR-S7, AR-042).

**Acceptance Criteria:**

**Given** an authenticated user navigates to `/profile/delete`
**When** the user confirms deletion via a destructive-action confirmation dialog (UX-DR29)
**Then** the user's DEK row in `subject_keys` is destroyed; ciphertext in transactional rows becomes irrecoverable
**And** contact PII is tombstoned (hard-deleted + FK replaced with permanent tombstone token)
**And** transaction amounts + dates + denominations + mathematical relationships remain so balances still calculate
**And** audit log entries are anonymized (`User-7a3f`, `Contact-2b1e` opaque identifiers) per domain requirements
**And** the Auth0 Management API user delete is invoked and `SubjectKeyDestroyed` domain event fires
**And** no lookup table maps anonymized identifiers back to names (irreversibility assertion in integration test)
**And** the user is logged out and redirected to a confirmation page stating erasure is complete

### Story 1.8: Empty Glance skeleton with activation nudge

As a newly-registered user on my first session,
I want to land on an empty Glance showing "لي 0 / عليّ 0" hero tiles with a single contextual nudge saying "Lent or borrowed recently? Add it now — takes 10 seconds,"
So that my first impression of Faktuboh is calm, clear, and immediately actionable.

**Acceptance Criteria:**

**Given** a user has just completed registration (Stage 0 of Glance Maturity Model)
**When** they land on `/dashboard`
**Then** the two hero tiles render showing zero with role labels "لي" and "عليّ" per Direction 6 Segmented Summary
**And** two empty column placeholders render with clear visual affordances (not blank space)
**And** a `ContextualNudge` component with `kind="add-first-debt"` renders above the columns
**And** the nudge has fixed 56px minimum height (prevents CLS)
**And** `role="status"` + `aria-live="polite"` ensure screen readers announce it
**And** no wizard, no tutorial overlay, no welcome modal appears
**And** tapping the nudge opens the debt-entry form (form is mocked/stubbed in Epic 1; live in Epic 3)
**And** the layout is identical to the populated-Glance layout at Stage 1/2/3 (Glance Maturity Model — only content density differs)

### Story 1.9: Phase-1 component API defined in design-system library

As a developer,
I want the Phase-1 components (AmountDisplay, ContactAvatar, DebtRow, HeroTile, GlanceShell, DenominationPicker) defined as standalone signal-based components in `libs/design-system` + `libs/domain-ui` with their public API frozen,
So that Epic 3 (live data) and Epic 4 (StatementView) consume them without modifying the API (Mary's single-owner rule).

**Acceptance Criteria:**

**Given** the components are implemented per UX-DR5–UX-DR10
**When** a component's public signal inputs are inspected
**Then** AmountDisplay has `amount: Signal<Money>`, `role: Signal<'receivable'|'payable'|'neutral'>`, `size: Signal<'sm'|'md'|'lg'|'xl'>`, `originalDenomination: Signal<Denomination|null>` and nothing else
**And** ContactAvatar has `contactId: Signal<string>`, `displayName: Signal<string>`, `size: Signal<'xs'|'sm'|'md'|'lg'>` and nothing else
**And** DebtRow, HeroTile, GlanceShell, DenominationPicker public APIs match UX-DR5–UX-DR10 signatures exactly
**And** all components are `standalone: true` with `OnPush` change detection
**And** signal-based APIs (`input()`, `output()`, `model()`, `computed()`) replace legacy `@Input()`/`@Output()` entirely
**And** each component has colocated Vitest unit tests + Angular Testing Library component tests at Glance Stage 1/2/3 densities
**And** a Storybook (or equivalent) showcase renders every component at every state in both LTR and RTL
**And** an ESLint rule enforces the single-owner convention: cross-epic PRs adding new `kind`/variant props must modify the component's home, not fork

### Story 1.10: Accessibility CI gates

As Wahid,
I want axe-core + Lighthouse CI + ESLint template-a11y + stylelint logical-property rule running on every PR with zero-violation thresholds,
So that accessibility is mechanically enforced before any merge (NFR-A1, NFR-A3, UX-DR20).

**Acceptance Criteria:**

**Given** the PR CI pipeline runs
**When** axe-core checks execute against every route-level E2E test
**Then** zero violations report at `wcag21aa` level; zero critical at `wcag21aaa`
**And** Lighthouse CI thresholds hold (accessibility ≥ 95, performance ≥ 85 desktop / ≥ 75 mobile throttled)
**And** `@angular-eslint/template-a11y` rules have zero errors
**And** stylelint blocks physical properties (`margin-left`, `right`, etc.) in feature code with a clear error
**And** the manual screen-reader test matrix (VoiceOver AR on iOS, TalkBack AR on Android, NVDA AR on Windows) is documented in `docs/accessibility/manual-qa.md` with release-candidate checklist

### Story 1.11: Performance budget + visual regression CI gates

As Wahid,
I want perf budgets (LCP, INP, CLS, initial JS, route-level JS, font total) + visual regression screenshots (LTR + RTL) running on every PR,
So that performance + RTL parity are enforced before merge (UX-DR37, UX-DR39).

**Acceptance Criteria:**

**Given** the PR CI pipeline runs
**When** perf-budget checks execute
**Then** initial JS bundle for `apps/owner-workspace` is ≤ 250 KB gzipped; for `apps/public-statement` is ≤ 180 KB gzipped (per-target budget.json enforced)
**And** route-level lazy bundles are ≤ 60 KB gz each
**And** Tajawal + Inter total woff2 ≤ 120 KB
**And** Lighthouse LCP < 2.5s Slow 4G, INP < 200ms, CLS < 0.1, FCP < 1.8s Slow 4G, TTFB < 800ms on key routes
**And** visual regression screenshots capture every shell screen in LTR + RTL; diffs above threshold fail the PR
**And** Zoneless invariant verified by scanning bundle output (no `zone.js`)

---

## Epic 2: Contacts — Know Your Counterparties (First Slice)

The user can add, edit, delete (guarded), list, and search contacts. The first story IS the §7.5.2 Contacts/Create-with-opening-balance slice exercising Money VO + idempotency + bitemporal + RFC 9457 + cross-slice integration event end-to-end.

### Story 2.1: Contacts/Create with opening-balance (§7.5.2 first slice)

As a new user recording my first relationship in Faktuboh,
I want to create a contact with an optional opening balance captured atomically in the same operation,
So that I can express "Ahmed owes me $300 as of today" in a single form, and the architecture's first slice exercises Money VO + idempotency + bitemporal + RFC 9457 + cross-slice integration event end-to-end (FR8, FR13 opening-balance).

**Acceptance Criteria:**

**Given** an authenticated user on `/contacts/new`
**When** the user enters name + optional phone/email/notes + optional opening balance `{ amount: "300.00", currency: "USD", direction: "receivable" }` + submits with an `Idempotency-Key` header
**Then** `POST /v1/contacts` creates the contact row and atomically writes the opening-balance debt entry as a bitemporal history-tracked record
**And** the `Idempotency-Key` middleware ensures double-submit returns the same response (verified by concurrent-double-submit integration test)
**And** RFC 9457 error response is returned for validation failures (e.g., duplicate contact name) with `errors[]` walked onto Signal Forms
**And** `ContactCreated` integration event emits via Wolverine and a no-op Debts projection handler consumes it (exercises NetArchTest fitness test #3)
**And** the `JournalEntry` row uses `numeric(19,4)` for user-facing amount; `Money` VO validates currency against `CurrencyRegistry`
**And** bitemporal interceptor writes `contacts_history` and `journal_entries_history` rows on UPDATE/DELETE
**And** telemetry event `contact_created` emits per AR-047
**And** the ContactAvatar renders with deterministic tint from hash of contact ID per UX-DR6
**And** response returns `201 Created` with `CreateContactResponse` including contact ID + opening-balance entry ID

### Story 2.2: Edit existing contact

As a user,
I want to edit a contact's name, phone, email, and notes,
So that I can keep the counterparty's information accurate (FR9).

**Acceptance Criteria:**

**Given** an existing contact
**When** the user submits `PATCH /v1/contacts/{id}` with updated fields + Idempotency-Key
**Then** changes are persisted + `ContactUpdated` integration event emits
**And** the bitemporal interceptor writes a `contacts_history` row with pre-update state
**And** concurrent-edit conflict (409) returns RFC 9457 with `Reload` action per UX-DR32
**And** the `ContactAvatar` deterministic tint remains stable (tied to contact ID, not name) per UX-DR6

### Story 2.3: Delete contact with outstanding-balance guard

As a user,
I want to delete a contact I no longer need, with a warning if outstanding balances exist,
So that I don't accidentally lose a record of an unsettled debt (FR10).

**Acceptance Criteria:**

**Given** a contact with zero outstanding balances across all denominations
**When** the user confirms deletion via `ConfirmDialog` per UX-DR29
**Then** the contact is soft-deleted (PII tombstoned per AR-042); `ContactDeleted` integration event emits
**And** when the contact has any non-zero balance, the confirmation dialog displays the outstanding balances per-denomination and requires an explicit "I understand" confirmation
**And** tapping "Cancel" in the warning state dismisses without deletion
**And** deleted contacts are excluded from the contacts list but their historical transactions remain in audit

### Story 2.4: List contacts with per-denomination balance summary

As a user,
I want to see all my contacts with each one's balance broken down per denomination,
So that I understand my full position per counterparty at a glance (FR11).

**Acceptance Criteria:**

**Given** the user has N contacts with varying balances
**When** the user navigates to `/contacts`
**Then** `GET /v1/contacts?include=balances` returns each contact with per-denomination balance aggregations computed server-side
**And** the list renders using `ContactAvatar` + contact name + per-denomination balance pills
**And** balances are never aggregated across denominations (NFR core)
**And** the list supports Glance Maturity Stages 1/2/3 (no sort/filter <11 contacts; sort Popover 11-50; sort + filter + search 51+)
**And** pagination uses `p-paginator` only above 50 rows; otherwise simple `@for`
**And** response time < 200ms per NFR-P7

### Story 2.5: Search and filter contacts by name

As a power user with 50+ contacts,
I want a search box that filters contacts by name in real time,
So that I can jump directly to a specific counterparty without scrolling (FR12).

**Acceptance Criteria:**

**Given** the user has > 10 contacts (Stage 2 Glance Maturity)
**When** a persistent search bar appears above the list (per UX contextual rule)
**Then** typing filters results with `scroll-snap-type: x mandatory` on chip boundaries for any active filter chips
**And** search debounces at 150ms
**And** the AutoComplete in debt-entry forms also uses this backend (endpoint `GET /v1/contacts?q=...&limit=10`)
**And** grapheme-aware matching handles Arabic combining marks correctly
**And** empty-result state shows "No matches — Clear filter" ghost button per UX-DR30

---

## Epic 3: Record Debts, See the Glance & Export

The user can record debts, see the full Glance populated at every Stage, view per-contact history with filter + rollup, export CSV, and access the user-visible audit trail.

### Story 3.1: Record new debt (lent or borrowed)

As a user recording a debt with an existing contact,
I want to record the amount + denomination + date + optional story, and have it appear in my Glance immediately,
So that "in the moment" capture completes in under 30 seconds (FR13, FR17).

**Acceptance Criteria:**

**Given** an authenticated user with at least one contact
**When** the user taps the mobile SpeedDial FAB (GlanceFab per UX-DR14) or the column-level "+" button on desktop, selects contact + amount + denomination + direction (lent/borrowed) + optional note, and submits
**Then** `POST /v1/journal-entries` with Idempotency-Key creates the debt with bitemporal history tracking
**And** the new row appears in the correct Direction 6 column (لي or عليّ) with subtle 120ms fade-in
**And** **no toast, no sound, no animation flourish** on success (UX-DR27 silent success)
**And** operation completes in < 500ms end-to-end per NFR-P4
**And** optimistic UI shows the row immediately; server rejection triggers rollback with Toast retry per UX-DR31
**And** the `DenominationPicker` shows Recent section with user's 3 most-used denominations (cached from history) + All section sorted alphabetically
**And** date defaults to today; can be edited
**And** amount uses `InputNumber mode="decimal"` with locale-appropriate grouping separators
**And** telemetry event `first_debt_recorded` or `debt_recorded` emits per AR-047

### Story 3.2: Edit debt with audit preservation

As a user,
I want to edit a debt's amount, date, denomination, or story, with every change tracked in the audit trail,
So that my records stay accurate and disputes can be resolved by history (FR14).

**Acceptance Criteria:**

**Given** an existing debt
**When** the user submits `PATCH /v1/journal-entries/{id}` with Idempotency-Key
**Then** the debt is updated and the bitemporal interceptor writes pre-mutation state to `journal_entries_history`
**And** the audit log records the actor (user ID), action type (`debt.updated`), affected entity, before/after values, source IP (per domain requirement), and `ProcessingActivity`
**And** the updated balance surfaces in the Glance in < 500ms
**And** concurrent-edit conflict (409) returns RFC 9457 with `Reload` action preserving user's draft

### Story 3.3: Delete debt with audit preservation

As a user,
I want to delete a debt entry that was recorded in error, with the deletion tracked in audit,
So that mistakes can be corrected without compromising history (FR15).

**Acceptance Criteria:**

**Given** an existing debt
**When** the user confirms deletion via `ConfirmDialog`
**Then** the debt row is soft-deleted (ValidTo stamped; history row written)
**And** the audit log records action `debt.deleted`
**And** the row disappears from the Glance; dashboard sums recalculate in < 200ms per NFR-P7
**And** deleted debts remain visible in the audit surface (Story 3.8) with strikethrough

### Story 3.4: Per-contact history with date-range filter and previous-balance rollup

As a user with months of transactions with one contact,
I want to view the full transaction history and filter by date range — with transactions before the filter rolling up into a "previous balance" line per denomination,
So that I can focus on recent activity without scrolling through months of history (FR16, FR28).

**Acceptance Criteria:**

**Given** a contact with ≥ 30 transactions across multiple denominations and dates
**When** the user navigates to `/contacts/{id}`
**Then** the full transaction history renders in chronological order with per-denomination subtotals
**And** applying a date-range filter collapses all transactions before the filter-start-date into a single "Previous balance: X USD, Y grams gold" line per denomination
**And** balance calculations complete in < 200ms per NFR-P7 even with 1000+ history rows
**And** the date-range picker is right-aligned with Hijri-calendar toggle per NFR-A9(f) (Hijri read-only at MVP)
**And** `ReconciliationMeta` surfaces "Last reconciled: [date]" per UX-DR12 when the contact has a confirmed statement (from Epic 4 onward; gracefully renders `never` state here)

### Story 3.5: Full Glance populated with Direction 6 Segmented Summary

As a user with multiple contacts and denominations,
I want the Glance to show both HeroTiles populated + both DebtRow columns + the settled-debts accordion, at the correct Glance Maturity Stage for my data volume,
So that I see my complete financial position in under 2 seconds (FR27, FR31, NFR-P6).

**Acceptance Criteria:**

**Given** a user at Stage 1 (1-10 debts), Stage 2 (11-50), or Stage 3 (51+)
**When** the `/dashboard` route loads
**Then** two `HeroTile` components render with role labels "لي" / "عليّ" + primary-denomination xl `AmountDisplay` + debt count (Stage 2+) + per-denomination secondary sums (max 3 before "+N more")
**And** two parallel `DataView` column lists render `DebtRow` components sorted by default rule (date desc)
**And** the `Accordion` "المُسدَّدة (N)" renders collapsed at the bottom
**And** at Stage 1, sort/filter controls are hidden; at Stage 2, sort Popover surfaces; at Stage 3, sort + filter chips + `<p-virtualScroller [itemSize]="64">` (desktop) / `72` (mobile) activate
**And** page TTR < 2s perceived; TTI < 500ms broadband / 1.5s 3G; CLS < 0.05 per "The Glance" engineering contract
**And** zero loading spinners; skeletons only
**And** balance correctness is 100% (verified by property-based tests asserting `sum(transactions) == stored_balance` for every contact-denomination pair)

### Story 3.6: Dashboard bar chart — distribution across contacts

As a user,
I want a bar chart showing my debt distribution across contacts per denomination,
So that I can spot which counterparties concentrate my exposure (FR32).

**Acceptance Criteria:**

**Given** the user is on `/dashboard`
**When** the bar chart renders
**Then** one bar per contact shows their net balance (no cross-currency aggregation — chart is rendered per-denomination with selector)
**And** chart is RTL-correct per NFR-A9(d): legends, axes, tooltips, labels flow right-to-left when `dir="rtl"`
**And** tabular numerals + no animated counters (Stability Signals Trust principle)
**And** bars use `--primary` for receivable / `--danger` for payable per role
**And** screen readers announce "N bars, contact X has balance Y" naturally

### Story 3.7: Dashboard pie chart — distribution across denominations

As a user who holds debts in multiple currencies and metals,
I want a pie chart showing the relative size of each denomination across my total book,
So that I can understand my currency/metal exposure at a glance (FR33).

**Acceptance Criteria:**

**Given** the user has debts in ≥ 2 denominations
**When** the pie chart renders
**Then** one slice per denomination; slice size = absolute-value sum in that denomination (no cross-currency conversion — slice sizes reflect unit counts, not value equivalence)
**And** legend labels include denomination + unit symbol + absolute total
**And** chart passes WCAG 2.1 AA color-differentiation (distinct slices by hue + pattern as backup per UX-DR color-blindness rule)
**And** single-denomination users see a clean single-slice chart with explanatory subtitle

### Story 3.8: User-visible audit trail surface

As a user,
I want to see the audit trail of any debt I edited or deleted,
So that I have transparent access to the history of my own records (FR49 user-visible portion).

**Acceptance Criteria:**

**Given** a debt with ≥ 1 edit or delete action
**When** the user taps "View history" on the debt detail page
**Then** a chronological list renders showing timestamp + action (created/edited/deleted) + before/after values for edits
**And** the list is read-only (FR49 immutability — no "undo" or "restore" at MVP)
**And** audit entries that reference `ProcessingActivity.DebtorConfirmation` are labeled "Recipient confirmed"
**And** entries older than 7 years or from anonymized accounts show opaque identifiers (`User-7a3f`)

### Story 3.9: CSV export with RTL column order and UTF-8 BOM

As a user,
I want to export all my contacts and transactions as a CSV I can open in Excel or Google Sheets,
So that I can keep external backups or do custom analysis and exercise my GDPR right to data export (FR30, NFR-A9e).

**Acceptance Criteria:**

**Given** an authenticated user taps "Export all data"
**When** `GET /v1/export/csv` streams the response
**Then** the CSV contains all contacts + all transactions + all audit entries with proper UTF-8 encoding and BOM (for Excel-Arabic compatibility)
**And** when the user's locale is Arabic, column order is reversed to match RTL reading convention (rightmost column = first data column)
**And** currency symbols are positioned per-currency convention in a dedicated column
**And** export completes in < 10s for accounts with up to 10,000 transactions per NFR-P10
**And** the response Content-Disposition names the file `faktuboh-export-YYYY-MM-DD.csv`
**And** opening the file in Excel + Google Sheets on both macOS and Windows renders Arabic content correctly (manual QA checklist)

### Story 3.10: Layer in NetArchTest #2 slice isolation and #4 no-IAggregateRoot

As a developer,
I want NetArchTest fitness tests #2 (slice isolation) and #4 (no `IAggregateRoot` in Shared Kernel) running as CI required checks once Epic 3's second and third real slices exist,
So that the full four-test fitness gate is active before complex epics (4, 5, 6) begin.

**Acceptance Criteria:**

**Given** Epic 3 ships 8+ feature slices
**When** fitness tests #2 and #4 are added to `ArchitectureTests` project
**Then** a slice's `Domain/` referencing another slice's `Domain/` directly fails with clear message
**And** any `IAggregateRoot`-implementing type in `Faktuboh.Domain` (Shared Kernel) fails the build
**And** both tests run as required GitHub Actions checks
**And** the Epic 0 "deferred" inventory is updated to reflect these as shipped

### Story 3.11: OpenAPI-drift gate baseline

As a developer,
I want the OpenAPI-drift gate baselined against Epic 2 + Epic 3 real endpoints,
So that future PRs changing the API surface fail CI until the `openapi.json` snapshot is regenerated and reviewed.

**Acceptance Criteria:**

**Given** Epic 2 + Epic 3 have shipped real endpoints
**When** `tools/validate-openapi.ps1` runs in CI
**Then** the generated spec from the running API is diffed against the committed `openapi.json` snapshot via Schemathesis (or openapi-diff)
**And** any drift fails the build with a message pointing to the drift location
**And** regenerating + committing the snapshot resolves the failure
**And** an ESLint rule `no-handwritten-shared-contracts` forbids any TypeScript file under `contracts/` not marked as generated

---

## Epic 4: Viral Loop MVP — Statement, Share & Confirm

Close the viral loop end-to-end at its thinnest viable shape that validates the Phase-A viral-coefficient hypothesis (≥0.3). Scope deliberately cut to 9 FRs — SignalR and owner-side link management deferred to Epic 7.

### Story 4.1: Capability-token scheme with constant-time response

As Wahid,
I want 128-bit opaque capability tokens stored server-side with constant-time response on unknown/expired/revoked tokens,
So that the public statement surface is defensible against token-existence-oracle timing attacks (AR-016, Step 2.9 adversarial baseline).

**Acceptance Criteria:**

**Given** `capability_tokens` Postgres table with columns `{token_hash, owner_subject_id, scope, exp, revoked_at, issued_by, version_byte}`
**When** the public-statement endpoint `/v1/statements/t/{token}` receives any token
**Then** the endpoint always performs full validation work (hash, DB lookup, decrypt) before returning — no short-circuit on early failure
**And** a statistical timing test (10k valid + 10k invalid tokens) shows p99 timing difference < 5ms (no timing oracle)
**And** token base64url encoding yields ≈ 22-char URL segment
**And** the `version_byte` enables in-band scheme migration without URL rewrite
**And** tokens never appear in logs, telemetry, or error messages (NetArchTest + Serilog/OTel redaction)
**And** `history.replaceState` strips the token from browser history after first navigation

### Story 4.2: apps/public-statement as distinct Angular build target

As a developer,
I want `apps/public-statement` building as a separate Angular bundle with compile-enforced separation from owner-workspace,
So that the hostile public surface cannot accidentally import owner-workspace code (ADR-001).

**Acceptance Criteria:**

**Given** the workspace has two targets
**When** `ng build public-statement` runs
**Then** the `stats.json` shows zero modules from `apps/owner-workspace` or forbidden `libs/core` subtrees
**And** attempting to `import` a forbidden module causes build failure with ESLint boundary-rule error message naming both modules
**And** the bundle-graph CI gate runs on every PR touching `apps/public-statement/*` or `libs/*`
**And** `libs/design-system`, `libs/domain-ui`, and the allowed `libs/core` subset (HTTP client, i18n, cache registry) are importable

### Story 4.3: 180KB gz bundle budget with SSR fallback documented

As Wahid,
I want the `apps/public-statement` initial JS bundle enforced at ≤ 180 KB gzipped with LCP < 2.5s on Slow 4G, with a pre-committed SSR fallback documented if the budget cannot be hit,
So that the hostile first-contact surface renders fast OR falls back gracefully (NFR-P1 public, Winston's entry criterion).

**Acceptance Criteria:**

**Given** `budget.json` for `apps/public-statement` sets `initial JS ≤ 180 KB gzipped`
**When** a PR increases the bundle beyond budget
**Then** CI fails with a clear message showing the current size + threshold
**And** Lighthouse CI runs against a staging `statement.faktuboh.com/t/{fixture-token}` and asserts LCP < 2.5s on emulated Slow 4G (nightly, not per-PR)
**And** `docs/architecture/ssr-fallback.md` documents the pre-committed fallback: server-rendered HTML for the public route, estimated at 5 eng-days, triggered if the budget cannot be hit on the Angular zoneless two-target approach
**And** the fallback trigger condition is documented (2 consecutive PRs cannot hit budget OR LCP regression sustained > 14 days)

### Story 4.4: Generate statement HTML + shareable link

As a user,
I want to generate a shareable link for any contact's statement with one tap,
So that I can send the link via WhatsApp/email/SMS for reconciliation (FR29, FR34).

**Acceptance Criteria:**

**Given** an authenticated user on a contact's detail page with ≥ 1 debt
**When** the user taps "Share statement"
**Then** `POST /v1/contacts/{id}/statements` generates a 128-bit capability token + stores in `capability_tokens` with 30-day default expiration per NFR-S11
**And** the response returns a URL `https://statement.faktuboh.com/t/{token}`
**And** the native share sheet opens (mobile) or a copy-to-clipboard action triggers (desktop)
**And** the share-message template uses culturally-appropriate Arabic default ("شوف كشف حسابنا مع بعض، تأكد معي من الأرقام") per UX-DR33
**And** the statement is HTML-rendered (rich PDF deferred to Phase 2)
**And** telemetry event `statement_shared` emits per AR-047 with opaque `owner_subject_id` + `contact_id_hash`

### Story 4.5: Public StatementView renders without account

As a recipient of a shared statement link,
I want to open the link on my phone without signing up and see a clean professional bilingual statement,
So that I can review the record quickly in any messaging-app context (FR35, FR39).

**Acceptance Criteria:**

**Given** a recipient taps a valid `statement.faktuboh.com/t/{token}` link
**When** the page loads
**Then** no authentication is required; no signup wall appears
**And** `StatementView` renders per UX-DR13: parties strip (lender `ContactAvatar` + name → recipient name) + per-denomination xl `AmountDisplay` balance + read-only transaction list + one-line legal disclaimer per FR39
**And** page loads in < 2s on Slow 4G per NFR-P5
**And** LCP < 2.5s
**And** `<html lang dir>` set per recipient's browser Accept-Language with toggle to switch
**And** `noindex, nofollow` meta + `X-Robots-Tag: noindex, nofollow` + `Cache-Control: private` headers
**And** the token is stripped from browser history on first navigation
**And** a first-render interstitial ("Tap to view statement") blocks preview-bot body renders from consuming the token
**And** telemetry event `statement_viewed` emits (IP-hashed, no PII) per AR-047

### Story 4.6: Recipient "Confirm" button timestamps agreement

As a recipient who verified the statement matches my memory,
I want to tap "Confirm" to timestamp agreement for both parties,
So that we have a mutually-acknowledged record without an awkward conversation (FR36).

**Acceptance Criteria:**

**Given** a valid statement view loaded for a recipient
**When** the recipient taps "تأكيد" (Confirm)
**Then** `POST /v1/statements/t/{token}/confirm` records the confirmation timestamp + recipient IP-hash + `ProcessingActivity.DebtorConfirmation`
**And** the button replaces with `ReconciliationMeta` showing "Confirmed: [now]" per UX-DR12
**And** the operation is idempotent (tapping twice produces the same timestamp)
**And** CSRF double-submit + `frame-ancestors 'none'` protections are in place
**And** the confirmation cannot be modified or deleted (append-only audit)
**And** telemetry event `statement_confirmed` emits per AR-047
**And** the owner does NOT receive a real-time notification at MVP (SignalR deferred to Epic 7); instead the confirmation appears on next session via Story 4.7's passive display

### Story 4.7: Owner sees passive last-confirmation date per contact

As an owner who shared a statement,
I want to see "Last reconciled: [date]" on my contact list when the recipient confirms,
So that I know the counterparty agrees without needing real-time notifications (FR37).

**Acceptance Criteria:**

**Given** a contact with ≥ 1 confirmed statement
**When** the owner loads `/contacts` or `/contacts/{id}` on any session after the confirmation
**Then** the contact row shows `ReconciliationMeta` with state `reconciled` + relative date ("3 days ago")
**And** hovering/focusing the element reveals the absolute date via PrimeNG Tooltip
**And** the element uses `<time datetime="ISO">` for screen readers
**And** contacts with shared-but-unconfirmed statements show `ReconciliationMeta` state `pending` with "Shared, awaiting confirmation"

### Story 4.8: Signup CTA designed as primary UX with funnel instrumentation

As Wahid,
I want the statement page's signup CTA designed as the primary conversion moment — above-the-fold after the debt summary — with the full viral funnel instrumented,
So that Phase-A viral coefficient is measurable from the first day of dogfood (FR38, Victor's Epic 4 reframe).

**Acceptance Criteria:**

**Given** a recipient has viewed a statement and tapped Confirm
**When** the post-confirmation view renders
**Then** the signup CTA renders prominently below the `ReconciliationMeta`: "Track your own debts — sign up free" with one-click flow that pre-fills the recipient's name from the statement
**And** telemetry events fire across the full funnel: `statement_viewed` → `cta_viewed` (scrolled past fold) → `cta_clicked` → `viewer_signup_from_share` → `first_debt_recorded` (attributed back to the share via `referred_by_statement_token` on user record)
**And** an A/B harness rotates 3-5 CTA copy variants (configured via env var)
**And** the viral-coefficient dashboard in App Insights computes `viewer_signup_from_share / statement_viewed` per cohort
**And** when the recipient signs up, their dashboard shows a one-time nudge: "You were viewing [lender's name]'s statement — record your side of this debt?" per PRD Critical Behavioral Flow #2

### Story 4.9: Token rate limiting — 20 links/hour/user + AFD edge rule

As Wahid,
I want link generation capped at 20 per user per hour + AFD edge rate-limit of 600 req/min per `/statement.*/t/*` + 60 views/minute per link,
So that statement-link abuse is rate-limited at two layers (FR42, NFR-S5, AR-017).

**Acceptance Criteria:**

**Given** a user attempts to generate 21 links in an hour
**When** the 21st request hits `POST /v1/contacts/{id}/statements`
**Then** HTTP 429 returns with RFC 9457 error `faktuboh.statements.link_generation_rate_limit_exceeded`
**And** the AFD WAF Standard ruleset enforces 600 req/min per `/statement.*/t/*` route
**And** per-token view rate cap at 60 req/min per NFR-S5
**And** rate-limit headers (`Retry-After`, `X-RateLimit-Remaining`) are included on 429 responses
**And** a user testing the "generate link" repeatedly in a short window sees the friendly inline error per UX-DR32

### Story 4.10: Token cryptographic generation with property tests

As Wahid,
I want capability tokens generated from a cryptographically secure RNG with ≥ 128-bit entropy, validated by property-based tests,
So that token guessability is infeasible (FR41, NFR-S4).

**Acceptance Criteria:**

**Given** a token is generated via `TokenGenerator.Create()`
**When** FsCheck property tests run
**Then** 10000 generated tokens have no duplicates
**And** token entropy measured via sample-variance test exceeds 128 bits
**And** tokens pass adversarial shape tests: truncated-by-1-char / tampered-middle-byte / replay-after-expiry / wrong-audience / expired-by-1ms — all reject with constant-time response
**And** `TokenGenerator` uses `RandomNumberGenerator.Create()` (not `Random`) with explicit unit test assertion

### Story 4.11: Epic 4 cost circuit-breaker (AR-050)

As Wahid,
I want a per-token day-view cap of 1000 views/token/day + daily €50 spend alert + hard kill-switch env flag + AFD 24h TTL on statement content,
So that one Twitter moment cannot bankrupt the free-tier runway (AR-050, Victor's insistence).

**Acceptance Criteria:**

**Given** a token has served 1000 views in a 24h rolling window
**When** the 1001st request arrives
**Then** the response is a static "This statement is receiving high volume — please try again later" HTML page (served from AFD cache, no origin hit)
**And** AFD cache-control on statement content is 24h with cache-bust on owner edit (`PATCH /v1/contacts/{id}/statements/{statementId}` purges the cached keys)
**And** a daily App Insights budget alert at €50/mo triggers paging to Wahid's email
**And** `FAKTUBOH_EMERGENCY_SHUTDOWN=true` env flag disables `/statement.*/*` routes within 60s of ACA restart (per Story 0.17)
**And** the kill-switch returns a generic "temporarily unavailable" page that does NOT reveal which statements existed

### Story 4.12: Constant-time-response timing test (silent-failure guard)

As Wahid,
I want a statistical timing test in CI that compares p99 response times between valid and invalid tokens across 10k requests each,
So that timing-oracle leaks — a silent-failure class that unit and integration tests cannot catch — are caught mechanically (Murat's landmine).

**Acceptance Criteria:**

**Given** `tests/Timing/` contains the harness
**When** `dotnet test --filter Category=Timing` runs nightly
**Then** 10k requests hit `/statement.faktuboh.com/t/{valid-token}` and 10k hit `/statement.faktuboh.com/t/{invalid-token}` against a staging-equivalent environment
**And** the p99 timing difference is < 5ms; the Welch's t-test p-value < 0.01 means fail
**And** the test runs nightly (not per-PR — too noisy) with trend tracking
**And** regression triggers an alert via the Golden-signals dashboard

### Story 4.13: k6 public-statement throughput harness

As Wahid,
I want a k6 scenario sustaining 166 rps against the public-statement route for 10 minutes,
So that NFR-SC4 (83 rps PRD commitment) has 2× headroom proven and AR-044 rehearsal #5 is executable (Murat's budget).

**Acceptance Criteria:**

**Given** `tests/Load/public-statement-throughput.js` exists
**When** `k6 run --vus 50 --duration 10m` executes against staging
**Then** sustained throughput reaches 166 rps with zero 5xx
**And** p95 < 300ms; p99 < 1.5s; error rate < 0.1%
**And** AFD cache-hit ratio > 95%
**And** results are posted to the Golden-signals dashboard
**And** this runs as part of every release-candidate acceptance suite

### Story 4.14: Capability-token replay-across-cache-boundary staged test

As Wahid,
I want a staged test deployed against AFD that generates a token, consumes it, rotates it, then attempts replay of the original URL,
So that Vary-header misconfiguration or AFD cache-key leak — the silent security failure Murat flagged — is caught before prod (AR-044 #6 validation).

**Acceptance Criteria:**

**Given** the staging environment is live
**When** the k6 cache-storm drill runs (500 rps burst for 30s with replay attempts)
**Then** no revoked or consumed token serves a cached response from AFD
**And** a rotated URL with old token returns the constant-time "unknown token" response, never the original statement
**And** cache-key includes token + Accept-Language + no other Vary dimensions
**And** CI gate asserts `Cache-Control` and `Vary` headers on `/t/*` responses match spec (prevents silent-decay from >95% hit-rate to 70%)

### Story 4.15: Public-SPA locale toggle + disclaimer (FR2 + FR7 spans)

As a recipient viewing a shared statement,
I want to toggle between Arabic and English via a language selector + see the one-line legal disclaimer in my selected language,
So that the statement page is fully bilingual despite being a separate Angular build target (FR2 span, FR7 span).

**Acceptance Criteria:**

**Given** `apps/public-statement` is loaded
**When** the user taps the language toggle in the minimal header
**Then** `<html lang dir>` flips; Transloco swaps all strings; the disclaimer text updates
**And** the disclaimer text is identical in content to Epic 1's owner-app disclaimer (NFR-A1 consistency)
**And** the toggle works without account context (no API call; client-side only)
**And** the `lang`/`dir` preference is stored in `localStorage` for return visits within the session

---

## Epic 5: Debt Transfer Between Parties

The user can transfer all or part of an outstanding debt from one contact to another in a single atomic journal entry with bidirectional audit badges. Proves the dual-entry pattern Epic 6 Exchange reuses.

### Story 5.1: Transfer flow UI with mandatory review dialog

As a user who wants to reassign a debt from Contact A to Contact B,
I want a guided flow that shows me the source debt + target contact + amount + review summary before confirming,
So that this irreversible compound operation feels safe (FR24 UI portion, UX-DR29).

**Acceptance Criteria:**

**Given** an authenticated user on a debt detail page
**When** the user taps "نقل الدَّين" (Transfer Debt)
**Then** the form asks: full or partial transfer → target contact (AutoComplete from existing contacts) → optional explanation note
**And** target = source validation blocks with inline error "Cannot transfer to same party"
**And** partial-transfer amount > remaining validates inline with "Amount exceeds balance"
**And** a review `p-dialog` shows "[Source contact] closes X — [Target contact] opens X — Both sides notified if shared" in plain language (no "debit/credit/journal entry" jargon)
**And** tapping "Confirm" submits; tapping "Cancel" returns to source without side effects
**And** the review step is the ONE routine-action in Faktuboh that shows a review dialog (all other routine mutations are silent-success per UX-DR27)

### Story 5.2: Atomic compound-journal-entry transfer (AR-049 first real consumer)

As a developer,
I want the Transfer backend to use the Compound-Journal-Entry interface (AR-049) with the balance invariant asserted at commit,
So that source-closes-X + target-opens-X write atomically through the same seam Epic 6 will consume (FR24 backend, FR25).

**Acceptance Criteria:**

**Given** a valid transfer request
**When** `POST /v1/journal-entries/transfer` executes with Idempotency-Key
**Then** the handler invokes `ICompoundJournalEntry` with two legs (source close, target open) sharing one `CorrelationId`
**And** both legs commit in a single database transaction (all-or-none)
**And** the balance invariant (`sum(debits) == sum(credits) per denomination`) is asserted at commit time
**And** both legs are bitemporal-tracked (history rows on subsequent edits)
**And** `TransferExecuted` integration event emits with source + target contact IDs + amount + `CorrelationId`
**And** the operation is idempotent via Idempotency-Key (concurrent-double-submit test passes)
**And** failure of either leg's write rolls back the entire operation (integration test asserts this)

### Story 5.3: AuditBadge component with bidirectional navigation

As a user viewing a transferred debt,
I want to see a clickable badge "Transferred from [source contact]" or "Transferred to [target contact]" that navigates me to the linked record,
So that I can trace the transfer chain from either side (FR25 UX, UX-DR15).

**Acceptance Criteria:**

**Given** a transferred debt on either side
**When** the debt row renders
**Then** `AuditBadge` component appears with `kind="transfer-from"` or `kind="transfer-to"` per UX-DR15
**And** direction-neutral icon (`pi-link`) is used — "↔" / "→" / "←" symbols replaced to avoid RTL/LTR confusion
**And** tapping the badge navigates via `routerLink` to the linked debt on the other side
**And** `aria-label` announces "View original debt with [Ali] that was transferred from" for screen readers
**And** focus ring applies to the outer anchor per `--focus-ring` token; badge `p-tag` is display-only

### Story 5.4: Transfer history view

As a user,
I want to see all transfers I've executed across all contacts in a chronological list,
So that I can audit my debt-reassignment activity (FR26).

**Acceptance Criteria:**

**Given** the user has ≥ 1 transfer
**When** the user navigates to `/transfers`
**Then** a chronological list shows source contact → target contact + amount + date + optional note
**And** each row shows `AuditBadge` components linking to both source and target debt records
**And** the list supports date-range filter
**And** empty-state shows "No transfers yet" factual copy (not apologetic) per UX-DR30
**And** transfers are excluded from both the Dashboard's contact bar chart and denomination pie chart (they reshape existing balances, they don't add to exposure)

### Story 5.5: Partial-transfer support

As Karim (PRD Journey 2),
I want to transfer $200 of a $500 debt while leaving $300 on the source contact,
So that I can handle the common real-world case where someone pays off part of their debt by assigning it elsewhere (FR24 partial variant).

**Acceptance Criteria:**

**Given** a source debt with a remaining balance of $500
**When** Karim executes a partial transfer of $200 to another contact
**Then** the source debt's remaining balance becomes $300 (bitemporal history records the reduction)
**And** the target contact has a new $200 debt with `AuditBadge kind="transfer-from"`
**And** both records share the same `CorrelationId` for audit traceability
**And** subsequent partial transfers from the same source further reduce the remaining balance
**And** attempting a partial transfer that equals the full remaining balance succeeds and closes the source debt entirely (equivalent to full-transfer semantics)

---

## Epic 6: Currency & Metal Exchange with Locked Rates

The user can convert a debt from one denomination to another at a locked rate, system proposes the current hourly rate with user-override, original denomination preserved in audit trail.

### Story 6.1: Hourly rate-feed ingestion with gold as base unit

As Wahid,
I want a Wolverine scheduled job that ingests exchange rates hourly using gold as the internal base unit, with same-transaction outbox safety on feed failures,
So that every supported denomination has a fresh rate available for Exchange operations (FR18, AR-006).

**Acceptance Criteria:**

**Given** a scheduled Wolverine job registered with a 1-hour schedule
**When** the job fires
**Then** it fetches rates from the primary rate-feed provider for gold, silver, and major currencies (USD, EUR, GBP, SAR, AED, TRY minimum per NFR-I1)
**And** rates are normalized with gold as the base unit (1 gram gold = X USD) at internal `numeric(28,8)` precision per AR-035 dual-precision rule
**And** rate records are persisted to `rates` table with `valid_from timestamptz` and `source_provider` columns
**And** feed failures trigger retry with exponential backoff (max 3 retries) per NFR-I2
**And** retry exhaustion enqueues to Wolverine dead-letter table with replay tooling
**And** full processing completes in < 30s per NFR-P8
**And** user-facing API response times remain within targets during feed processing per NFR-SC5 (zero error-rate increase)

### Story 6.2: Dual-provider redundancy with automatic failover

As Wahid,
I want the gold-base pricing to use two independent providers (primary + warm secondary) with automatic failover within 60 minutes of primary failure,
So that gold-base rate availability meets NFR-I6 on MVP Day 1.

**Acceptance Criteria:**

**Given** two rate-feed providers configured (primary + secondary)
**When** the primary returns 3+ consecutive failures or times out for ≥ 60 min
**Then** the secondary is activated automatically and a dashboard alert fires
**And** a reconciliation check compares primary + secondary rates when both are healthy; divergence > threshold triggers an alert (integration test asserts this)
**And** non-gold currency rates may use a single provider at MVP (per NFR-I6 documented scope)
**And** provider configuration is runtime (env var `RATE_FEED_PRIMARY` + `RATE_FEED_SECONDARY`) so a problem provider can be swapped without deploy

### Story 6.3: View current system rate pre-confirmation

As a user about to execute an exchange,
I want to see the current system rate between the two denominations before I confirm,
So that I can sanity-check the rate or agree on an overridden rate (FR23).

**Acceptance Criteria:**

**Given** an authenticated user on the Exchange form
**When** the user selects source + target denominations
**Then** `GET /v1/rates/current?from=GOLD_G&to=USD` returns the latest rate
**And** the rate is displayed inline (e.g., "1 gram gold = $92.50 today")
**And** the rate includes the `as_of` timestamp per NFR-I3
**And** rate response time < 100ms (indexed lookup)

### Story 6.4: Exchange operation with locked rate + user override (AR-049 second consumer)

As Nadia (PRD Journey 5),
I want to convert a 10g gold debt to USD at $95/g (overriding the system rate of $92.50/g) with the rate locked to this transaction forever,
So that Hassan's repayment in dollars is recorded correctly and the original denomination is preserved in the audit trail (FR19, FR21).

**Acceptance Criteria:**

**Given** an existing debt of 10g gold on a contact
**When** the user submits Exchange: convert 10g gold to USD at rate $95/g (override)
**Then** `POST /v1/journal-entries/exchange` uses `ICompoundJournalEntry` (AR-049) with two legs (source-close 10g gold, target-open $950 USD) sharing one `CorrelationId`
**And** the rate is locked at the time of the transaction; subsequent rate changes never auto-update this transaction
**And** the original denomination ("10g gold") is preserved in the audit row alongside the derived denomination ("$950 USD")
**And** audit trail line reads: "Exchange: 10g gold → $950 USD at $95/g (user-adjusted from system rate $92.50/g)"
**And** both legs are bitemporal-tracked
**And** the operation is idempotent via Idempotency-Key
**And** `DenominationPicker limited` variant filters target denominations to those compatible with source per UX-DR10

### Story 6.5: View historical exchange rate on past operations

As a user auditing past exchanges,
I want to see the exact rate that was locked for any historical exchange operation,
So that I can resolve disputes or verify calculations (FR20).

**Acceptance Criteria:**

**Given** a historical exchange entry
**When** the user navigates to the debt detail and taps "View exchange history"
**Then** the original denomination + target denomination + locked rate + timestamp all render
**And** the view shows whether the rate was user-overridden + the system rate at that time (if overridden)
**And** `AuditBadge kind="exchange-prior"` / `kind="exchange-derived"` links the two sides of the exchange

### Story 6.6: Denomination integrity enforcement

As a user with debts in multiple denominations,
I want Faktuboh to never implicitly convert or aggregate across denominations,
So that my gold debts stay in gold and my USD debts stay in USD — my view of position is never silently falsified (FR21).

**Acceptance Criteria:**

**Given** a contact with debts in USD, EUR, and gold grams
**When** any balance, sum, dashboard aggregation, or export runs
**Then** each denomination is computed and displayed separately
**And** no cross-denomination arithmetic exists in any code path (NetArchTest asserts)
**And** the Glance HeroTiles show per-denomination subtotals; the denomination pie chart shows slices per denomination unit count (not value-equivalent)
**And** a CSV export column lists denomination explicitly for every row

### Story 6.7: Historical rate storage for audit

As Wahid,
I want every fetched rate stored with `valid_from` + `source_provider` so that any past exchange can be audited against the rate that was actually used,
So that disputes can be resolved against immutable rate history (FR22).

**Acceptance Criteria:**

**Given** the `rates` table
**When** a rate is fetched and persisted
**Then** the row includes `denomination_from`, `denomination_to`, `rate` (numeric(28,8)), `valid_from timestamptz`, `source_provider`, `recorded_at timestamptz`
**And** rates are append-only (no UPDATE allowed; immutability asserted by DB trigger)
**And** queries to retrieve historical rates at a specific `valid_from` run in < 50ms (indexed)
**And** rate history is retained for 7 years minimum (aligned with audit retention per domain requirement)

### Story 6.8: Rate freshness SLA — banner and disable

As a user with stale exchange rates,
I want a non-dismissible banner when rates are > 4h old and exchange operations disabled when > 24h old,
So that I never execute an exchange at a rate that's obviously stale (NFR-I3, UX-DR32).

**Acceptance Criteria:**

**Given** the latest rate is > 4h but < 24h old
**When** any screen that uses or displays a rate renders
**Then** a non-dismissible `ContextualNudge severity="warn"` banner shows "Rates last updated [X] hours ago"
**And** when > 24h old, the Exchange form's submit button is disabled with explanation text "Exchange operations disabled — rates stale >24h"
**And** viewing historical rates + balances remains permitted (only writes are blocked)
**And** the banner cannot be suppressed by primary-provider outage alone (the freshness SLA applies to any provider, not just primary)
**And** clock-skew scenarios (local-host ≠ DB timestamp) are handled via DB-authoritative timestamps

### Story 6.9: WireMock cassette harness for feed-scenario tests

As Wahid,
I want a WireMock.NET cassette harness simulating 8-12 rate-feed failure scenarios (blackout, partial blackout, divergence above/below threshold, duplicate timestamp, out-of-order timestamp, stale 4h/24h, schema drift, auth failure),
So that AR-044 #3 rate-feed rehearsal can run in CI without hitting live providers (Murat's commitment).

**Acceptance Criteria:**

**Given** `tests/Infrastructure.Tests/RateFeedCassettes/` exists
**When** the cassette test suite runs
**Then** 8-12 scenarios each assert a specific system behavior: banner fires / disable activates / dead-letter enqueue / failover to secondary / reconciliation alert
**And** Pact consumer-side drift tests run for both providers (1 per provider)
**And** a property-based test (FsCheck) on the dual-provider reconciliation arbiter generates `(primaryResponse, secondaryResponse)` pairs and asserts correct winner selection
**And** `numeric(28,8)` round-trip precision tests verify chains of length 1-5 across currency conversions lose no precision

---

## Epic 7: Real-Time Notifications, Share Nudges, Fraud Reporting & Link Management

Retention/polish layer amplifying the viral loop Epic 4 proved. SignalR infrastructure, owner-side link management, contextual share-nudges, and recipient fraud-report affordance.

### Story 7.1: SignalR hub infrastructure

As a developer,
I want a SignalR hub wired at `/hubs/notifications` with auth context on the socket + automatic reconnection within 5 seconds,
So that Epic 7's remaining stories have real-time push primitives to consume (FR51, NFR-I4).

**Acceptance Criteria:**

**Given** the SignalR hub is registered in `Program.cs`
**When** an authenticated user's Angular SPA connects
**Then** the Bearer token is validated on the socket handshake; invalid tokens reject connection
**And** the Angular service wraps the hub with auto-reconnection (per NFR-I4 within 5s)
**And** `ProcessingActivity.OwnerLedger` flows as a socket header
**And** disconnection on network interruption is transparent (banner appears after 10s disconnect; clears on reconnect)
**And** outage fallback: when hub is unreachable for > 60s, the app falls back to next-session passive display (Story 4.7 behavior)
**And** SignalR concurrent-connection cap enforced per AR-050 cost-circuit-breaker (graduated: 500 concurrent at MVP scale)

### Story 7.2: Real-time notification on counterparty confirmation

As an owner who shared a statement,
I want to receive a real-time push the moment the recipient taps "Confirm,"
So that I see "Last reconciled: [just now]" instantly rather than on next session (FR50).

**Acceptance Criteria:**

**Given** an owner is authenticated with an active SignalR connection
**When** a recipient confirms the owner's shared statement
**Then** the owner's SPA receives a `StatementConfirmed` message via SignalR within 1s of the confirmation per NFR-P9
**And** the `ReconciliationMeta` component updates reactively from `pending` to `reconciled` without manual refresh
**And** if the owner is not connected (session closed), the next load shows the confirmation via Story 4.7 passive display
**And** an `aria-live="polite"` announcement surfaces the confirmation to screen readers

### Story 7.3: Manage and revoke statement links

As an owner,
I want to view all my generated statement links and revoke any specific link,
So that I can kill a link I sent by mistake or no longer need (FR40).

**Acceptance Criteria:**

**Given** an owner has generated ≥ 1 link
**When** the user navigates to `/statements/links`
**Then** a list renders each link with: linked contact, generation date, expiration date, view count, confirmation status, revoke action
**And** tapping "Revoke" on a link shows `ConfirmDialog`; confirming marks the link revoked + purges AFD cache keys for that token
**And** the revoked link returns a neutral "link expired — contact the sender" page without exposing prior statement data or link ownership per NFR-S11
**And** the list supports filter by status (active / expired / revoked)
**And** empty state: "No statement links yet" factual copy

### Story 7.4: Share nudge — after first 3 debts per contact

As a user recording multiple debts with a new contact,
I want to be prompted (non-blocking) to share the statement after each of the first 3 debts recorded per contact,
So that the viral loop activates during the highest-engagement moments (FR52a).

**Acceptance Criteria:**

**Given** a contact has 1, 2, or 3 debts recorded
**When** the user lands on the dashboard or the contact detail page
**Then** `ContextualNudge kind="share-first-statement"` renders with text "Share the statement with [contact name]?"
**And** the nudge is non-blocking and dismissible
**And** tapping the nudge opens the share flow (Story 4.4)
**And** the nudge disappears after the 3rd debt per contact OR after the user shares at least one statement for that contact

### Story 7.5: Share nudge — 7-day unshared staleness

As a user with debts recorded but not shared,
I want a reminder when recorded-but-unshared transactions with a contact are > 7 days old,
So that I reconcile statements before they age into forgotten territory (FR52b).

**Acceptance Criteria:**

**Given** a contact has ≥ 1 debt older than 7 days that has never been included in a shared statement
**When** the user lands on that contact's detail page
**Then** `ContextualNudge kind="staleness-reminder"` renders with text "You haven't shared [contact name]'s statement in over 7 days"
**And** tapping opens the share flow
**And** dismissing the nudge 3 consecutive times on the same contact suppresses for 30 days per PRD FR52 rules

### Story 7.6: Share nudge — material-threshold balance crossing

As a user whose balance with a contact crosses a configurable material threshold,
I want a reminder to share the statement when a denomination crosses >€50-equivalent,
So that high-value balances are reconciled proactively (FR52c).

**Acceptance Criteria:**

**Given** a contact's balance in any denomination crosses the material threshold (default €50-equivalent; configurable)
**When** the user lands on that contact's detail page or the dashboard
**Then** `ContextualNudge kind="threshold-crossing"` renders with text "[Contact]'s balance crossed [threshold]. Share to reconcile?"
**And** threshold is configurable per user in profile settings (Phase-2 UX; MVP uses fixed default)
**And** dismiss-cooldown of 30 days applies

### Story 7.7: Recipient fraud-report affordance

As a recipient of a shared statement who believes it is fraudulent or harassing,
I want a "Report as fraudulent or harassing" button visible on the statement page that queues a report to admin review,
So that I have recourse against bad-actor senders (FR53).

**Acceptance Criteria:**

**Given** a recipient on a valid statement view
**When** the recipient taps "Report"
**Then** `POST /v1/statements/t/{token}/report` submits with optional free-text reason + pre-defined category (fraud / harassment / other)
**And** the submission includes link ID, recipient IP-hash (for rate-limiting reports), report reason, free-text (optional)
**And** reports do NOT auto-revoke the link (admin review in FR46/FR47 decides)
**And** the recipient sees a neutral confirmation page ("Report received — thank you") without feedback on the link owner's identity (protects recipient from retaliation signal)
**And** the report is persisted to `admin_review_queue` table consumed by Epic 8's FR46 admin-investigation flow
**And** recipient fraud-report rate is capped per IP-hash (max 10 reports per IP-hash per 24h)

---

## Epic 8: Platform Operations, Admin & Operational Safety Jobs

Admin surfaces + operational safety jobs (restore/rotate/balance-integrity) that require real production data and thus cannot land earlier.

### Story 8.1: Admin dashboard with platform metrics

As Wahid (operations),
I want an admin dashboard showing active users + new registrations + statement links generated + confirmation rates + viral coefficient,
So that I can monitor platform health and Phase-A success metrics (FR43).

**Acceptance Criteria:**

**Given** an elevated admin session (per NFR-S10 separate elevated authentication)
**When** the admin navigates to `/admin/dashboard`
**Then** metrics render: active-user count (7-day rolling) / new registrations (week) / statement-links-generated (week) / confirmation rate / viral coefficient (signups-from-shares / views) / Phase-A activation rate
**And** data sources are App Insights KQL queries (reused from Story 0.11's four dashboards)
**And** minimum-k policy enforced: aggregate rows with < 5 underlying natural persons are suppressed per AR-043
**And** admin operations are logged in the audit trail per NFR-S10
**And** the dashboard is accessible to a user with the `admin` role claim; unauthorized access returns 403 with RFC 9457

### Story 8.2: Freemium threshold configuration UI

As Wahid,
I want to configure freemium thresholds (transactions per user, contacts per user) via an admin UI,
So that when the Revenue-readiness trigger fires I can tune thresholds without a code deploy (FR44).

**Acceptance Criteria:**

**Given** an elevated admin session
**When** the admin navigates to `/admin/freemium`
**Then** current thresholds display (transactions_per_user, contacts_per_user — defaults: 500 / 10) as runtime config
**And** editing + saving persists via `PATCH /v1/admin/freemium-thresholds` with audit log entry
**And** the enforcement is runtime (no deploy required) — thresholds update on next request cycle
**And** thresholds ship disabled (enforcement_enabled = false) at MVP per PRD Revenue-readiness gate
**And** a master toggle `enforcement_enabled` is visible but defaults false; a tooltip warns "Enabling activates freemium gates platform-wide"

### Story 8.3: Grandfathering rules management

As Wahid,
I want to manage grandfathering rules for users who exceeded thresholds before freemium was enabled,
So that existing users aren't abruptly capped when enforcement activates (FR45).

**Acceptance Criteria:**

**Given** freemium enforcement is about to activate
**When** the admin navigates to `/admin/freemium/grandfathering`
**Then** a list shows users currently above thresholds with their existing counts
**And** bulk actions can mark users as "grandfathered" (permanent exemption) or "grace period" (30-day overage window)
**And** grandfathered users are exempt from future enforcement checks
**And** rules are persisted to `grandfathering_rules` table with audit entries

### Story 8.4: Admin investigation and flagging

As Wahid,
I want to investigate fraud-report submissions (from Story 7.7) + flag accounts showing abusive patterns,
So that bad-actor senders are surfaced and addressed (FR46).

**Acceptance Criteria:**

**Given** fraud reports exist in `admin_review_queue` (from Story 7.7)
**When** the admin navigates to `/admin/review`
**Then** a queue list shows pending reports with: link ID, recipient IP-hash, reason, free-text, timestamp
**And** tapping a report opens a detail view with the reported link's owner account + recent link-generation activity
**And** the admin can mark "no action" (close report) OR "flag account" (triggers Story 8.5 suspension)
**And** each action is logged in the audit trail
**And** minimum-k policy means rare single-report accounts are flagged only for repeated patterns (≥ 3 reports within 7 days) automatically; manual single-report flagging always available

### Story 8.5: Admin suspend statement link generation

As Wahid,
I want to temporarily suspend statement link generation for a flagged account,
So that abuse investigations don't require disabling the entire account (FR47).

**Acceptance Criteria:**

**Given** an account flagged in Story 8.4
**When** the admin taps "Suspend link generation"
**Then** a record in `account_suspensions` table marks the account with `statement_links_suspended=true` + reason + expiration date
**And** attempts to call `POST /v1/contacts/{id}/statements` return RFC 9457 `faktuboh.admin.account_suspended` with HTTP 403
**And** existing links remain active (suspension is forward-looking only)
**And** suspension can be lifted via the same admin UI with audit trail entry

### Story 8.6: Freemium limit enforcement (ships disabled; activates via Revenue-readiness)

As Wahid,
I want the freemium enforcement code path implemented and tested but dormant (enforcement_enabled=false) until the Revenue-readiness trigger fires,
So that viral growth is not throttled pre-revenue (FR48, PRD freemium enforcement timing).

**Acceptance Criteria:**

**Given** enforcement_enabled=false (MVP default)
**When** a user exceeds a threshold
**Then** no restriction applies; the action proceeds normally
**And** a non-blocking banner at 90% of threshold: "You're approaching your free tier limit" (UX implemented + tested but silent at MVP)
**And** when enforcement_enabled=true, reaching the limit blocks the capped action (add-contact, record-debt) with upgrade prompt: "You've reached the free tier limit. Upgrade to Pro ($2.50/month or $20/year) to unlock unlimited"
**And** viewing, sharing, and exporting are NEVER restricted (the paywall gates creation, not access) per PRD Critical Behavioral Flow #3
**And** integration tests cover both enforcement_enabled=true AND enforcement_enabled=false paths

### Story 8.7: Daily balance-integrity Wolverine job (AR-051 / NFR-R6)

As Wahid,
I want a daily scheduled Wolverine job that checks `sum(transactions) == stored_balance` for every contact-denomination pair and alerts within 1 hour on mismatch,
So that silent ledger divergence is detected fast (NFR-R6, AR-051).

**Acceptance Criteria:**

**Given** a Wolverine scheduled job registered for daily execution at 03:00 UTC
**When** the job runs
**Then** every contact-denomination pair is compared: sum of individual transactions vs stored balance
**And** an inconsistency = difference > smallest unit of the denomination ($0.01 for USD, 0.001g for gold)
**And** any inconsistency triggers an App Insights alert to Wahid's email within 1 hour of detection per NFR-R6
**And** the alert includes: contact ID (opaque), denomination, computed vs stored amount, discrepancy
**And** the admin dashboard (`/admin/integrity`) lists unresolved integrity alerts
**And** a runbook `docs/runbooks/balance-integrity-alert.md` documents triage steps
**And** a synthetic-divergence integration test verifies the alert path end-to-end

### Story 8.8: Restore-under-pressure tabletop rehearsal

As Wahid,
I want a rehearsed Postgres point-in-time-restore from a 30-day-old backup with KMS-encrypted field round-trip validation,
So that AR-044 #1 is executable before first paying user (RT-46 hard gate).

**Acceptance Criteria:**

**Given** a 30-day-old automated backup exists in Azure
**When** the restore runbook is executed in staging
**Then** PITR completes within documented RPO/RTO targets
**And** the anonymization log replays as step zero (no erased PII reintroduced)
**And** KMS-encrypted field round-trip validates (per-subject DEK rotation survives restore)
**And** post-restore, the staging environment passes the full integration test suite
**And** the rehearsal outcome is documented in `docs/runbooks/restore-rehearsal-YYYY-MM-DD.md`
**And** the rehearsal is repeated quarterly

### Story 8.9: Cross-jurisdictional legal-hold drill

As Wahid,
I want a simulation of a Saudi court order arriving during an in-flight GDPR erasure request, with deterministic precedence resolution,
So that AR-044 #2 is validated before real production collisions (LegalHold precedence).

**Acceptance Criteria:**

**Given** a test user has submitted an in-flight GDPR erasure request
**When** a simulated `LegalHold` with `Jurisdiction=SA` + `State=Issued` is created targeting the same subject
**Then** the erasure request is deferred; a `DeferredErasure` record is created
**And** the precedence rule resolves (Saudi hold suspends GDPR erasure) with full audit trail
**And** when the `LegalHold` transitions to `Released`, the deferred erasure executes automatically
**And** the audit log shows the suspension + release events with timestamps
**And** the drill is repeated annually

### Story 8.10: Secrets rotation dry-run (Auth0 + KEK)

As Wahid,
I want quarterly rotation of Auth0 client-secret + KEK rehearsed end-to-end in staging before first paying user,
So that AR-046 is validated and rotation is never attempted in production without prior rehearsal.

**Acceptance Criteria:**

**Given** an Auth0 client-secret and a Key Vault Standard KEK are in staging
**When** the rotation procedure is executed
**Then** a new Auth0 client-secret is generated, new KEK version is created
**And** all DEKs are re-wrapped with the new KEK (integration test asserts every `subject_keys` row has new KEK version)
**And** old Auth0 client-secret remains valid during grace window (≥ 24h); old KEK version remains decryption-only
**And** grace window elapses and old secrets are decommissioned
**And** the integration test suite passes post-rotation
**And** the procedure is documented in `docs/runbooks/secrets-rotation.md` with exact commands

### Story 8.11: NFR-SC4 full load test execution

As Wahid,
I want the k6 load-test baseline harness (from Story 0.13) executed at production-representative scale targeting 83 rps sustained + 500 rps cache-sizing posture,
So that NFR-SC4 is formally validated before opening beta (AR-044 rehearsal tied to real traffic).

**Acceptance Criteria:**

**Given** production-representative data + environment
**When** `k6 run tests/Load/production-representative.js` executes
**Then** 83 rps sustained for 30 minutes holds with all performance targets met
**And** a 500 rps burst for 30s absorbs at AFD edge with cache-hit > 95%; origin cache-miss tail does not exceed 50 rps
**And** Wolverine DLQ remains < 50 rows
**And** p95 < 300ms, p99 < 1.5s, error rate < 0.1%
**And** the results are documented in `docs/runbooks/load-test-YYYY-MM-DD.md`
**And** any regression from staging load-test results triggers investigation before beta launch
