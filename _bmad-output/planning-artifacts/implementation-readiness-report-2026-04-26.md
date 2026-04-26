---
stepsCompleted:
  - step-01-document-discovery
  - step-02-prd-analysis
  - step-03-epic-coverage-validation
  - step-04-ux-alignment
  - step-05-epic-quality-review
  - step-06-final-assessment
assessmentVerdict: READY (post-resolution)
originalVerdict: NEEDS MINOR WORK
verdictUpdatedOn: 2026-04-26
assessor: Claude (Opus 4.7) per bmad-check-implementation-readiness skill
assessmentDate: 2026-04-26
resolutions:
  - misalignment-1-charts: resolved 2026-04-26 (option 3 — chart support retained with restraint envelope; ux-design-specification.md amended in two places)
  - misalignment-2-bundle-budget: resolved 2026-04-26 (ux-design-specification.md:3180-3181 split into owner vs public)
filesIncluded:
  prd: _bmad-output/planning-artifacts/prd.md
  prd_validation_report: _bmad-output/planning-artifacts/prd-validation-report.md
  architecture: _bmad-output/planning-artifacts/architecture.md
  adrs:
    - _bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md
    - _bmad-output/planning-artifacts/adrs/adr-022-application-level-bitemporal.md
    - _bmad-output/planning-artifacts/adrs/adr-023-resend-transactional-email.md
    - _bmad-output/planning-artifacts/adrs/adr-024-minimal-apis-framework.md
    - _bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md
  epics: _bmad-output/planning-artifacts/epics.md
  stories_root: _bmad-output/implementation-artifacts/
  stories_count: 91
  ux: _bmad-output/planning-artifacts/ux-design-specification.md
historical_reference_only:
  - _bmad-output/planning-artifacts/adr-resolution-draft.md  # MERGED — folded into architecture.md and prd.md per its own frontmatter
  - _bmad-output/planning-artifacts/product-brief-faktuboh.md
  - _bmad-output/planning-artifacts/product-brief-faktuboh-distillate.md
---

# Implementation Readiness Assessment Report

**Date:** 2026-04-26
**Project:** Faktuboh

## Step 1 — Document Discovery

### Inventory

| Type           | Canonical File                                   | Notes                                                  |
| -------------- | ------------------------------------------------ | ------------------------------------------------------ |
| PRD            | `prd.md` (64,605 B, Apr 22)                      | Adversarial-review fixes (R-01…R-10) merged 2026-04-22 |
| PRD validation | `prd-validation-report.md` (78,514 B, Apr 22)    | Supplemental — known gaps already raised               |
| Architecture   | `architecture.md` (234,582 B, Apr 22)            | Merged 2026-04-22 to absorb adversarial-review fixes   |
| ADRs           | `adrs/adr-021..025` (5 files, Apr 21)            | Auth0, bitemporal, Resend, Minimal APIs, Key Vault Std |
| Epics          | `epics.md` (172,649 B, Apr 22)                   | 9 epics (0–8)                                          |
| Stories        | `_bmad-output/implementation-artifacts/*.md`     | 91 story specs, named `<epic>-<story>-<slug>.md`       |
| UX             | `ux-design-specification.md` (186,421 B, Apr 21) | + 6 HTML direction prototypes                          |

### Story-set composition

| Epic      | Theme                                                                                | Story count |
| --------- | ------------------------------------------------------------------------------------ | ----------- |
| 0         | Foundations (CI/CD, observability, NetArchTest, idempotency, problem-details, etc.)  | 17          |
| 1         | Bilingual shell, auth, profile, account deletion                                     | 11          |
| 2         | Contacts                                                                             | 5           |
| 3         | Debts, Glance, dashboard charts, audit trail, CSV export                             | 11          |
| 4         | Public Statements (capability tokens, public SPA, throughput harness)                | 15          |
| 5         | Transfers (compound journal entries)                                                 | 5           |
| 6         | Exchange / FX rates (gold base, dual provider)                                       | 9           |
| 7         | Realtime (SignalR), share nudges, fraud reporting                                    | 7           |
| 8         | Admin / operational (freemium, drills, daily integrity)                              | 11          |
| **Total** |                                                                                      | **91**      |

### Issues

- **Duplicates (whole vs sharded):** none
- **Missing required documents:** none
- **Excluded from scope:** `adr-resolution-draft.md` (status: MERGED), product brief files (Phase 1 outputs)

## Step 2 — PRD Analysis

### Functional Requirements

**Total FRs: 53** (FR1–FR53, sequentially numbered, no gaps)

#### User Management (FR1–FR7)

- **FR1:** User can register a new account with email and password
- **FR2:** User can authenticate using their credentials
- **FR3:** User can reset their password via email. *Delivery: Auth0 hosted flow (per ADR-021) — email composed, signed, and sent by Auth0; satisfied by configuration, not in-app code.*
- **FR4:** User can set their preferred language (Arabic or English)
- **FR5:** User can view and edit their profile information
- **FR6:** User can delete their account and all associated data (GDPR right to deletion)
- **FR7:** User can view a legal disclaimer explaining that tracked debts are user records, not legally binding contracts

#### Contact Management (FR8–FR12)

- **FR8:** User can add a new contact with a name and optional details (phone, email, notes)
- **FR9:** User can edit an existing contact's information
- **FR10:** User can delete a contact (with warning if outstanding balances exist)
- **FR11:** User can view a list of all their contacts with summary balances per denomination
- **FR12:** User can search and filter contacts by name

#### Debt Management (FR13–FR17)

- **FR13:** User can record a new debt (lent or borrowed) with a contact, specifying amount, denomination (currency or precious metal), date, and optional story/context
- **FR14:** User can edit an existing debt entry (with audit trail preserved)
- **FR15:** User can delete a debt entry (with audit trail preserved)
- **FR16:** User can view full transaction history with a specific contact, with date range filter; when applied, system rolls up all transactions before filter start into a single "previous balance" per denomination
- **FR17:** User can record debts in any supported denomination — debts preserved in original denomination, never auto-converted

#### Currency & Exchange Operations (FR18–FR23)

- **FR18:** System fetches exchange rates for all supported currencies and metals every hour, using gold as internal base unit
- **FR19:** User can perform a currency/metal exchange operation between two denominations for a specific contact. System auto-populates rate from latest hourly feed; user can override manually. Rate locked at transaction time.
- **FR20:** User can view exchange rate used for any historical exchange operation
- **FR21:** System preserves denomination integrity — no implicit or automatic conversions between denominations
- **FR22:** System stores historical exchange rate data for reference and audit
- **FR23:** User can view current system exchange rate between any two supported denominations before confirming exchange

#### Debt Transfer (FR24–FR26)

- **FR24:** User can transfer all or part of a debt from one contact to another via a journal entry
- **FR25:** System records both sides of the transfer as linked transactions with a clear audit trail
- **FR26:** User can view transfer history showing origin and destination of transferred debts

#### Account Statements & Dashboard (FR27–FR33)

- **FR27:** User can view a dashboard showing overview of all contacts with balances per denomination
- **FR28:** User can view per-contact account statement with full transaction history and current balance per denomination
- **FR29:** User can generate an account statement report for a specific contact
- **FR30:** User can export their data as CSV (all contacts, all transactions)
- **FR31:** Dashboard displays user's overall financial position across all contacts and denominations without cross-currency aggregation
- **FR32:** Dashboard displays a bar chart of debt distribution across contacts
- **FR33:** Dashboard displays a pie chart of debt distribution across denominations

#### Statement Sharing & Confirmation (FR34–FR42, FR52, FR53)

- **FR34:** User can generate a unique shareable link for any contact's account statement
- **FR35:** Statement recipient can view shared statement in mobile-optimized page without creating an account
- **FR36:** Statement recipient can confirm statement via "Confirm" button, recording confirmation date
- **FR37:** User can see last confirmation date for each contact's statement
- **FR38:** Shared statement pages display Faktuboh branding and a sign-up CTA
- **FR39:** Shared statement pages display legal disclaimer and watermark indicating user-generated record
- **FR40:** User can manage (view and revoke) their generated statement links
- **FR41:** System generates cryptographically random, non-guessable tokens for statement links
- **FR42:** System limits statement link generation to 20 links per user per hour. *Magic-link delivery via Auth0 Passwordless (per ADR-021) — rate limit also enforced at Auth0 tenant level.*
- **FR52:** System prompts user to share a statement at activation moments: (a) after each of first 3 debts per contact; (b) when a contact has recorded-but-unshared transactions >7 days old; (c) when a contact's balance crosses material threshold (default: any denomination >€50 equivalent). Non-blocking, dismissible; 3 consecutive dismissals → no re-nudge for 30 days.
- **FR53:** Shared statement pages display "Report as fraudulent or harassing" affordance. Reports queue to admin (FR46) with link ID, recipient IP-hash, reason, optional free-text. No auto-revoke; admin (FR47) decides. Recipient sees neutral confirmation, no link-owner identity disclosure.

#### Administration & Platform Operations (FR43–FR49)

- **FR43:** Admin can view dashboard showing platform metrics (active users, registrations, statement links generated, confirmation rates)
- **FR44:** Admin can configure freemium thresholds (transaction count, contact count)
- **FR45:** Admin can manage grandfathering rules for existing users transitioning to freemium
- **FR46:** Admin can investigate and flag accounts for potential abuse
- **FR47:** Admin can temporarily suspend statement link generation for a flagged account
- **FR48:** System enforces freemium limits for new users based on configured thresholds
- **FR49:** System logs all debt operations (create, edit, delete, transfer, exchange) in immutable audit trail

#### Real-Time Notifications (FR50–FR51)

- **FR50:** User receives a real-time notification when a counterparty confirms their statement
- **FR51:** System maintains real-time push connection with automatic reconnection on network interruption

### Non-Functional Requirements

**Total NFRs: 50** (12 Performance + 11 Security + 6 Scalability + 9 Accessibility + 6 Integration + 6 Reliability)

#### Performance (12 metrics — tabulated, not all individually labeled)

| # | Metric | Target |
| --- | --- | --- |
| **NFR-P1 (landing)** | Landing-page load | FCP < 1.5s, LCP < 2.5s |
| **NFR-P1 (public)** | Public-statement bundle | initial JS < 180 KB gz, LCP < 2.5s on Slow 4G |
| **NFR-P1 (owner)** | Owner auth-shell bundle | initial JS < 250 KB gz |
| Perf-4 | SPA initial load | TTI < 3s |
| Perf-5 | Debt recording op | < 500ms end-to-end |
| Perf-6 | Shared statement page | < 2s |
| Perf-7 | Dashboard with charts | < 2s |
| Perf-8 | Balance calculation per contact | < 200ms (incl. date-range rollup) |
| Perf-9 | Exchange rate feed | < 30s per hourly cycle |
| Perf-10 | Real-time notification | < 1s from event |
| Perf-11 | CSV export (≤10K txns) | < 10s |
| Perf-12 | Concurrent users | 500 simultaneous, all targets above |

#### Security (NFR-S1 – NFR-S11)

- **NFR-S1:** All data encrypted at rest using provider-managed keys; in transit TLS 1.2+
- **NFR-S2:** Password hashing — **bcrypt cost ≥ 12** OR **Argon2id (memory ≥ 64 MiB, iterations ≥ 3, parallelism ≥ 1)**; reviewed every 24 months against OWASP ASVS v5.x; never plaintext
- **NFR-S3:** Session management — secure tokens (XSS/CSRF-safe attrs); 30-min inactivity timeout with auto-extension on activity
- **NFR-S4:** Statement link tokens — cryptographically random, min 128-bit entropy, non-sequential, non-guessable
- **NFR-S5:** Rate limiting — max 10 failed auth/15 min/IP; max 20 statement links/user/hr; max 60 page views/link/min
- **NFR-S6:** Audit trail append-only and immutable — no API or UI path allows modification or deletion
- **NFR-S7:** GDPR — data deletion anonymizes audit records (retains structure without PII), exports all user data on request, consent management
- **NFR-S8:** Shared statement pages expose no internal IDs, credentials, or data beyond specific contact's statement
- **NFR-S9:** Input validation on all user-facing endpoints — XSS, SQL injection, CSRF protection
- **NFR-S10:** Admin operations — separate elevated authentication, logged in audit trail
- **NFR-S11:** Statement links default to **30-day expiration**; expired links return neutral page; expiration server-enforced

#### Scalability (NFR-SC1 – NFR-SC6)

- **NFR-SC1:** 10,000 registered users with up to 500 concurrent active at MVP launch
- **NFR-SC2:** 10× user growth (100,000) with infrastructure scaling and < 10% performance degradation
- **NFR-SC3:** DB schema supports queries within performance targets when filtered by user, contact, date, denomination across large transaction histories
- **NFR-SC4:** Statement link pages handle traffic spikes independently of main SPA — isolated serving path. **Sustained: 5,000 RPM (~83 RPS)**, no SPA response increase. Burst SLA not a PRD contract.
- **NFR-SC5:** Exchange rate feed processing scales independently; user-facing API response times remain within targets, zero error rate increase
- **NFR-SC6:** Infrastructure supports horizontal scaling of web tier, vertical scaling of DB tier

#### Accessibility (NFR-A1 – NFR-A9)

- **NFR-A1:** WCAG 2.1 AA compliance across all user-facing pages (SPA, landing, shared statement)
- **NFR-A2:** Full keyboard navigation — all interactive elements reachable and operable
- **NFR-A3:** Screen reader — ARIA per **WCAG 2.1 SC 1.3.1 (Info and Relationships) and SC 4.1.2 (Name, Role, Value)**, semantic HTML5 landmarks, ARIA live regions
- **NFR-A4:** Color contrast — 4.5:1 normal, 3:1 large
- **NFR-A5:** RTL accessibility — bidirectional text correct in screen readers; logical reading order
- **NFR-A6:** Touch targets ≥ 44×44px on mobile
- **NFR-A7:** Form validation errors associated with fields, announced to AT
- **NFR-A8:** Focus management on SPA route changes, modals, dynamic content
- **NFR-A9:** **RTL quality acceptance gate** — 6 named scenarios (a–f) must pass on ≥2 RTL-native reviewer devices (mobile + desktop). Ship-blocker.
  - (a) Mixed Arabic + Latin-numeral transaction stories with bidi isolation
  - (b) Numeral display — user-configurable Latin (0-9) vs Eastern-Arabic (٠-٩); default per locale
  - (c) Bidi isolation in transaction notes with embedded Latin/URLs
  - (d) RTL layout in dashboard bar + pie charts (legends, axes, tooltips, labels)
  - (e) RTL CSV export — column order reversed for Arabic locale; UTF-8 with BOM; correct currency symbol position
  - (f) Right-aligned date-range picker with Hijri-calendar display (read-only at MVP; Gregorian primary)

#### Integration (NFR-I1 – NFR-I6)

- **NFR-I1:** Exchange rate API supports gold, silver, USD, EUR, GBP, SAR, AED, TRY at minimum
- **NFR-I2:** Hourly schedule with retry logic — max 3 retries with exponential backoff
- **NFR-I3:** Graceful degradation if feed unavailable — last known rates with timestamp banner. **Freshness SLA:** > 4h triggers non-dismissible banner; > 24h **disables exchange operations (FR19)** with explicit disabled-state explanation; viewing historical rates and balances permitted.
- **NFR-I4:** Real-time connection auto-reconnect within 5 seconds
- **NFR-I5:** CSV export compatible with Excel, Google Sheets, UTF-8 for Arabic content
- **NFR-I6:** **Rate feed redundancy** — gold-base pricing has 2 independent providers from MVP day 1 (primary + warm secondary; auto-failover within 60 min). Non-gold currency rates may share single provider with documented RT for secondary onboarding.

#### Reliability (NFR-R1 – NFR-R6)

- **NFR-R1:** 99.9% uptime (< 8.76 hours downtime/year)
- **NFR-R2:** Zero data loss for committed transactions — durably persisted before user confirmation
- **NFR-R3:** Automated DB backups with point-in-time recovery, ≥ 30-day retention
- **NFR-R4:** Health monitoring + alerting for web tier, DB, real-time service, exchange rate feed
- **NFR-R5:** Graceful error handling — UI errors include problem description + suggested corrective action; detailed server-log context; no silent failures
- **NFR-R6:** DB integrity checks for balance inconsistencies — sum of transaction amounts vs stored balance must match within smallest denomination unit. Daily checks; alert within 1 hour of detection.

### Additional Requirements (Constraints, Technical, Business)

#### Compliance & Regulatory

- **GDPR data residency:** Primary data residency in Azure Europe; right to data export (CSV), right to deletion, consent management, data processing transparency
- **GDPR Right to Erasure vs. Audit Integrity:** On account deletion, anonymize audit records — replace PII (user name, contact names, email) with opaque identifiers (e.g., "User-7a3f"). Retain transaction amounts, dates, denominations, mathematical relationships. Anonymization must be irreversible (no lookup table).
- **Legal Disclaimer:** Visible during registration AND on shared statement pages — debts are user records, not legally binding contracts
- **No Financial Services Licensing:** No KYC/AML, PCI DSS, or financial services licensing at this stage (no payments, no held funds, no advice)

#### Technical Constraints

- **Hosting:** Azure, Europe region (driven by GDPR residency)
- **Audit log retention:** 7 years from entry creation OR until anonymization on account deletion (whichever first); aligned with GDPR data minimization
- **Audit log entry fields:** timestamp, actor (user ID), action type, affected entities, before/after values, source IP
- **Audit log access:** system administrators only via authenticated admin interface; no user-facing access to raw audit data
- **Project Type:** Angular 21 SPA (greenfield) + static landing pages (SSR/pre-rendered for SEO) + lightweight unauthenticated shared statement pages (noindex, nofollow)
- **Browser support:** Chrome / Firefox / Safari / Edge — latest 2 versions; Samsung Internet — latest (secondary)
- **Responsive breakpoints:** mobile < 768px, tablet 768–1024px, desktop > 1024px
- **State management:** Angular Signals as default (per architecture D4.13). NgRx Signal Store as **explicit per-slice escape hatch** when derived state spans ≥ 2 slices, pagination/SWR exceeds `rxResource()`, or optimistic updates need `withEntities`. Documented in slice README.
- **i18n:** Angular built-in i18n or ngx-translate; RTL via `dir` attribute and CSS logical properties (`margin-inline-start` etc.)
- **Real-time:** SignalR with token-based auth, automatic reconnection
- **No PWA at MVP** — graceful degradation under intermittent network only

#### Business / Schedule Constraints

- **MVP ship milestones (from 2026-04-22 architecture-completion baseline):**
  - Day 35 — solo-dev internal dogfood
  - Day 90 — alpha (invite-only)
  - Day 180 — public launch
- **Operating cost (phase-aware, reconciled with architecture §7.4.1):**
  - **Months 1–12 (Azure free-tier runway):** ≤ €100/month at any volume up to NFR-SC2
  - **Months 13+ (post-cliff, RT-47 fires):** ≤ €135/month at 10K users AND ≤ €250/month at 100K users
  - Either ceiling breach → architecture RT-47 descope review; €0/month founder tolerance is a post-launch goal (RT-cost-reduction), not unmet commitment
- **Phase A (Bootstrap, Months 0–3):** ≥ 40% acquired-cohort activation, ≥ 5 new users/week by Month 2, founder-led onboarding ≥ 1/10 signups
- **Phase A → Phase B transition criterion** (all hold ≥ 3 weeks): viral coefficient ≥ 0.3, activation ≥ 40%, weekly signup floor met for last 4 weeks
- **Phase A escape clause:** 6 months from public launch without transition criterion firing → invoke `bmad-correct-course`
- **Phase B (Viral-era steady state):** 10,000 users at 6 months post-Phase-B entry, 60%+ activation, 40%+ retention at 90 days, 30%+ viral signal
- **Revenue readiness trigger:** 500+ users hit freemium threshold AND ≥ 40% willingness-to-pay survey before paywall enabled
- **Freemium thresholds ship configured-but-disabled** at MVP — activated only by Revenue Readiness trigger
- **MVP capability triage tiers:** P0 (ship-critical, cannot descope) and P1 (ships if schedule holds; descopes cleanly to Phase 2 under bandwidth pressure)

#### MVP Scope Boundaries (for traceability)

- **Bilingual:** AR + EN only (French scaffolding removed per R-09)
- **MVP Phase 1 supports Journeys 1–4** plus Journey 5 (exchange) — all 5 must work end-to-end
- **Hijri calendar:** read-only display at MVP; Gregorian primary (per NFR-A9 (f))
- **Statement-link expiration:** 30-day default at MVP (per NFR-S11); custom windows / per-recipient revocation deferred to Phase 2

### PRD Completeness Assessment (initial — pre-traceability)

| Dimension | Assessment |
| --- | --- |
| Functional coverage | **Strong.** 53 FRs cover all 5 journeys (incl. exchange Journey 5). No obvious capability gaps relative to the journey requirement summary. |
| Non-functional coverage | **Strong.** 6 NFR families with explicit quantitative targets and measurement criteria; performance budgets split owner-vs-public; cost ceilings phase-aware. |
| Measurability | **Very strong.** Every NFR has a concrete number, threshold, or named-scenario gate (e.g., NFR-A9 a–f, NFR-R6 daily integrity check). |
| Architecture alignment | **Likely strong but unverified.** PRD references ADR-021 and ADR-024 directly inside FR3, FR42, NFR-P1; cost ceilings explicitly cite architecture §7.4.1 and RT-47. Step 3 (Epic Coverage) and Step 4 (Architecture Alignment) will verify. |
| Implementation leakage | **Low.** Vendor-name leakage was scrubbed in the Apr 22 polish micro-pass (PrimeNG removal from NFR-P1; SignalR → "Real-time notification delivery"); FR3 and FR42 retain Auth0 references but linked to ADR-021 (acceptable given delivery mechanism is the contract, not implementation detail). |
| Open ambiguities | **None blocking.** P0/P1 triage explicit; freemium activation gated to Revenue Readiness; bilingual scope locked to AR+EN; Hijri scope locked to read-only. |

### Risks for Step 3+ (Epic Coverage)

- **FR42** mixes a hard system limit (20 links/hour) with implementation delegation (Auth0 Passwordless rate-limiting) — verify the story file separates the system-side cap from the Auth0 tenant config so neither implementation owns the contract alone.
- **FR52 nudges** have three trigger conditions (3 debts, 7-day stale, balance threshold) — story coverage must address all three or the viral-loop assumption underneath Phase A targets (≥40% activation, ≥0.3 viral coefficient) becomes hollow.
- **NFR-A9 ship-blocker** requires named human reviewers — story 1-1 (bilingual shell) or epic 1 close-out story should make the 6-scenario gate explicit, otherwise the gate exists in the PRD but not in any sprintable artifact.
- **NFR-I3 disable-state at 24h rate staleness** — needs a story that explicitly disables FR19 in the UI and surfaces the disabled-state messaging; epic 6 (exchange) is the natural home.

## Step 3 — Epic Coverage Validation

### Coverage Matrix

The epics document (`epics.md` line 330) provides a canonical **FR Coverage Map**. Cross-checked against the 53 FRs extracted in Step 2:

| FR | PRD Capability (short) | Epic | Story File(s) | Status |
| --- | --- | --- | --- | --- |
| FR1 | Register account | Epic 1 | `1-2-user-registration-auth0-hosted-flow.md` | ✓ Covered |
| FR2 | Authenticate | Epic 1 + 4 | `1-3-user-login-language-selection.md`, `4-15-public-spa-locale-toggle-disclaimer.md` | ✓ Covered (split: owner-app + public-SPA) |
| FR3 | Password reset (Auth0) | Epic 1 | `1-4-password-reset-auth0.md` | ✓ Covered |
| FR4 | Set preferred language | Epic 1 | `1-3-user-login-language-selection.md` | ✓ Covered |
| FR5 | View/edit profile | Epic 1 | `1-5-profile-view-and-edit.md` | ✓ Covered |
| FR6 | Delete account (GDPR) | Epic 1 | `1-7-account-deletion-gdpr-erasure.md` | ✓ Covered |
| FR7 | Legal disclaimer | Epic 1 + 4 | `1-6-legal-disclaimer-display.md`, `4-15-public-spa-locale-toggle-disclaimer.md` | ✓ Covered (split) |
| FR8 | Add contact | Epic 2 | `2-1-contacts-create-opening-balance.md` | ✓ Covered |
| FR9 | Edit contact | Epic 2 | `2-2-edit-existing-contact.md` | ✓ Covered |
| FR10 | Delete contact (guarded) | Epic 2 | `2-3-delete-contact-with-outstanding-balance-guard.md` | ✓ Covered |
| FR11 | List contacts w/ per-denomination summary | Epic 2 | `2-4-list-contacts-with-per-denomination-balance-summary.md` | ✓ Covered |
| FR12 | Search/filter contacts | Epic 2 | `2-5-search-and-filter-contacts-by-name.md` | ✓ Covered |
| FR13 | Record new debt | Epic 2 + 3 | `2-1-contacts-create-opening-balance.md` (first slice), `3-1-record-new-debt.md` (general flow) | ✓ Covered (intentional split) |
| FR14 | Edit debt (audit preserved) | Epic 3 | `3-2-edit-debt-with-audit-preservation.md` | ✓ Covered |
| FR15 | Delete debt (audit preserved) | Epic 3 | `3-3-delete-debt-with-audit-preservation.md` | ✓ Covered |
| FR16 | Per-contact history + date filter + previous-balance rollup | Epic 3 | `3-4-per-contact-history-with-date-range-filter-and-previous-balance-rollup.md` | ✓ Covered |
| FR17 | Denomination preservation | Epic 3 | `3-1-record-new-debt.md` (denomination preservation embedded), Epic 6 `6-6-denomination-integrity-enforcement.md` | ✓ Covered |
| FR18 | Hourly rate feed (gold base) | Epic 6 | `6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md` | ✓ Covered |
| FR19 | Exchange operation (locked rate + override) | Epic 6 | `6-4-exchange-operation-with-locked-rate-and-user-override.md` | ✓ Covered |
| FR20 | View historical exchange rate | Epic 6 | `6-5-view-historical-exchange-rate-on-past-operations.md` | ✓ Covered |
| FR21 | Denomination integrity enforcement | Epic 6 | `6-6-denomination-integrity-enforcement.md` | ✓ Covered |
| FR22 | Store historical rates | Epic 6 | `6-7-historical-rate-storage-for-audit.md` | ✓ Covered |
| FR23 | View current system rate pre-confirm | Epic 6 | `6-3-view-current-system-rate-pre-confirmation.md` | ✓ Covered |
| FR24 | Transfer debt (full or partial) | Epic 5 | `5-1-transfer-flow-ui-with-mandatory-review-dialog.md`, `5-2-atomic-compound-journal-entry-transfer.md`, `5-5-partial-transfer-support.md` | ✓ Covered |
| FR25 | Linked audit trail for transfer | Epic 5 | `5-2-atomic-compound-journal-entry-transfer.md`, `5-3-auditbadge-component-with-bidirectional-navigation.md` | ✓ Covered |
| FR26 | View transfer history | Epic 5 | `5-4-transfer-history-view.md` | ✓ Covered |
| FR27 | Dashboard overview (contacts × denominations) | Epic 3 | `3-5-full-glance-populated-with-direction-6-segmented-summary.md` | ✓ Covered |
| FR28 | Per-contact statement w/ history + balance | Epic 3 | `3-4-per-contact-history-with-date-range-filter-and-previous-balance-rollup.md` | ✓ Covered |
| FR29 | Generate statement report (HTML at MVP) | Epic 4 | `4-4-generate-statement-html-shareable-link.md` | ✓ Covered |
| FR30 | CSV export | Epic 3 | `3-9-csv-export-with-rtl-column-order-and-utf-8-bom.md` | ✓ Covered |
| FR31 | Dashboard overall position (no cross-currency aggregation) | Epic 3 | `3-5-full-glance-populated-with-direction-6-segmented-summary.md` | ✓ Covered |
| FR32 | Dashboard bar chart (across contacts) | Epic 3 | `3-6-dashboard-bar-chart-distribution-across-contacts.md` | ✓ Covered |
| FR33 | Dashboard pie chart (across denominations) | Epic 3 | `3-7-dashboard-pie-chart-distribution-across-denominations.md` | ✓ Covered |
| FR34 | Generate shareable link | Epic 4 | `4-4-generate-statement-html-shareable-link.md` | ✓ Covered |
| FR35 | Recipient views without account (public SPA) | Epic 4 | `4-5-public-statementview-renders-without-account.md` | ✓ Covered |
| FR36 | Recipient confirms (date recorded) | Epic 4 | `4-6-recipient-confirm-button-timestamps-agreement.md` | ✓ Covered |
| FR37 | Owner sees last-confirmation date | Epic 4 | `4-7-owner-sees-passive-last-confirmation-date-per-contact.md` | ✓ Covered |
| FR38 | Branding + signup CTA | Epic 4 | `4-8-signup-cta-designed-as-primary-ux-with-funnel-instrumentation.md` | ✓ Covered |
| FR39 | Disclaimer + watermark | Epic 4 | `4-15-public-spa-locale-toggle-disclaimer.md` | ✓ Covered |
| FR40 | Manage + revoke links | Epic 7 (deferred from E4) | `7-3-manage-and-revoke-statement-links.md` | ✓ Covered (rationale: <100 users in Phase A served by manual DB ops + kill switch) |
| FR41 | Cryptographic token generation | Epic 4 | `4-1-capability-token-scheme-with-constant-time-response.md`, `4-10-token-cryptographic-generation-with-property-tests.md` | ✓ Covered |
| FR42 | Rate-limit 20 links/user/hr | Epic 4 | `4-9-token-rate-limiting-20-links-hour-user-afd-edge-rule.md` | ✓ Covered (system cap separate from Auth0 tenant) |
| FR43 | Admin dashboard | Epic 8 | `8-1-admin-dashboard-with-platform-metrics.md` | ✓ Covered |
| FR44 | Admin freemium thresholds config | Epic 8 | `8-2-freemium-threshold-configuration-ui.md` | ✓ Covered |
| FR45 | Admin grandfathering rules | Epic 8 | `8-3-grandfathering-rules-management.md` | ✓ Covered |
| FR46 | Admin investigate + flag | Epic 8 | `8-4-admin-investigation-and-flagging.md` | ✓ Covered (consumes FR53 fraud-report queue) |
| FR47 | Admin suspend link generation | Epic 8 | `8-5-admin-suspend-statement-link-generation.md` | ✓ Covered |
| FR48 | Enforce freemium limits | Epic 8 | `8-6-freemium-limit-enforcement-ships-disabled.md` | ✓ Covered (ships disabled, activates via Revenue-readiness) |
| FR49 | Immutable audit log on debt ops | Epic 0 + 3 | `0-6-ibitemporal-interceptor-and-migrationservice.md` (storage), `3-8-user-visible-audit-trail-surface.md` (surface) | ✓ Covered (split: primitive + surface) |
| FR50 | Real-time notification on confirm | Epic 7 (deferred from E4) | `7-2-real-time-notification-on-counterparty-confirmation.md` | ✓ Covered (rationale: retention/trust signal, not viral) |
| FR51 | SignalR push w/ auto-reconnect | Epic 7 (deferred from E4) | `7-1-signalr-hub-infrastructure.md` | ✓ Covered (rationale: ~3-5 eng-day saving from E4 critical path) |
| FR52 | Viral-loop share nudges (3 triggers) | Epic 7 | `7-4-share-nudge-after-first-3-debts-per-contact.md`, `7-5-share-nudge-7-day-unshared-staleness.md`, `7-6-share-nudge-material-threshold-balance-crossing.md` | ✓ Covered (one story per trigger condition) |
| FR53 | Recipient fraud-report affordance | Epic 7 | `7-7-recipient-fraud-report-affordance.md` | ✓ Covered |

### Step-2 Risks — Verification

| Risk flagged in Step 2 | Story-level resolution | Status |
| --- | --- | --- |
| FR42 mixes system cap with Auth0 tenant config | Story 4-9 explicitly separates the per-user cap (server-enforced + AFD edge rule) from the Auth0 magic-link rate-limit | ✅ Resolved |
| FR52 nudge has 3 trigger conditions | Three independent stories: 7-4 (3-debts), 7-5 (7-day stale), 7-6 (material threshold) | ✅ Resolved |
| NFR-A9 ship-blocker needs a sprintable home | Story 0-12 (NFR-A9 RTL acceptance gate harness) lives in Epic 0 — runs as a CI gate, not just a manual review | ✅ Resolved |
| NFR-I3 24h disable-state | Story 6-8 (rate freshness SLA — banner and disable) explicitly covers the disable-state | ✅ Resolved |

### FR Spans (intentional cross-epic ownership)

These are not gaps — they're documented multi-epic ownership patterns that the epics document explicitly calls out:

- **FR2** — Epic 1 (owner-app locale toggle + `<html lang dir>`) + Epic 4 (public-SPA locale toggle, independent build target)
- **FR7** — Epic 1 (registration surface + owner-app disclaimer page) + Epic 4 (public-statement page footer disclaimer)
- **FR13** — Epic 2 (opening-balance via Contacts/Create §7.5.2 first slice) + Epic 3 (general record-debt flow)
- **FR49** — Epic 0 (append-only storage primitive + IBitemporal interceptor) + Epic 3 (user-visible audit trail surface)

### Missing FR Coverage

**Critical Missing FRs:** None.

**High Priority Missing FRs:** None.

**FRs in epics not in PRD:** None.

### Coverage Statistics

| Metric | Value |
| --- | --- |
| Total PRD FRs | **53** |
| FRs covered in epics | **53** |
| Coverage percentage | **100%** |
| Distinct story files mapping to ≥1 FR | ~52 (many P0 stories) |
| Foundation/scaffold stories (no user-visible FRs) | 17 (all in Epic 0) |
| Test-harness / NFR-only stories | 8 (e.g., 0-12, 0-13, 4-12, 4-13, 4-14, 6-9, 8-7, 8-11) |

### Reconciliation Notes

- **PRD FR list (53 items, FR1–FR53)** matches **epics.md `### Functional Requirements` list (53 items)** verbatim — no semantic drift between PRD and epics on FR text.
- **Epic descope decisions** (FR40, FR50, FR51 from E4 → E7) are documented with rationale in the FR Coverage Map and visible in the story file structure (those stories live under `7-*` not `4-*`).
- **Epic 0 has zero user-visible FRs by design** — its 17 stories are entirely scaffold/AR-coverage. This is a deliberate planning choice to prevent scaffold work from crowding out the shell epic and breaking the Day 35 dogfood target.

## Step 4 — UX Alignment

### UX Document Status

**Found.** [`ux-design-specification.md`](_bmad-output/planning-artifacts/ux-design-specification.md) (186,421 B, 3,208 lines) — comprehensive specification covering executive summary, design system foundation (PrimeNG v21 + Tailwind), six explored design directions (with HTML prototypes), the chosen Direction 6 ("Segmented Summary"), 4 user journey flows + 1 secondary, custom component strategy (Phase 1/2/3), 10 UX consistency patterns, responsive design, and accessibility/performance budgets.

### UX ↔ PRD Alignment

#### Journey Coverage

| PRD Journey | UX Flow | Status |
| --- | --- | --- |
| Journey 1 — Nadia first-time setup + daily use | Flow 1 — First-Session Activation; Flow 2 — Record a Debt | ✓ Aligned |
| Journey 2 — Karim debt transfer | Flow 4 — Debt Transfer Between Parties | ✓ Aligned |
| Journey 3 — Salma statement recipient | Flow 3 — Share Statement & Counterparty Confirmation | ✓ Aligned |
| Journey 4 — Admin operations | (Not in UX scope; admin surface intentionally not designed in MVP UX spec) | ⚠️ Gap (low priority — admin UX is acceptable to defer) |
| Journey 5 — Currency/metal exchange | Secondary Flow — Currency / Metal Exchange | ✓ Aligned |

#### FR Surface Alignment (sample, where UX explicitly covers FRs)

| FR | UX Section | Alignment |
| --- | --- | --- |
| FR4 (set preferred language) | Implementation Approach (Customization Strategy); RTL-Specific Accessibility (line 2811+) | ✓ |
| FR13 (record debt) | Flow 2 — Record a Debt | ✓ |
| FR16 (date-range filter + previous-balance rollup) | UX consistency patterns (Search & Filtering at 2513) | ✓ |
| FR17 (denomination preservation) | Glance Maturity Model — multi-denomination row treatment | ✓ |
| FR24/25/26 (debt transfer) | Flow 4 + AuditBadge component pattern | ✓ |
| FR27 (dashboard overview) | Direction 6 Chosen Design — segmented receivable/payable hero | ✓ |
| FR31 (no cross-currency aggregation) | Direction 6 explicit per-denomination column rendering | ✓ |
| FR34/35/36 (statement share + recipient confirm) | Flow 3 — Share Statement & Counterparty Confirmation | ✓ |
| FR50 (real-time confirmation notification) | Feedback Patterns (UX consistency at 2196) — toast on counterparty confirm | ✓ |
| **FR32 (dashboard bar chart across contacts)** | **Direction 6 "Out of scope" line 974: pie/donut charts violate "no decoration" rule** | ❌ **CONFLICT** |
| **FR33 (dashboard pie chart across denominations)** | Same as FR32 + line 526 defers PrimeNG `Chart` until "clear analytics need emerges" | ❌ **CONFLICT** |

#### Critical Misalignment 1 — Dashboard Charts (HIGH) ✅ RESOLVED 2026-04-26

**Resolution chosen:** Option 3 — chart support retained for FR32/FR33 with a "Quiet chart envelope" UX restraint specification.

**UX edits applied:**

- `ux-design-specification.md:526-527` — `Chart` removed from "explicitly avoided" components; replaced with scoped-permission language directing implementers to the Quiet chart envelope.
- `ux-design-specification.md:972-1024` — "Out of scope for this direction" amended (Glance hero stays chart-free; charts ship as a separate dashboard surface). New subsection **"Quiet chart envelope (FR32 / FR33)"** added, specifying: semantic-token color only, zero animation, plain tooltips, text-only legend, suppress empty charts when data is sparse, RTL parity, read-only at MVP, lazy-imported to protect the 250 KB owner bundle.

**Effect:** stories 3-6 and 3-7 can implement FR32/FR33 with a single source of truth for visual restraint. PRD requirement preserved; "quiet financial tool" pillar preserved; CI/perf budget protected.

**Original conflict (preserved for audit trail):**

- UX `ux-design-specification.md:974-975` (pre-amendment) stated pie/donut charts were out of scope for Direction 6. Line 526 (pre-amendment) deferred PrimeNG `Chart` until "clear analytics need emerges."
- PRD FR32 / FR33 require bar + pie distribution charts. Stories 3-6 and 3-7 implement them.

#### Critical Misalignment 2 — Owner-App Bundle Budget (MEDIUM) ✅ RESOLVED 2026-04-26

**Status:** Fixed in `ux-design-specification.md:3180-3181`. The single ambiguous "Initial JS bundle" row was split into two explicit rows: public-statement (< 180 KB gz, matches NFR-P1 public + ADR-024) and owner auth-shell (< 250 KB gz, matches NFR-P1 owner). UX performance budgets now reconcile with PRD/architecture.

**Original conflict (preserved for audit trail):** UX `ux-design-specification.md:3180` lists "Initial JS bundle < 180KB gzipped" for "Main entry" — and Direction 6 (the Glance) is the owner-app's primary surface, suggesting this 180KB budget applies to the owner shell.

**But:** PRD NFR-P1 (owner) sets the owner auth-shell budget at **< 250 KB gzipped**, and architecture (per ADR-024 cascade) confirms the same number. Story 1-11 ("Performance budget + visual regression CI gates") and the "180KB gz bundle budget with SSR fallback documented" story 4-3 (which targets the public-statement bundle, not owner) need clear targets.

**Impact:** Three plausible interpretations:

- (a) UX intentionally tightened the owner-app target to 180KB (acceptable; just stricter than PRD/architecture allow). Implementation team should know which number CI enforces.
- (b) UX line 3180 was meant to refer to the public-statement bundle (which is genuinely 180KB) and didn't enumerate the owner shell separately. Easy fix.
- (c) UX missed the owner/public split that landed in PRD and architecture during the adversarial-review reconciliation pass. Genuine drift.

**Recommended resolution before Phase 4:** Update UX `### Performance Budgets (UX-Correlated)` table to explicitly list both budgets (public-statement: 180 KB; owner auth-shell: 250 KB), matching PRD NFR-P1 (public/owner) and architecture §6.5.4.

#### Minor Note — Landing-Page FCP

PRD specifies FCP < 1.5s for landing pages. UX's performance table specifies FCP < 1.8s on Slow 4G for the Glance only — landing pages are not in UX scope. This is **not a misalignment** (UX scope is owner-app + statement view; landing pages are a separate marketing surface).

### UX ↔ Architecture Alignment

| Architecture Decision | UX Reflection | Status |
| --- | --- | --- |
| ADR-024 — Minimal APIs + source-gen OpenAPI | UX is framework-agnostic on backend; doesn't conflict | ✓ |
| Two-target Angular workspace (owner + public-statement) | UX Direction 6 = owner-app primary; public-statement is separately scoped (Flow 3) | ✓ |
| Zoneless Angular | UX `ux-design-specification.md:3192` explicitly requires zoneless Angular | ✓ Strong alignment |
| Signals + OnPush as default state pattern | UX line 3192: "Signals + OnPush + explicit `markForCheck()`" | ✓ |
| ADR-021 Auth0 + custom domain `auth.faktuboh.com` | UX doesn't surface auth UX explicitly (Auth0 hosted); accepted | ✓ |
| 180 KB gz public bundle budget | UX line 3180 (180KB Main entry) | ✓ matches the public bundle number, but ambiguous about owner-app — see Misalignment 2 above |
| RTL pipeline + WCAG 2.1 AA | UX has 94 references to WCAG/RTL/bidi/contrast/etc. — strongest single area of UX↔architecture alignment | ✓ Excellent |
| NFR-A9 RTL acceptance gate (6 named scenarios) | UX RTL-Specific Accessibility section (line 2811+); supports the 6 scenarios via mixed-bidi, numeral, layout, CSV, and Hijri-display patterns | ✓ |
| Capability-token scheme (FR41 + ADR area) | UX Flow 3 abstracts the link as user-facing; no token specifics in UX (correct boundary) | ✓ |

### Warnings

1. ~~**(HIGH)** **Charts misalignment**~~ ✅ **RESOLVED 2026-04-26** — option 3 chosen: charts retained with "Quiet chart envelope" restraint spec added to UX. Stories 3-6/3-7 implementable as-is against the new envelope.
2. ~~**(MEDIUM)** **Owner-app bundle budget ambiguity**~~ ✅ **RESOLVED 2026-04-26** — UX performance table now lists both budgets explicitly (public-statement 180 KB / owner auth-shell 250 KB).
3. **(LOW)** **Admin journey not in UX scope** — Journey 4 (Admin) has no UX flow. Acceptable for MVP given admin is a low-traffic internal surface and Epic 8 stories include functional admin UI without a bespoke UX language. Confirm before Phase 4 that Epic 8's stories carry their own UI-specification language sufficient for implementation.

### Alignment Summary

| Dimension | Verdict |
| --- | --- |
| Journey coverage | Strong (4 of 5 journeys + 1 secondary; admin acceptable to defer) |
| FR-to-UX surface coverage | Strong except for FR32/FR33 conflict |
| Performance budgets | Numerically correct on public-statement; ambiguous on owner-shell — minor doc fix |
| Accessibility / RTL / bilingual | Excellent — UX is in advance of PRD on accessibility detail |
| Architecture compatibility | Strong (zoneless, signals, two-target workspace, Auth0, design system all reflected) |

## Step 5 — Epic Quality Review

### Epic Structure Validation

#### Epic-by-Epic User Value Check

| # | Epic Title | User-Centric Goal? | User-Visible? | Verdict |
| --- | --- | --- | --- | --- |
| 0 | Platform Foundation | ❌ "Stand up greenfield scaffold" | No | 🟠 **Technical milestone epic — BMad rule deviation (documented)** |
| 1 | First-Run Shell & Identity | ✓ "User can register, authenticate, set language, manage profile, see disclaimer, delete account" | Yes | ✓ |
| 2 | Contacts — Know Your Counterparties (First Slice) | ✓ "User can add/edit/delete/list/search contacts with opening balance" | Yes | ✓ |
| 3 | Record Debts, See the Glance & Export | ✓ "User can record/edit/delete debts, see Glance, export CSV, view audit trail" | Yes | ✓ |
| 4 | Viral Loop MVP — Statement, Share & Confirm | ✓ "User can generate share link, recipient confirms, signup CTA" | Yes | ✓ Strong (the product hypothesis) |
| 5 | Debt Transfer Between Parties | ✓ "User can transfer debts between contacts" | Yes | ✓ |
| 6 | Currency & Metal Exchange with Locked Rates | ✓ "User can exchange between denominations with locked rate" | Yes | ✓ |
| 7 | Real-Time + Nudges + Fraud + Link Mgmt | ✓ "Real-time confirmations, share nudges, fraud reports, link management" | Yes | ✓ |
| 8 | Platform Operations, Admin & Operational Safety Jobs | ⚠ Operator-facing (admin) | Operator-facing | 🟡 Acceptable (admin is internal-user value) |

#### Epic Independence Validation

Dependencies declared in Epic Summary (epics.md:578–589):

```text
E0 (foundation, AR-only) → E1 → E2 → E3 → E4 → E7 → E8
                                   ↘  E5, E6 (both depend on E3 + E0/AR-049)
```

| Test | Result |
| --- | --- |
| Critical path forms a clean chain (E0→E1→E2→E3→E4) | ✓ |
| No epic requires a higher-numbered epic to function | ✓ |
| E5↔E6 hard dependency mitigated | ✓ AR-049 (Compound-Journal-Entry interface seam) defined in Epic 0 lets E6 ship without E5 |
| Forward references in story bodies | None detected in sampled stories (0.1–0.5, 1.1–1.5, 4.5–4.9) |
| Cycles | None |
| Sub-epic spans (FR2, FR7, FR13, FR49) | Documented as intentional spans; each surface in each epic is independently completable |

### Story Quality Assessment (sampled)

I sampled 14 stories across Epics 0, 1, and 4 and reviewed all 91 story-spec filenames. Findings below.

#### Story Format Compliance

| Format Check | Result |
| --- | --- |
| All sampled stories use "As X, I want Y, So that Z" format | ✓ |
| All sampled ACs use **Given/When/Then** BDD structure | ✓ |
| All sampled ACs are testable (specific HTTP codes, fixed numbers, named events, named ESLint rules) | ✓ |
| Cross-references to PRD FRs explicit in story headers | ✓ (e.g., "(FR1)", "(FR35, FR39)", "(FR42, NFR-S5, AR-017)") |
| Cross-references to architecture decisions/ADRs | ✓ (e.g., "per ADR-024", "per AR-047", "per UX-DR11") |
| Error-path ACs included | ✓ (e.g., 0.4 ErrorCatalog enforcement; 0.5 conflict; 4.9 21st-link rate-limit; 1.2 duplicate-email) |
| Telemetry/observability ACs included | ✓ (e.g., AR-047 events: `user_signup`, `statement_viewed`, `statement_confirmed`, `cta_clicked`, `viewer_signup_from_share`) |
| Idempotency considered for mutations | ✓ (e.g., Story 4.6 explicit; Story 1.5 references the idempotency-key middleware from 0.5) |

#### AC Density & Sizing

- Stories range from 4 ACs (Story 4.7) to 9 ACs (Story 4.5). Median ~6.
- AC density is **on the upper edge** for solo-dev sprint sizing but appropriate for safety-critical flows (auth, statements, confirmations).
- No "epic-sized stories" detected in samples. Each story is scoped to a single user-visible behavior or a single foundation primitive.

#### Database/Entity Creation Timing

- Story 0.5 creates `idempotency_keys` table when first needed (foundation).
- Story 0.6 (IBitemporal interceptor + MigrationService) provides the migration framework.
- Story 2.1 (Contacts/Create with opening balance) creates the contacts schema.
- Story 3.1 (Record new debt) creates debt schema.
- **Pattern: tables created when first slice that needs them lands.** ✓ (Right per Step 5).

#### Greenfield Special Checks

| Check | Result |
| --- | --- |
| Initial project setup story exists | ✓ Story 0.1 (Backend solution scaffold) + Story 0.2 (Angular two-target workspace) |
| Development environment configured | ✓ Story 0.1 wires Aspire AppHost; Story 0.2 wires Angular workspace; Story 0.6 wires migrations |
| CI/CD pipeline early | ✓ Story 0.9 (GitHub Actions CI/CD with OIDC) |
| Architecture-specified infrastructure provisioning | ✓ Story 0.10 (Azure infrastructure provisioned) |

### Findings

#### 🔴 Critical Violations

**None.**

#### 🟠 Major Issues

**M-1. Epic 0 is a technical-milestone epic (BMad rule deviation, documented).**

- **What:** Epic 0 contains 17 stories, all scaffold/AR-coverage, with no user-visible FRs. Per Step 5's strict rules, "Infrastructure Setup" / "Setup Database" / "API Development" framings are technical-milestone epics that should be folded into user-value epics.
- **Documented justification:** The epics document at line 597 records the team's deliberate rationale: *"Epic 0 split from Epic 1 (Amelia) — scaffold stories as a distinct epic with no user FRs prevents the '3-week slog with no user-visible progress' failure mode."* If the 17 scaffold stories were folded into Epic 1, Epic 1 would balloon to 28 stories and the Day 35 dogfood target would be at high risk.
- **Severity:** Major (per BMad rule strictness) but **not Critical** because the alternative (folding into Epic 1) would create a different and arguably worse problem.
- **Recommendation:** Acknowledge as a documented exception. The alternative (folding) violates Step 5's other guidance ("appropriately sized stories" and "stories independently completable in single sprint"). Solo-dev MVP greenfield is a valid context for separating foundation from feature epics.

#### 🟡 Minor Concerns

**m-1.** Epic 8 is operator-facing (admin), which the Epic Summary explicitly labels "Operator-facing" rather than user-facing. By BMad's strict definition of user-value, admin features sit at the edge. Treating operators as internal users (industry-standard interpretation) keeps this acceptable.

**m-2.** Story 4.5 (Public StatementView) has 9 ACs spanning rendering, performance, locale, headers, history-stripping, bot-blocking, and telemetry. On the heavier end of solo-dev sprint sizing. Could potentially be split into "renders without account" + "edge-hardening for shareable surface" if implementation reveals stretch.

**m-3.** Some stories reference UX-DR codes (UX-DR11, UX-DR12, UX-DR13, UX-DR28, UX-DR32) that I haven't traced into the UX spec yet — visual inspection suggests they are sectional codes within `ux-design-specification.md` but a single-page index of all UX-DR codes would help future story authors.

### Strengths Worth Preserving Through Phase 4

1. **Documented structural decisions with reviewer attribution** — the 19 "party-mode round" decisions in the Epic Summary (epics.md:595–619) anchor the planning rationale. Future course corrections (e.g., responding to Day 30 RT-44 descope trigger) should preserve this attribution discipline.
2. **AR-049 dependency-decoupling pattern** — defining the Compound-Journal-Entry interface seam in Epic 0 to allow Epic 6 to ship without Epic 5 is a sophisticated dependency hygiene move. Worth replicating if any other E5↔E6-style coupling emerges.
3. **Pre-committed Day 30 scope-cut PR (RT-44)** — leading-indicator descope with mechanically-executable fallback. Solo-dev operational discipline rare at this scale. Phase 4 sprint-status workflow should keep this trigger live.
4. **Mutation testing on Domain layer (Stryker.NET, AR-048)** — solo-dev correctness substitute for pair-review. Verify Phase 4 sprint planning includes the weekly Stryker.NET cycle as part of the test cadence, not as a one-off.
5. **Telemetry primitive in Epic 0 (AR-047) with per-epic emission ACs** — every FR-delivering epic carries an AC to emit its corresponding Phase-A metric event. This wires viral-coefficient + activation measurement directly into stories instead of leaving it as a "we'll add analytics later" debt.

### Best-Practices Compliance Summary

| Standard | Verdict |
| --- | --- |
| Epic delivers user value | ✓ for E1–E7 / 🟠 documented exception for E0 / ✓ operator-value for E8 |
| Epic can function independently of higher-numbered epics | ✓ |
| Stories appropriately sized | ✓ (with minor caveat on a few 8–9 AC stories) |
| No forward dependencies | ✓ |
| Database tables created when needed | ✓ |
| Clear acceptance criteria (BDD, testable) | ✓ Excellent |
| Traceability to FRs maintained | ✓ Explicit FR / NFR / ADR / AR / UX-DR cross-refs |
| Greenfield setup pattern present | ✓ Stories 0.1, 0.2, 0.9, 0.10 |

## Summary and Recommendations

### Overall Readiness Status

**🟢 READY for Phase 4** *(updated 2026-04-26 after both misalignments resolved; original verdict preserved below for audit trail).*

Both material misalignments identified during the assessment are resolved:

- **HIGH (Charts FR32/FR33):** Resolved — option 3 chosen. UX amended with a "Quiet chart envelope" restraint spec at `ux-design-specification.md:972-1024`. Stories 3-6/3-7 ready to implement against the envelope.
- **MEDIUM (Owner-app bundle budget):** Resolved — UX performance table now lists both budgets explicitly at `ux-design-specification.md:3180-3181`.

**Recommended next step:** run `bmad-sprint-planning` to produce the sprint status that the implementation agents will follow story-by-story.

---

#### Original verdict (audit trail)

**🟡 NEEDS MINOR WORK — Phase 4 ready after ~2–4 hours of bounded doc fixes.**

The artifacts are at a remarkably high maturity level. Coverage is **100% (53/53 FRs)**. Story quality across sampled stories is uniformly strong (BDD ACs, explicit cross-references to PRD/architecture/ADRs/UX-DRs/AR-numbers, error-path coverage, telemetry events embedded). The PRD has been through five polish passes including pre-mortem and 5-Whys deep dives. The architecture has been adversarially reviewed and merged the resolution into the canonical `architecture.md`.

**Two material misalignments** were identified — both are bounded doc-level fixes, neither requires re-planning architecture or epics. Phase 4 should proceed once these are resolved.

### Critical Issues Requiring Action Before Phase 4

#### 🔴 HIGH — Charts UX↔PRD Conflict (Step 4 Misalignment 1)

UX `ux-design-specification.md:974-975` excludes pie/donut charts from chosen Direction 6 ("violate the 'no decoration' rule"); line 526 defers PrimeNG `Chart` until "clear analytics need emerges". PRD FR32/FR33 require both bar and pie charts; stories 3-6 and 3-7 are scoped to deliver them.

**Decision needed (pick exactly one):**

1. **Reconcile UX:** Update Direction 6 with a chart treatment that respects "no decoration" (e.g., minimalist sparklines, text-percentage bars). Update UX spec section "Out of scope for this direction".
2. **Reconcile PRD:** Demote FR32/FR33 from MVP P0 to Phase 2. Revise stories 3-6/3-7. Update epics.md FR Coverage Map.
3. **Document an exception:** Add a UX addendum that scopes FR32/FR33 with explicit visual restraint guidance (which color tokens, no animation, no decorative styling).

**Recommended decision-maker:** Wahid (product owner). My personal lean is **option 3** — it preserves the PRD requirement, respects the UX "quiet financial tool" pillar, and gives implementation a concrete restraint envelope. Option 2 weakens the data-density story for power users (Karim journey). Option 1 risks UX drift.

#### 🟠 MEDIUM — Owner-App Bundle Budget Ambiguity (Step 4 Misalignment 2) ✅ RESOLVED 2026-04-26

UX `ux-design-specification.md:3180-3181` now lists both bundle budgets explicitly:

- Public-statement: < 180 KB gz (matches NFR-P1 public + ADR-024)
- Owner auth-shell: < 250 KB gz (matches NFR-P1 owner)

CI gates and Story 1.11 (performance budget gates) can now target unambiguous numbers per build target.

### Issues Acknowledged But Not Blocking

#### 🟠 Epic 0 as Technical-Milestone Epic

Per strict BMad rules, Epic 0 (17 scaffold stories, no user-visible FRs) violates the "Infrastructure Setup is not a user-value epic" red flag. The team's documented justification (`epics.md:597`) is sound — folding 17 stories into Epic 1 would make Epic 1 oversized and would lose the Day 35 dogfood target. **Proceed with Epic 0 as a documented exception.** No action required.

#### 🟡 Minor Concerns (Step 5)

- Epic 8 operator-facing — accepted as internal-user value
- A few 8–9 AC stories on the heavier side (notably 4.5) — proceed; split if implementation reveals stretch
- UX-DR codes scattered across UX spec without a central index — nice-to-have

### Recommended Next Steps

In order:

1. ~~**(Required, ~30 min)** Decide on the charts question.~~ ✅ **DONE 2026-04-26** — option 3 chosen; UX amended with Quiet chart envelope at `ux-design-specification.md:972-1024` and component permission updated at `ux-design-specification.md:526-527`.
2. ~~**(Required, ~10 min)** Fix the UX performance budget table to disambiguate owner vs public bundle targets.~~ ✅ **DONE 2026-04-26** — `ux-design-specification.md:3180-3181` now lists both budgets explicitly.
3. **(Optional, ~15 min)** Add a one-page index of UX-DR codes to the top of `ux-design-specification.md` so story authors can resolve cross-references without grepping.
4. **(Optional, ~30 min)** Walk through the 19 party-mode decisions and confirm none have aged badly since 2026-04-22. Especially: the Epic 4 SignalR-defer (decision #4) and the Day-30 RT-44 trigger (decision #19) — both depend on assumptions about the viral-loop hypothesis that should be re-checked at the eve of Phase 4.
5. **(NEXT — required to enter Phase 4)** Run `bmad-sprint-planning` to produce the sprint status that the implementation agents will follow story-by-story. This is the next required step per the Phase 4 dependency chain documented in epics.md.

### Final Note

This assessment identified **2 material issues** (1 HIGH, 1 MEDIUM) and **3 minor concerns** across 6 categories (document discovery, PRD analysis, epic coverage, UX alignment, epic quality, final synthesis). The two material issues are bounded doc-level fixes totaling ~30–40 minutes of focused doc work. After resolution, Phase 4 readiness is unconditional.

The unusually high quality of the planning artifacts means Phase 4 implementation should proceed with low risk and high implementation velocity. The 91 story specs are sprintable as-is; the FR coverage is verified; the architecture has been adversarially reviewed; the schedule has a pre-committed descope mechanism.

**Address the two HIGH/MEDIUM items, then run `bmad-sprint-planning` to start Phase 4.**

---

*Implementation Readiness Assessment Complete · 2026-04-26 · per `bmad-check-implementation-readiness` skill*
