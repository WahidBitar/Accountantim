---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-04-21'
inputDocuments:
  - prd.md
  - product-brief-accountantim.md
  - product-brief-accountantim-distillate.md
  - prd-validation-report.md
  - ux-design-specification.md
referenceDocuments:
  # Provided inline by user — sourced from another project (DiTracker).
  # Treated as "best-ideas pool" to adapt, not contract to inherit.
  - source: DiTracker
    kind: architecture-overview
    name: architecture.md
  - source: DiTracker
    kind: api-conventions
    name: api-conventions.md
  - source: DiTracker
    kind: frontend-conventions
    name: frontend-conventions.md
  - source: DiTracker
    kind: local-development
    name: local-development.md
  - source: DiTracker
    kind: adr
    name: 0001-vertical-slice-fastendpoints.md
  - source: DiTracker
    kind: adr
    name: 0002-ddd-domain-core.md
  - source: DiTracker
    kind: adr
    name: 0003-angular-standalone-signals.md
  - source: DiTracker
    kind: adr
    name: 0004-backend-technology-stack.md
  - source: DiTracker
    kind: adr
    name: 0005-frontend-architecture.md
workflowType: 'architecture'
project_name: 'Accountantim'
user_name: 'Wahid'
date: '2026-04-18'
classification:
  projectType: web_app
  domain: fintech
  complexity: high
  projectContext: greenfield
framingDecisions:
  referenceStackAuthority: "advisory_only"   # DiTracker docs are a best-ideas pool, not a default
  team: "dedicated_team_tbd"                 # Own team, not necessarily DiTracker's
  repoStrategy: "standalone"                 # New repo, no ties to DiTracker codebase
  databaseChoice: "open"                     # Azure SQL or PostgreSQL both on the table
---

# Architecture Decision Document - Accountantim

**Author:** Wahid
**Date:** 2026-04-18

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

---

## Step 2 — Project Context Analysis

### 2.1 Requirements Overview

The PRD defines **51 functional requirements across 10 categories** and **30 NFRs across 6 categories**. Grouped by architectural subsystem:

- **Identity & ownership:** authenticated owner workspace; public statement surface (unauthenticated, link-based). _Forward-roadmap: debtors will receive accounts in a future version — identity and data-ownership models must be **two-sided-ready from day one**._
- **Ledger core:** multi-denomination debts preserved in original unit (no aggregation); cross-currency only via explicit locked-rate exchange; immutable append-only audit with 7-year retention.
- **Denomination & rates:** multi-currency + precious metals; hourly external rate feed with **gold as base unit**; rates locked at exchange time.
- **Confirmation flow:** shareable public statement link; debtor confirmation updates a reconciliation marker without touching ledger immutability.
- **Freemium:** 500 tx / 10 contacts. _Open question — 10-contact cap conflicts with MENA ROSCA/jam3iyya patterns; flagged for product reconciliation._
- **Data lifecycle:** GDPR-compliant irreversible PII anonymization that preserves audit math.
- **Export & portability:** CSV export is a core (non-paywall) capability.

Two distinct **write paths**:
1. Authenticated SPA — owner workspace mutations (high-trust, rich interactions).
2. Unauthenticated public statement surface — debtor confirmation (low-trust, hostile input expected).

### 2.2 Critical NFRs Beyond Fintech Hygiene

- **"The Glance" engineering contract:** TTR <2s perceived, TTI <500ms broadband / <1.5s 3G, CLS <0.05, **skeleton-until-authoritative** (never render a number we may retract), 100% balance correctness.
- **Performance budgets (stricter than PRD):** initial JS <180KB gz, route-lazy <60KB, LCP <2.5s Slow 4G, INP <200ms, FCP <1.8s, TTFB <800ms.
- **Accessibility:** WCAG 2.1 AA baseline + **AAA contrast on financial surfaces**.
- **Public statement scale (re-framed):** sustained **80 rps / burst 500 rps for 30s / p95 <300ms / cache hit-rate >95%** (original NFR-SC4 "5,000 req/min" was ambiguous; WhatsApp-driven traffic is spiky within seconds, not minutes).
- **Locale pipeline:** bilingual day-one (AR-RTL + EN-LTR) is only a subset — the broader concern is a **locale-aware data pipeline** (script direction, numeral system, calendar, collation, currency-symbol anchoring, bidi isolation).
- **Bitemporal semantics:** valid-time + transaction-time distinction is load-bearing for audit, rate feed, and long-lived statement links — every query needs explicit "as-of" semantics.
- **Zoneless Angular:** hard UX constraint on change detection — affects state library choice and SSR compatibility.

### 2.3 Scale & Complexity Verdict — **HIGH**

Drivers: immutable financial math under multi-denomination arithmetic; two isolated serving paths with different trust and scale profiles; bilingual/RTL data pipeline; immutable audit + GDPR erasure reconciliation; strict performance budgets; external rate-feed integration; forward-compatible two-sided identity.

### 2.4 Primary Technical Domain

Full-stack web:
- Angular 21 zoneless SPA (owner workspace)
- HTTP API (owner mutations, admin)
- Separate public statement surface (optimized for cache + cold-start)
- Real-time push (owner-side balance updates)
- Scheduled background jobs (hourly rate ingestion, reconciliation sweeps, retention)
- External rate-feed integration
- Bilingual/locale-aware presentation pipeline

### 2.5 Hard-Constrained vs Open-for-Decision

**Hard-constrained (non-negotiable):**
- Angular 21 zoneless, PrimeNG v21 Aura + "Accountantim Quiet" preset, Tailwind layout-only
- Tajawal + Inter self-hosted fonts
- @primeng/mcp tooling
- Azure Europe hosting region
- Bilingual AR+EN day one
- Debts preserved in original denomination; gold as base unit for rates
- Immutable append-only audit, 7-year retention
- GDPR-preserving anonymization (audit math must still reconcile)

**Open architectural decisions (17+):**
backend runtime (.NET / Node / other) · database (Azure SQL vs PostgreSQL) · real-time transport (SignalR vs alternatives) · hosting topology (App Service / Container Apps / AKS) · background jobs · identity/auth provider · DTO mapping approach · audit storage (same DB vs separate) · rate-feed provider selection · public statement rendering strategy (CDN-prerendered vs edge-SSR) · CSV export mode · i18n library · observability stack · repo layout · **decimal/precision representation per denomination** · **bitemporal storage strategy** · **freemium contact-cap review (product gap)**.

### 2.6 Cross-Cutting Concerns

1. **Bitemporal time model** — every ledger write has a valid-time and transaction-time; statement links can be re-opened years later.
2. **Locale-as-pipeline** — not a setting; data, rendering, collation, numerals, and calendar flow together.
3. **Decimal / unit algebra** — typed Money/Weight domain primitives, not float; gold-base conversion at declared scale per denomination.
4. **Idempotency + outbox** — rate-feed ingestion, webhooks, audit append require exactly-once semantics.
5. **Two-path isolation** — owner SPA and public statement must not share auth surface, cache key space, or failure domain.
6. **Audit integrity under anonymization** — PII separated from ledger math at the schema level.
7. **Cache coherence on public statements** — pre-rendered, signed, short-TTL, revocable.
8. **Observability across locales** — logs and metrics tagged with locale + script + calendar.
9. **Two-sided identity evolution** — MVP owner-only, but data model must accommodate debtor accounts later without a migration blast.
10. **Performance budgets as CI gates** — bundle, LCP, INP, CLS enforced per PR.
11. **Testability of financial invariants** — balance correctness must be provable, not asserted.
12. **Competitive framing** — primary competitor is not an app but a **WhatsApp screenshot of a handwritten note**; The Glance exists to beat that in under 3 seconds.
13. **Public-link token lifecycle** — issuance entropy, storage, revocation, two-layer rate limiting, and constant-time response surface are a single cross-cutting concern, not per-feature plumbing.
14. **Quasi-identifier policy** — at freemium scale, (amount + currency + timestamp) is often uniquely identifying. Any audit, analytics, or export path must respect a minimum-k policy or refuse the query.
15. **Dual-path math surface** — live / backfill / reconciliation / export all pass through a single canonical `DomainMath` module, enforced at build time (dependency rule: no module computes balances except via `DomainMath`). Prevents "two code paths, one correct" drift.
16. **Locale as a domain type** — `LocaleFormattedAmount`, `LocaleDate`, `LocaleCollation` emitted from the data-access tier; presentation tier cannot render raw numerics or dates. Enforced at build time, not convention.

### 2.7 Primary Architectural Risks

Ranking reflects composite review from Party Mode and Self-Consistency Validation across three frames (blast radius, irreversibility, trust damage). Value judgments underpinning the ranking are declared explicitly in §2.11.

| # | Risk | Why it matters |
|---|---|---|
| 1 | **Arithmetic correctness surface** (compound) | Two required facets: **(a) correctness at the primitive level** — typed Money/Weight domain primitives, gold-base unit algebra, declared per-denomination scale, property-based tests, mutation testing ≥90% on the canonical math module; **(b) architectural enforcement** — a single canonical `DomainMath` module with a build-time dependency rule preventing any other code path (live / backfill / reconciliation / export / CSV) from computing balances. Both facets must hold; either alone is insufficient. |
| 2 | **Public statement serving path** | Hostile, unauthenticated, spike-prone (80 rps sustained / 500 rps burst for 30s / p95 <300ms / cache-hit >95%). Two-path isolation from owner SPA at WAF, cache, and origin layers. |
| 3 | **GDPR erasure vs audit integrity** | Requires a declared cryptographic posture: key-shred **pseudonymization** vs tombstone **anonymization** (currently conflated). Schema-level surrogate-ID pattern plus outbox-driven erasure propagation to search, cache, analytics, logs. Re-identification via quasi-identifiers is a second-order attack at freemium scale and requires a minimum-k policy. |
| 4 | **The Glance engineering contract** | 180KB initial JS, LCP <2.5s Slow 4G, INP <200ms, CLS <0.05, skeleton-until-authoritative, 100% balance correctness. Budget + zoneless + never-retract-a-number is a three-way tight constraint. |
| 5 | **Audit immutability enforcement** | Irreversible under frame weighting — tampering kills audit. Must be provable via mutation testing across every write path including migrations, not just documented. Invisible failure mode; ranking reflects the visibility-weighting value judgment in §2.11, not low severity. |
| 6 | **Concurrency & race on ledger writes** | Idempotency keys on statement confirmation + optimistic locking or serializable reads on ledger writes. Test with a Jepsen-lite harness. |
| 7 | **Locale-as-pipeline** | Broader than bilingual: numerals, bidi, calendar, collation, currency-symbol anchoring. Mechanical to stress-test (AR+EN visual regression per PR). Pipeline discipline problem, not a showstopper. |
| 8 | **Rate-feed staleness, replay, and divergence** | Locked rates must be signed and bound to (user, debt, timestamp). Hour-boundary behavior, NaN payloads, duplicate timestamps, 2× spike — all need explicit test cases. |

### 2.8 Product Context Note

The primary competitor is not Splitwise, Settle Up, or Tricount. It is a **WhatsApp screenshot of a handwritten note** plus social trust. Any architectural trade-off that degrades first-load experience is an invitation to abandon the app and return to that baseline. This sets the sign on decisions like SSR vs CSR for public statement, inlined vs lazy-loaded amount formatting, and cold-start budget.

**Freemium caps (transactions, contacts) are runtime configuration, not schema or hardcoded constants.** This permits Product to retune caps without migrations as MENA-specific usage patterns (jam3iyya, extended-family ledgers) become measurable. Onboarding telemetry tracks "contacts added → abandonment at cap" as a leading retention signal.

### 2.9 Adversarial Hardening Baseline

Non-negotiable defenses that shape every Step 3+ decision.

**Public surface hardening:**
- 128-bit opaque public-statement tokens with server-side state and first-class revocation
- Constant-time response for invalid / expired / unknown tokens (no existence oracle)
- Two-layer rate limiting (CDN edge per-IP + origin per-token + per-IP-hash)
- Idempotent, nonce-bound confirmation with CSRF double-submit and `frame-ancestors 'none'`
- Server-controlled HTML template only; markdown subset for creditor notes, raw HTML denied; sanitize at write AND at render
- Token stripped from browser history via `history.replaceState` after first navigation
- Separate WAF ruleset per serving path (owner SPA vs public statement)
- Zero PII in URLs — token is the only dynamic segment; no debtor name, no currency, no amount in slug or query params
- `noindex` + `X-Robots-Tag: noindex, nofollow` + `Cache-Control: private` headers on statement HTML
- First-render interstitial ("Tap to view statement") — blocks preview bots from triggering body render and token consumption
- Separate telemetry streams for bot-triggered vs debtor-triggered token opens; alert owner if a link has been opened from diverging IPs / user-agents

**Data-lifecycle hardening:**
- Declared cryptographic erasure model (pseudonymization vs anonymization) pinned before Step 3 closes
- Outbox-driven erasure propagation to search, cache, analytics, logs
- Serilog / telemetry PII denylist, enforced in CI
- Backup restore playbook replays the anonymization log as step zero
- Cache carries an anonymization-version tag; version bump invalidates
- Minimum-k policy on aggregate queries; refuse queries that would produce unique rows at freemium cardinality
- Legal-hold state machine as a first-class, orthogonal state to erasure. Jurisdictional precedence rules documented: e.g., a Saudi court-ordered hold suspends a GDPR erasure request with full auditability of the suspension. Holds have explicit issuers, durations, and release events, all audited.

### 2.10 Pre-Launch Operational Rehearsals

Architectural commitments to rehearsal-driven operations. Each rehearsal exercises a specific failure surface before real traffic finds it:

- **Restore-under-pressure tabletop** — simulate a P1 incident requiring DB restore from a 30-day-old backup. Verify the anonymization log replays as step zero and no erased PII is reintroduced.
- **Cross-jurisdictional legal-hold drill** — simulate a Saudi court order arriving during an in-flight GDPR erasure request. Verify the precedence rules resolve deterministically and are auditable.
- **Rate-feed blackout + divergence drill** — inject a 45-minute feed outage, a 2× price spike, and a duplicate-timestamp payload. Verify locked rates remain stable, no NaN propagates, and users see explicit "rate stale" state rather than wrong math.
- **Skeleton-to-authoritative CI synthetic** — continuous measurement under emulated Slow 4G + 400ms server RTT; fail the build if any calculation path renders a numeric before all dependent denominations hydrate.
- **Public-statement storm drill** — k6 script replicating a WhatsApp-share spike (500 rps burst for 30s). Verify cache-hit > 95%, constant-time unknown-token response holds, and origin doesn't melt.

### 2.11 Value Judgments Declared

Two points in the ranking above are frame-sensitive (different weighting choices produce different orders). To keep the document auditable, we declare those choices explicitly so future reviewers can agree, disagree, or deliberately invert them.

**Judgment 1 — Correctness is architectural, not just algorithmic.**
Risk #1 is treated as compound (correctness + single-path enforcement). A correct math module is insufficient if a second math implementation exists anywhere in the codebase. This merges what might otherwise be two separate risks and is the highest-weight decision in the ranking.

**Judgment 2 — Visible user-facing failures are ranked above invisible-until-disaster failures.**
Audit immutability (#5) is irreversible when it fails — under pure irreversibility weighting, it would rank top-3. We chose to weight visibility more heavily because the product's survival depends on *maintained user trust*, and invisible failures do not erode trust until they manifest as a public disaster. This is defensible but not the only defensible choice; a regulator-first product would weight irreversibility higher and promote #5 into the top tier.

## 3. Starter Template Evaluation & Scaffold Blueprint

### 3.1 Framing — Load-Bearing Decisions (pinned before scaffold design)

Five decisions shape every downstream section. They were settled deliberately — not as a byproduct of scaffold template selection — because each is expensive to reverse once the folder layout, build pipeline, and deployment topology are set.

**D1 — Monorepo Angular workspace with two build targets, not a single SPA.**
The owner workspace and the public statement surface ship from **one Angular workspace** but as **two distinct build artifacts** with compile-boundary separation. Initial direction of "single SPA, route-separated" was pressure-tested and revised: ESLint + CSP + cache-key namespacing is *policy*, not *isolation*. A bundle-graph leak on the hostile public surface is both a reconnaissance gift to attackers and a structural threat to the 180KB gz budget (§3.4). Two build targets make "the public statement bundle cannot import from the owner workspace" a property the compiler enforces, not a property linters hope holds.

**D2 — Pressure-test against a cleaner greenfield; backend is not a DiTracker port.**
Reference documents from DiTracker are a best-ideas pool, not a template. Stack family survives scrutiny (FastEndpoints-or-minimal-API + DDD + Aspire 13 + EF Core) because it maps to Accountantim's NFRs — not because DiTracker uses it. Multi-tenancy, SaaS-shaped tenancy conventions, and the handwritten TS contracts folder do **not** carry over. Every scaffolded file's PR must cite WHY (against NFRs/risks), not WHERE (from DiTracker).

**D3 — Resolve FastEndpoints × Aspire 13 × .NET 10 compatibility before scaffold closes.**
Revised from a "first-commit smoke-test gate" to a **one-day spike before scaffold work begins** (§3.7). The `ReflectionTypeLoadException` tracked in FastEndpoints #1013 must be reproduced against FE 7.x stable on Aspire 13 + .NET 10. If it reproduces, we switch **now** to ASP.NET minimal-API with an endpoint-filter slice pattern. FastEndpoints 8.x beta is **not** acceptable on a fintech production public surface. Deciding pre-scaffold costs ~10× less than a post-scaffold coupling rewrite.

**D4 — Compliance posture decided here, not deferred.**
Bitemporal storage shape, cryptographic posture per data class, `LegalHold` as a first-class Domain aggregate, and `ProcessingActivity` as ambient context are all scaffolded day-one (§3.5). These would be prohibitively expensive to retrofit after the aggregate shapes, repository seams, and observability wiring are locked.

**D5 — TS-contract seams reserved; generator choice deferred.**
A `contracts/` placeholder directory is reserved at repo root. An ESLint rule forbids handwritten shared TypeScript types. An OpenAPI-drift gate fires on day one (§3.5). The specific generator (NSwag / openapi-typescript / Kiota / Aspire-native) is a Step 5 or Step 6 decision. What cannot defer: the *seam* — every HTTP call in Angular flows through generated clients from day one.

### 3.2 Verified Current Versions (2026-04-18)

| Layer | Package / Tool | Version | Status Notes |
|---|---|---|---|
| Runtime — backend | .NET | 10 (LTS) | GA. Default target across all projects. |
| Language — backend | C# | 14 | Bundled with .NET 10 SDK. |
| Host orchestration | .NET Aspire | 13.x | GA. AppHost + ServiceDefaults pattern. |
| IDE | Visual Studio | 2026 | First-class .NET 10 / Aspire 13 support. |
| HTTP framework | FastEndpoints 7.x stable **OR** ASP.NET minimal API | pending spike | Resolved by §3.7. FE 8.x beta **rejected** for fintech production. |
| Messaging / outbox | Wolverine | latest stable | Transactional inbox/outbox with EF Core. |
| ORM | EF Core | 10 | Matches runtime; temporal-table support varies by provider (§3.5 bitemporal). |
| Mapping | Mapperly | latest stable | Source-generated; zero reflection on hot path. |
| Logging | Serilog + OpenTelemetry | latest stable | Stamped with `ProcessingActivity` ambient context (§3.5). |
| Architectural tests | NetArchTest | latest stable | Enforces Domain-layer purity + ambient-context propagation (§3.5). |
| Runtime — frontend | Node.js | 22 LTS | Matches Angular 21 engines. |
| Framework — frontend | Angular | 21 | Zoneless by default. Signal Forms. Vitest-first. |
| UI kit | PrimeNG | 21 | Aura preset + `tailwindcss-primeui` plugin. |
| Styling | Tailwind CSS | 4.x | Paired with PrimeNG theme tokens. |
| i18n | Transloco | latest stable | Runtime bilingual AR/EN + RTL. |
| Test runner (FE) | Vitest | latest stable | Angular 21 default. |
| E2E | Playwright | latest stable | — |
| Contract drift gate | Schemathesis **or** openapi-diff | latest | Day-one CI gate (§3.5). |

Each inclusion above has a justification rooted in an Accountantim NFR or Step 2.7 risk — not inherited from DiTracker by default. Where DiTracker uses the same tool, the justification is re-derived.

### 3.3 Backend Scaffold

**Solution layout (Aspire-rooted):**

```
Accountantim.sln
├── aspire/
│   ├── Accountantim.AppHost/            # Aspire 13 orchestration
│   └── Accountantim.ServiceDefaults/    # OTel, health, resilience wiring
├── src/
│   ├── Accountantim.Api/                # HTTP host (FE OR minimal-API — see §3.7)
│   ├── Accountantim.Application/        # Use cases, ProcessingActivity ambient context
│   ├── Accountantim.Domain/             # Zero-dep. Aggregates, value objects, LegalHold, bitemporal primitives
│   ├── Accountantim.Infrastructure/     # EF Core, Wolverine, Key Vault, read-models
│   └── Accountantim.Contracts/          # Request/Response DTOs — single source of truth for TS generation (D5)
├── tests/
│   ├── Accountantim.Domain.Tests/       # Pure, in-memory
│   ├── Accountantim.Application.Tests/  # Use-case level
│   ├── Accountantim.Api.Tests/          # HTTP contract + endpoint integration
│   ├── Accountantim.Infrastructure.Tests/   # EF/DB-against-testcontainers
│   └── Accountantim.ArchitectureTests/  # NetArchTest — Domain purity, ambient-context enforcement
├── contracts/                            # Reserved for generated TS contracts (D5)
└── .spike/                               # Disposable proof artifacts (see §3.7)
```

**Declared variance vs DiTracker** (per D2):
- **Kept:** Vertical-slice-capable HTTP layer, DDD Domain purity, Wolverine outbox, Aspire AppHost + ServiceDefaults, NetArchTest.
- **Stripped:** Any multi-tenancy scaffolding. SaaS-shaped identity. Handwritten `shared/contracts` TypeScript folder.
- **Added:** `LegalHold` as a first-class Domain aggregate. Bitemporal primitives at the Domain layer. `ProcessingActivity` ambient context propagated through Application, Infrastructure, logs, and Wolverine messages. `contracts/` placeholder + day-one OpenAPI-drift gate.

**Init command sequence** (executed after §3.7 spike outcome is known):

```
dotnet new sln -n Accountantim
dotnet new aspire-apphost -o aspire/Accountantim.AppHost
dotnet new aspire-servicedefaults -o aspire/Accountantim.ServiceDefaults
dotnet new classlib -o src/Accountantim.Domain            # no package deps
dotnet new classlib -o src/Accountantim.Application
dotnet new classlib -o src/Accountantim.Infrastructure
dotnet new classlib -o src/Accountantim.Contracts
dotnet new web      -o src/Accountantim.Api               # FE OR minimal-API template
# Test projects: xunit v3 + FluentAssertions + Testcontainers + NetArchTest
# Solution wiring, project references, initial package adds omitted for brevity
```

The exact HTTP-framework template (`dotnet new feapp` vs `dotnet new web`) is determined by §3.7 — this is the single branch point that must be resolved before the scaffold PR.

### 3.4 Frontend Scaffold — Monorepo, Two Build Targets (per D1)

**Workspace layout:**

```
apps/
├── owner-workspace/              # Build target #1 — authenticated SPA
│   └── src/app/features/
│       ├── dashboard/
│       ├── debtors/
│       ├── agreements/
│       ├── transactions/
│       └── statements/
└── public-statement/             # Build target #2 — unauthenticated, token-scoped surface
    └── src/app/features/
        └── statement-view/       # ONLY feature allowed in this target
libs/
├── design-system/                # PrimeNG Aura theme + Tailwind tokens, design primitives
├── core/                         # HTTP, auth, i18n, cache registry (§3.5), error model, generated clients (D5)
└── domain-ui/                    # Domain-shaped UI primitives safe for both apps (amount display, date display, RTL helpers)
contracts/                        # (repo root, shared with backend) — generated TS clients land here
```

**Compile-boundary separation** (mechanical, not aspirational):
- `apps/public-statement` may import only from `libs/design-system`, `libs/domain-ui`, and a tightly-scoped subset of `libs/core` (HTTP client, i18n, cache registry — **not** auth, **not** owner-only selectors).
- Enforced by Angular workspace path constraints + an ESLint boundary rule keyed on the workspace project graph.
- Build targets produce separate bundle graphs. Cross-target reference = build failure, not lint warning.

**Day-one frontend gates (per R2):**
- **Bundle-graph gate** — CI job parses the public-statement `stats.json` on every PR and fails if any module from `apps/owner-workspace` or forbidden `libs/core` subtrees appears in the graph.
- **Initial-JS-size gate** — Public-statement route budget: **180KB gzipped**, enforced in CI. LCP budget: **< 2.5s** on emulated Slow 4G.
- **Zoneless invariant** — No `zone.js` anywhere in either app's polyfills. Verified by scanning `angular.json` and the actual bundle.

**Cache-key registry (per R3):**
Located in `libs/core/src/cache/`. A typed factory emits keys with mandatory prefix discriminators:
- `pub:*` — public-statement surface only.
- `own:*` — owner-workspace only.

A unit test asserts the registry has no un-prefixed entries. A dev-mode runtime check throws on key collision. The registry is the single authority for HTTP cache, CDN cache, and Service Worker cache keys.

### 3.5 Cross-Cutting Scaffold Elements

**ProcessingActivity ambient context (per R4):**

```csharp
public enum ProcessingActivity {
    OwnerLedger,           // authenticated owner actions on their own data
    DebtorConfirmation,    // public-statement surface, token-scoped, non-authenticated
    SystemBackground       // outbox processors, scheduled jobs, re-materialization
}
```

Propagated via `AsyncLocal<ProcessingActivity>` established at the HTTP entry point (per-endpoint) and at the Wolverine message handler entry. Stamped onto every:
- Serilog log record (as a structured property).
- OpenTelemetry span (as a span attribute).
- Audit-log row (as a non-null column).
- Outgoing Wolverine message (as a header; re-established on the receiving side).

**NetArchTest enforcement:** An architectural test asserts that every endpoint and every message handler either sets a `ProcessingActivity` or inherits one. No ambient default is permitted — uninitialized access throws.

**Bitemporal storage strategy (per R6):**
- **Valid-time:** Explicit columns on every aggregate root that represents money-movement or identity state (`ValidFrom`, `ValidTo`). Modeled in the Domain layer as value objects, not EF concerns.
- **Transaction-time:** Database-native mechanism.
  - On **PostgreSQL** (primary candidate for Azure hosting): trigger-based history tables with `sys_period tstzrange`, queried via the `temporal_tables` extension pattern.
  - On **SQL Server** (fallback): system-versioned temporal tables.
- **Query seam:** Repository interfaces expose `AsOf(DateTimeOffset transactionTime, DateTimeOffset? validTime = null)`. The default (no arguments) returns the current valid state as of now. The seam is defined at the Domain layer; the implementation lives in Infrastructure.

**Cryptographic posture per data class (per R7):**

| Data class | Erasure strategy | Mechanism |
|---|---|---|
| Transactional rows (amounts, dates, entity refs) | **Key-shred pseudonymization** | Per-subject encryption keys held in Azure Key Vault. Erasure = destroy the subject's key. Rows remain, but PII-bearing columns become irrecoverable ciphertext. Preserves audit totals, breaks linkability. |
| Derived aggregates (stats, reports) | **k-anonymity (k ≥ 5)** | Aggregates materialized only when group size ≥ 5 natural persons. Smaller groups are not reported. Enforced at materialization, not at query time. |
| Contact PII (phone, email, name) | **Tombstone** | Hard-delete the row, replace FKs with a permanent tombstone token. Re-hydration is impossible and audit trails reference the token. |

Each aggregate declares its class in its Domain type. Repositories enforce class-appropriate erasure at the point of the GDPR request — no centralized "delete_user" procedure.

**LegalHold as a first-class Domain aggregate (per R8):**

```
LegalHold {
    Id, Jurisdiction, Authority,   // which regulator / court / internal counsel
    Scope,                          // subject(s), aggregate(s), time range
    Precedence,                     // resolution rule when holds overlap or conflict
    State                           // Issued → Active → Released
}
```

State transitions are Domain events, persisted to the audit log, and **block** the erasure pathways above while `Active`. The `Precedence` rule fires when a user's erasure request collides with an active hold — the user's request is recorded but not executed; a deferred-erasure record is created and surfaces when the hold releases.

**Day-one contract-drift gate (per R9 / D5):**
- A snapshot of `openapi.json` is committed to the repo on every API-surface change.
- CI runs **Schemathesis** (or `openapi-diff`) comparing the generated spec from the running API against the committed snapshot. Drift fails the build.
- An ESLint rule (`no-handwritten-shared-contracts`) forbids any TypeScript file under `contracts/` that is not marked as generated.
- Every HTTP call in Angular flows through a generated client. No `HttpClient.get<MyType>(...)` where `MyType` is a handwritten interface describing a backend DTO.

### 3.6 Architectural Decisions Provided by Scaffold (and Explicitly Deferred)

**Provided by scaffold (settled now):**
- Monorepo Angular workspace with two build targets + compile-boundary separation (D1).
- Aspire 13 + ServiceDefaults as the host orchestration layer.
- Zero-dep Domain layer; DDD aggregate shapes; Wolverine outbox; Mapperly mapping; Serilog + OTel observability.
- NetArchTest as the architectural-rule enforcement mechanism.
- `ProcessingActivity` ambient context (R4).
- Bitemporal storage strategy and query seam (R6).
- Cryptographic posture per data class (R7).
- `LegalHold` as a Domain aggregate (R8).
- Contract-drift gate + `contracts/` seam + no-handwritten-shared-contracts ESLint rule (R9 / D5).
- Public-statement bundle-graph gate + 180KB gz budget + cache-key registry (R2 / R3).

**Explicitly deferred (with their natural resolution step):**
- Specific TypeScript-contract generator (NSwag vs openapi-typescript vs Kiota vs Aspire-native) — **Step 5 or Step 6.**
- Exact HTTP framework package (FastEndpoints 7.x vs minimal API with endpoint-filter slices) — **§3.7 spike outcome.**
- Database engine final selection (PostgreSQL vs SQL Server on Azure) — **Step 4 (component design) / Step 8 (infrastructure).** Both paths above are viable; the choice affects bitemporal implementation detail, not shape.
- Identity provider (Entra External ID vs self-hosted Duende IdentityServer vs Auth0) — **Step 4 / Step 8.**

### 3.7 Pre-Scaffold Spike Protocol — FastEndpoints × Aspire 13 × .NET 10

**Goal:** Resolve D3 before the scaffold PR is opened. Time-boxed to **one working day.**

**Procedure:**
1. In `.spike/fe-aspire-smoke/`, scaffold a minimal Aspire 13 AppHost + ServiceDefaults + single FE 7.x (latest stable) API project on .NET 10.
2. Define three endpoints: a GET returning a DTO, a POST accepting a DTO, and an endpoint that triggers a Wolverine message round-trip.
3. Run under `dotnet run` via AppHost. Exercise all three endpoints through the Aspire dashboard's integrated client.
4. **Observe:** Does the `ReflectionTypeLoadException` (FastEndpoints #1013) reproduce? Does OTel instrumentation propagate through FE's pipeline? Do endpoint-filters + FE's `Configure()` interact cleanly?

**Decision rule (property-based, not symptom-based):**

The spike's acceptance criterion is written in terms of the property that motivates ADR-006 — not the symptom that motivates the spike. Four vertical-slice properties must hold under FE 7.x + Aspire 13 + .NET 10:

1. **Per-slice DI scope** — each endpoint can declare its own DI scope without leaking into sibling slices.
2. **Per-slice auth** — auth requirements expressed at the slice, not in a shared pipeline branch keyed on route.
3. **Per-slice testability** — endpoint + its handler are testable in isolation (WebApplicationFactory-level) without booting the full host graph.
4. **Per-slice `ProcessingActivity` set point** — ambient context set at slice entry and propagated through handler, EF Core, Wolverine messages, Serilog logs, and OTel spans (per ADR-004).

**Outcomes:**
- **All four properties preserved; no #1013 reproduction:** Scaffold uses FastEndpoints 7.x. Pin observed versions in `.spike/fe-aspire-smoke/DECISION.md`.
- **All four properties preserved via a documented workaround; #1013 reproduces but has a clean mitigation:** Scaffold uses FastEndpoints 7.x with the workaround pinned and cross-referenced from ADR-003.
- **Any one property requires a pipeline hack to preserve, OR #1013 has no clean mitigation:** Scaffold uses **ASP.NET minimal API with endpoint-filter slice pattern** — one file per endpoint under `src/Accountantim.Api/Endpoints/`, mapping its own route and filters. Vertical-slice properties preserved; FE's ergonomics lost but NFR budget and ADR-006 invariant intact. **FastEndpoints 8.x beta is not evaluated** for fintech production.

The rule is written this way because `#1013` is a symptom; vertical-slice preservation is the property. A green `#1013` with broken slice DI is still a rejection. A red `#1013` with a clean mitigation that preserves the four properties is acceptable.

**Artifact:** Spike outcome committed to `.spike/fe-aspire-smoke/DECISION.md` with observed versions, a per-property evidence log (one paragraph per property above), the `#1013` reproduction trace, and the chosen path. This document is referenced from the Step 3 scaffold PR description so reviewers can verify the choice is grounded in the ADR-006 properties, not in release-note hope.

### 3.8 Architecture Decision Records

The five decisions introduced as narrative framing in §3.1 are recorded here as formal ADRs with options considered, consequences, and **reversibility cost** — the last of which lets a future reviewer judge which decisions are cheap to re-litigate versus one-way doors that close quickly after scaffold.

#### ADR-001 — Two Build Targets for the Angular Workspace

**Status:** Accepted (2026-04-18) · supersedes D1

**Context:** Owner workspace and public statement surface share design language, but the public surface is hostile (Step 2.7 R#2: 80 rps sustained / 500 rps burst / p95 < 300 ms / cache-hit > 95%). Public route budget: 180 KB gz / LCP < 2.5 s on Slow 4G.

**Options considered:**
- **A.** Two separate apps in two repos — maximum isolation; worst velocity; design-system duplication or brittle npm-link dance.
- **B.** Single SPA, route-separated + ESLint / CSP / cache-key policies — maximum velocity; leak-by-mistake is one merged PR away; bundle-graph contamination silently breaks the 180 KB budget.
- **C.** One Angular workspace, two build targets + compile-boundary separation — shared `libs/design-system` + `libs/domain-ui`; public bundle cannot import from owner workspace.

**Decision:** **C.**

**Consequences:** `+` Compile-enforced isolation (property, not policy). `+` Shared design tokens + i18n without cross-target leak. `+` Independent CDN / WAF / cache strategies per artifact. `−` Workspace config complexity. `−` Requires bundle-graph gate discipline in CI (§3.4).

**Reversibility:** Workspace → two apps ≈ 1 sprint. Single-SPA → workspace-with-targets ≈ 2–3 sprints (bundle-graph debt + cache-key retrofit). Choosing C now avoids the second, more expensive path.

**NFR / Risk links:** Step 2.7 R#2, Step 2.3 performance envelope.

---

#### ADR-002 — Greenfield Backend (Not a DiTracker Port)

**Status:** Accepted · supersedes D2

**Context:** DiTracker is battle-tested B2B multi-tenant SaaS. Accountantim is B2C fintech, bilingual MENA + EU, personal / community ledger. Architecture frontmatter declares `referenceStackAuthority: advisory_only`.

**Options considered:**
- **A.** Fork DiTracker as starter; rip what doesn't fit.
- **B.** Port DiTracker's skeleton verbatim; re-examine conventions post-scaffold.
- **C.** Fresh scaffold; adopt DiTracker patterns only where they survive Accountantim NFR scrutiny.

**Decision:** **C.**

**Consequences:** `+` Every inherited convention is a deliberate choice, not a habit. `+` No B2B tenancy residue in the Domain layer. `−` More upfront scaffold time. `−` Reviewers lose the "same shape as DiTracker" heuristic and must reason from NFRs instead.

**Reversibility:** One-way door for early commits — once conventions land in the Domain layer, retrofitting is a cultural rewrite across every feature slice. Forking DiTracker now and stripping later ≈ same total cost as doing C correctly now, plus cognitive debt.

**NFR / Risk links:** Step 1 product brief (B2C, fintech, bilingual), variance declared in §3.3.

---

#### ADR-003 — Pre-Scaffold Spike for FastEndpoints × Aspire 13 × .NET 10

**Status:** Accepted · supersedes D3

**Context:** FastEndpoints issue #1013 reports `ReflectionTypeLoadException` on Aspire 13 + .NET 10. Resolution unverified for FE 7.x stable. FE 8.x beta rejected for fintech production. Initial plan was a first-commit smoke-test gate.

**Options considered:**
- **A.** Commit to FE 7.x; smoke-test on first commit; pivot after if broken.
- **B.** Commit to FE 8.x beta now.
- **C.** Skip FE evaluation; commit to ASP.NET minimal-API with endpoint-filter slices.
- **D.** One-day spike in `.spike/fe-aspire-smoke/` before the scaffold PR; pick A or C from spike evidence.

**Decision:** **D.**

**Consequences:** `+` Decision grounded in observed behavior, not release-note hope. `+` Pivot cost stays minimal. `+` Spike artifact (`DECISION.md` with version matrix + reproduction log) becomes a reviewable PR reference. `−` One-day delay on the scaffold PR. `−` Requires Aspire 13 + .NET 10 dev env to exist before scaffold work.

**Reversibility:** Pre-scaffold pivot = trivial (delete `.spike/`, re-template). Post-scaffold pivot A → C ≈ 2+ sprints (endpoint coupling, pipeline conventions, test factories, validator wiring).

**NFR / Risk links:** Fintech production surface, no-beta-on-money-path rule, §3.7 procedure.

---

#### ADR-004 — Compliance Posture Pinned at Scaffold Time

**Status:** Accepted · supersedes D4

**Context:** GDPR erasure vs. audit integrity is Accountantim's central regulatory tension. Bitemporal storage, crypto-erasure per data class, `LegalHold` as a Domain aggregate, and `ProcessingActivity` as ambient context are all retrofit-hostile once aggregate shapes and repository seams land.

**Options considered:**
- **A.** Ship MVP with flat data shape; retrofit compliance when regulators escalate.
- **B.** Ship procedural compliance only (DPIA checklists, erasure endpoints); leave data shape flat.
- **C.** Pin data shape + mechanisms day-one: valid-time + transaction-time, crypto posture per data class, `LegalHold` aggregate, `ProcessingActivity` ambient context enforced by NetArchTest.

**Decision:** **C.**

**Consequences:** `+` Erasure-vs-audit contradictions resolved structurally, not through runbook prayers. `+` Regulator conversation starts from a defensible posture with audit trails. `+` `LegalHold` precedence rule turns informal holds into replayable Domain events. `−` Domain layer is heavier from day one. `−` `AsyncLocal<ProcessingActivity>` requires onboarding discipline and NetArchTest enforcement.

**Reversibility:** Bitemporal retrofit per aggregate = multi-sprint per aggregate + data migration + re-audit. `LegalHold` retrofit = event-log rewrite. Doing C now is ≈ 3× cheaper than any A-to-C or B-to-C retrofit path.

**NFR / Risk links:** Step 2.7 R#5 audit immutability, Step 2.3 compliance envelope, MENA + EU data-protection regimes.

---

#### ADR-005 — TypeScript Contract Generator Deferred; Seams Reserved

**Status:** Accepted · supersedes D5 · generator choice deferred to Step 5 / 6

**Context:** DiTracker ships a handwritten shared TS contracts folder; it drifts from C# DTOs. Accountantim goal: auto-generate TS from C# DTOs. Generator choice (NSwag / openapi-typescript / Kiota / Aspire-native) depends on API conventions not yet settled.

**Options considered:**
- **A.** Pick a generator now (e.g., NSwag) and commit.
- **B.** Ship a handwritten shared `contracts` folder in the interim.
- **C.** Reserve the seams now: `contracts/` placeholder, ESLint `no-handwritten-shared-contracts` rule, day-one OpenAPI-drift gate, generated-client pattern for all HTTP calls. Generator choice lands when API conventions land.

**Decision:** **C.**

**Consequences:** `+` No premature commitment to a generator that may lose a bake-off in Step 5 / 6. `+` Every HTTP call flows through a generated client from day one — the tool behind it is swappable. `+` Drift gate fires without a generator (it protects the committed `openapi.json` snapshot regardless). `−` Small velocity tax until the generator lands (workarounds for local types are explicit). `−` A deferred decision remains a visible open item on the scaffold board.

**Reversibility:** Generator swap with seams in place = low cost (regenerate clients, update imports). Removing seams to unblock handwritten types = drift debt + cultural rewrite. The *seam* is the load-bearing piece, not the generator.

**NFR / Risk links:** Contract-drift prevention, single-source-of-truth principle, Step 5 / 6 natural resolution step.

---

---

#### ADR-006 — Vertical-Slice Architecture for the Api Layer

**Status:** Accepted · **upstream of ADR-003** · makes explicit the decision implicit in §3.3 and §3.7

**Context:** Accountantim's regulatory surface is volatile — bilingual MENA + EU, fintech, hostile public-statement path (Step 2.7 R#2), audit-integrity invariant (Step 2.7 R#5). Features may need to be carved out, reshaped, or scoped down atomically in response to regulator action without cascading refactors. ADR-004 pins `ProcessingActivity` as ambient context *set at the slice boundary*, which presupposes that "the slice" is a real unit of code, not a convention spanning layers. This decision was surfaced by a 5 Whys deep dive on ADR-003: the spike's urgency only makes sense because endpoint-framework choice is deeply coupled to slice shape, and that coupling only exists because we're committed to vertical-slice. Leaving this implicit would let a future reviewer reasonably ask "why not layered?" without a written answer.

**Options considered:**
- **A.** Layered architecture — thin Controllers over Application services, shared pipeline for auth/validation. HTTP framework is near-disposable; features distributed across horizontal layers.
- **B.** Feature-folder layered (compromise) — files grouped by feature, but shape remains layered: controller + service + repo per folder. Looks vertical; isn't.
- **C.** Vertical-slice architecture — each endpoint class owns its request, handler, response, validation, auth, DI scope, and `ProcessingActivity` setup. HTTP framework is FE 7.x or ASP.NET minimal-API with endpoint-filter slices (both preserve the property per ADR-003 / §3.7).

**Decision:** **C.**

**Consequences:** `+` Feature-level deletability / carve-out when a regulator acts on a single feature. `+` Ambient `ProcessingActivity` set at a real code boundary, enforceable by NetArchTest (per ADR-004). `+` Test factories scoped per slice — no shared-service mock mountain. `+` Downstream pressure on endpoint-framework choice (ADR-003) becomes legible rather than mysterious. `−` HTTP-framework choice is coupled to slice shape, which is the exact cost ADR-003 was designed to contain. `−` New-dev onboarding must teach the "the slice is the unit" mental model; layered intuitions will mislead.

**Reversibility:** Vertical-slice → layered = multi-sprint cultural rewrite per feature slice (every endpoint's test factory, DI, auth, and ambient-context setup moves to a different home). Layered → vertical-slice = roughly symmetric cost. This is the upstream decision that makes ADR-003 load-bearing; reversing ADR-006 would make ADR-003 unnecessary — but also surrender the volatility-response property that motivates ADR-004.

**Load-bearing assumption this ADR rests on:** *Accountantim's regulatory surface is volatile enough that feature-level deletability / carve-out-ability is a first-class architectural property, not a nice-to-have.* If a future reviewer successfully argues the surface is stable, ADR-006 weakens and a simpler layered architecture becomes defensible — which in turn retires ADR-003.

**NFR / Risk links:** Step 2.7 R#5 audit immutability, ADR-004 compliance posture, bilingual MENA + EU regulatory scope, hostile public-statement surface (R#2 carve-out pressure).

---

**Reversibility summary (one-way doors vs. re-litigable):**

| ADR | Status | Reversibility class |
|---|---|---|
| ADR-001 — Two build targets | Accepted | **Re-litigable** (~1 sprint to two-apps) |
| ADR-002 — Greenfield backend | Accepted | **One-way door** after first feature-slice commits |
| ADR-003 — FE × Aspire spike | Accepted | **Cheap now, expensive after scaffold** — bounded by §3.7 |
| ADR-004 — Compliance day-one | Accepted | **One-way door** after aggregates land |
| ADR-005 — TS-contract seams | Accepted (generator deferred) | **Re-litigable** — seams cost < generator cost |
| ADR-006 — Vertical-slice Api | Accepted | **One-way door** after first feature-slice commits · retires ADR-003 if reversed |

Three of the six are one-way doors (ADR-002, ADR-004, ADR-006) — they should receive the most scrutiny before the scaffold PR merges. One (ADR-003) is currently cheap but closes quickly — bounded by the spike window in §3.7 and the property criteria above. Two (ADR-001, ADR-005) remain re-litigable without catastrophic cost.

**Dependency chain among the ADRs:**

```
ADR-006 (vertical-slice) ──┬──► ADR-003 (FE × Aspire spike needed)
                            │
                            └──► ADR-004 (ProcessingActivity set at slice boundary)
                                          │
                                          └──► NetArchTest enforcement (§3.5)

ADR-001 (two build targets) — independent, motivated by R#2 alone
ADR-002 (greenfield backend) — independent, motivated by B2C/fintech scope
ADR-005 (TS-contract seams) — independent, motivated by contract-drift risk
```

ADR-006 is the **root** of the scaffold-side chain. Reversing it collapses ADR-003 (no framework coupling) and materially weakens ADR-004 (no natural slice boundary for ambient context). This is the decision a reviewer should challenge first if they want to reshape the scaffold.

## 4. Core Architectural Decisions

This step records 20 decisions facilitated collaboratively (D4.1–D4.20) across five categories. Each decision was selected against the Step 2 risk register and the Step 3 scaffold invariants — not against generic best practice. Many decisions deliberately chose the trimmest defensible option for MVP, with **explicit revisit triggers** (measurable conditions) that should reopen the decision when crossed.

### 4.1 Decision Priority Analysis

**Critical (block implementation — must hold before first feature slice merges):**
D4.1 (Postgres) · D4.5 (Keycloak) · D4.6 (capability token + CDN-cached opaque tokens) · D4.8 (envelope encryption posture) · D4.9 (RFC 9457 + domain code taxonomy) · D4.11 (Idempotency-Key + natural-key backstop) · D4.16 (ACA hosting) · D4.18 (Frankfurt region).

**Important (shape architecture — must hold before component design closes):**
D4.2 (no Redis) · D4.3 (EF Core migrations + idempotent trigger installer) · D4.7 (app-level rate limiting) · D4.10 (URL-path versioning) · D4.12 (Wolverine Postgres transport) · D4.13 (signals + plain services + rxResource) · D4.14 (Signal Forms + FormMutationConfig) · D4.17 (GitHub Actions + azd + OIDC) · D4.19 (AFD Standard + SWA) · D4.20 (App Insights + sampling).

**Deliberately deferred (with natural resolution step):**
- TypeScript contract generator (NSwag / openapi-typescript / Kiota / Aspire-native) — Step 5 / 6 (per ADR-005).
- CQRS read-model materialization mechanism (D4.4 = "no formal CQRS at MVP" — re-evaluate per slice if needed) — Step 5.
- ACA Jobs decomposition for scheduled work — revisit when first scheduled job exceeds 30s runtime or 5 distinct jobs exist.
- AFD Premium upgrade — revisit on first sustained bot/credential-stuffing evidence.
- Installable PWA manifest (D4.15 deferred to v1.1+ as a separate post-MVP feature, not a revisit trigger).
- Multi-region Postgres geo-replication — revisit on EU-MENA paid customer commitments.

### 4.2 Data Architecture (D4.1–D4.4)

#### D4.1 — Database Engine: **PostgreSQL on Azure Database for PostgreSQL Flexible Server**

**Decision:** PostgreSQL 16+ via Azure Database for PostgreSQL Flexible Server in Germany West Central. Single primary with zone-redundant HA at production tier.

**Rationale:** Bitemporal storage (per Step 3 §3.5) maps cleanly to Postgres's `tstzrange` + trigger-based system_period pattern via the well-trodden `temporal_tables` extension. EF Core 10 has first-class Npgsql provider support. Cost curve for B2C is materially lower than Azure SQL Hyperscale at the same scale envelope. JSONB and partial-index support make audit-log queries and quasi-identifier policy enforcement (per §2.9) ergonomic.

**Reversibility:** **One-way door after first feature slice commits.** Bitemporal trigger code, EF migrations, and operational runbooks are provider-specific.

**NFR / Risk links:** R#1 (arithmetic correctness — typed columns), R#5 (audit immutability — trigger-enforced append-only), R#6 (concurrency — `SERIALIZABLE` available + advisory locks), Step 2.6 cross-cutting concern #1 (bitemporal).

#### D4.2 — Caching Layer: **CDN-only, no Redis**

**Decision:** No application-tier cache. Public-statement edge caching via Azure Front Door (D4.19). Owner-workspace data is per-user-mutable and CDN-unfriendly; the browser HTTP cache + service-side EF Core query plan caching suffice. Postgres holds all stateful caches (idempotency keys, capability tokens, sessions).

**Rationale:** Adding Redis introduces a second stateful component, an additional network hop, cache-coherence complexity (DB↔cache invalidation), and an attack surface — for a MVP where cache hit-rate >95% on the public surface is achieved at the AFD edge, not in-process. Postgres is faster than Redis for the access patterns we have (small per-request lookups in indexed tables) once the tail-latency cost of a hop is accounted for.

**Reversibility:** Re-litigable. Redis can be added behind an interface seam without schema changes.

**Revisit trigger:** if Postgres CPU exceeds 60% sustained on read-heavy workloads AND profiling shows cache-eligible query patterns, evaluate Azure Cache for Redis Standard tier.

**NFR / Risk links:** R#2 (public surface 80/500 rps — solved at AFD), Step 3 §3.4 cache-key registry constraint.

#### D4.3 — Migration Tooling: **EF Core migrations + idempotent trigger installer (Aspire MigrationService)**

**Decision:** EF Core 10 migrations for schema. Bitemporal triggers and Postgres-native objects (extensions, functions, view-or-trigger definitions) installed via an Aspire-orchestrated **MigrationService** that runs idempotently against every environment on startup. The MigrationService asserts trigger presence and recreates if drift detected.

**Rationale:** EF migrations handle relational schema cleanly; trigger code lives outside EF's awareness and would silently drift if managed manually. The MigrationService pattern keeps "schema present + trigger present + extension installed" as one atomic precondition for application boot. Idempotency means dev/staging/prod all converge to the same shape on every cold start.

**Reversibility:** Re-litigable. Could substitute Grate or hand-rolled SQL versioning.

**NFR / Risk links:** R#5 (audit immutability via triggers), R#6 (concurrency invariants enforced by triggers), Step 3 §3.5 bitemporal storage.

#### D4.4 — Read-Model Strategy: **No formal CQRS at MVP**

**Decision:** No separate read model. EF Core projections in Application-layer query handlers serve all read paths. Public-statement reads use indexed Postgres queries hitting the AFD cache layer for the >95% hit-rate target.

**Rationale:** CQRS adds a projection pipeline, eventual-consistency reasoning, and a second data shape — costs that don't pay off until either write contention or read-shape divergence exceeds what indexed projections can serve. Neither condition holds at MVP scale.

**Reversibility:** Re-litigable per slice. Wolverine's projection support can be added incrementally for any slice that demonstrates the need.

**Revisit trigger:** any slice where (a) read query exceeds 100ms p95 after indexing, OR (b) read shape diverges meaningfully from write shape, OR (c) read load multiplies write load by >10x.

**NFR / Risk links:** R#2 (public-statement read path), Step 2.6 cross-cutting #15 (canonical DomainMath enforcement holds regardless of read shape).

### 4.3 Authentication & Security (D4.5–D4.8)

#### D4.5 — Identity Provider: **Keycloak (self-host) on Azure Container Apps**

**Decision:** Keycloak self-hosted on Azure Container Apps (D4.16), single realm `accountantim` with multiple identity providers (Google, Apple, phone-OTP via custom SPI, email+password). Themed per locale via Keycloak theme customization.

**Rationale:** Trades operational weight (patching, SPI maintenance, theme work) for cost predictability and vendor independence — the cost curve of Entra External ID and Auth0 tilts steeply against B2C growth, with per-MAU pricing ceilings that constrain product decisions. Keycloak flips the curve: high fixed cost at launch, near-zero marginal cost at scale. For a fintech expecting growth, this is the defensible long-term shape. Compliance posture stays inside our Frankfurt perimeter.

**Hard cascading commitments:**
- **Dedicated Keycloak Postgres database** on the shared Flexible Server instance (separate DB, not separate server) — for blast-radius and erasure-transactionality.
- **SMS OTP delivery via custom SPI** integrating Azure Communication Services (preferred MENA reach) or Twilio fallback.
- **Realm signing keys backed by Azure Key Vault** via the Keycloak Vault SPI / community extension — pins rotation into D4.8.
- **Erasure flow:** owner request → LegalHold check → Keycloak Admin API user delete → `SubjectKeyDestroyed` domain event → key-shred → audit trail preserved.
- **Patching commitment:** monthly Keycloak patch window with automated upgrade tests in CI.

**Reversibility:** **One-way door after first real users sign up.** Subject IDs live in Keycloak forever; migration requires permanent subject-ID mapping tables. Same reversibility class as ADR-002 / ADR-004 / ADR-006.

**Recommended pre-scaffold spike (mirror of §3.7):** one-day Keycloak deployability spike on Aspire 13 + ACA, exercising single-realm + multi-provider + Vault SPI + SMS SPI seams. Outcome documented in `.spike/keycloak-aspire-smoke/DECISION.md`. Surface as **§3.9** when it lands.

**NFR / Risk links:** R#2 (public-statement is unauthenticated — Keycloak protects only owner workspace), R#3 (GDPR erasure flow integrates via Admin API), Step 2.9 adversarial baseline.

#### D4.6 — Public-Statement Token Scheme: **Capability token (opaque server-side + CDN cache + URL-rotation invalidation)**

**Decision:** 128-bit opaque token in URL (base64url ≈ 22 chars). Server-side state in a Postgres `capability_tokens` table holds `{token_hash, owner_subject_id, scope, exp, revoked_at, issued_by, version_byte}`. AFD caches per-token responses with the token as the cache key. Revocation = delete row + AFD purge for that key + URL rotation for the new live URL. Possession-of-URL grants access (capability-security pattern).

**Hard cascading commitments:**
- **Token never appears in logs, telemetry, or error messages.** Serilog + OTel span attribute redaction enforced by NetArchTest rule (e.g., `[ConstantTimeResponse]` attribute).
- **Constant-time response on unknown/expired tokens** — deliberate "always-do-the-work-then-return-generic-payload" pattern. Tested via timing-comparison harness on a representative endpoint.
- **In-band token version byte** so future scheme migrations can run side-by-side without URL rewrite.
- **Key-shred propagation (ADR-004)** — when owner's subject key is destroyed, all their capability tokens become inoperative via the join column to `owner_subject_id`.
- **Token stripped from browser history** via `history.replaceState` after first navigation.

**Reversibility:** Re-litigable for the storage shape; **one-way door** for the URL shape (existing public links must keep working forever — bookmarkability is part of the user contract).

**NFR / Risk links:** R#2 (hostile public surface), Step 2.9 (public surface hardening — all 11 properties apply), Step 2.10 (public-statement storm drill validates this decision).

#### D4.7 — Rate-Limiting Topology: **App-level only at MVP** (with explicit reversal trigger to add WAF rate limiting)

**Decision:** `Microsoft.AspNetCore.RateLimiting` middleware in the API. Two policies: `public-token` (per-token + per-IP-hash bucket, matching the 80/500 rps envelope) and `owner-api` (per-authenticated-user). No WAF-tier rate limiting at MVP — AFD Standard's WAF managed ruleset blocks malformed requests but rate limiting is handled in-process.

**Rationale:** App-level gives us per-token granularity that WAF rules cannot (WAF doesn't know what a "token" is). At MVP traffic, the cost of a 429 returned in-process is negligible compared to the operational simplicity of one rate-limit codebase.

**Reversibility:** Re-litigable. Adding AFD Premium rate-limit rules is additive, not replacement.

**Revisit trigger:** if any of (a) sustained bot traffic >1000 req/hr, OR (b) credential-stuffing pattern detected, OR (c) backend p95 latency degrades during burst due to rate-limit middleware overhead, upgrade to AFD Premium and split rate-limiting across edge + app.

**NFR / Risk links:** R#2 (burst envelope), Step 2.9 adversarial baseline (two-layer rate limiting noted as ideal, deferred to revisit trigger).

#### D4.8 — Secret / Key Custody: **Envelope encryption (Vault master KEK + per-subject DEK + per-row IV)**

**Decision:** Two-tier envelope encryption per ADR-004's key-shred posture:
- **Master KEK** held in Azure Key Vault Premium (Managed HSM-backed) in Germany West Central. Annual rotation + on-demand. Never leaves Vault.
- **Per-subject DEK** generated on user creation, encrypted with the master KEK, stored in the `subject_keys` Postgres table. Decrypted in-memory only when needed for write/read of that subject's PII.
- **Per-row IV** (initialization vector) generated per encrypted row, stored alongside the ciphertext column.

**Hard cascading commitments:**
- **Vault separation per environment** (dev / staging / prod), plus a **dedicated Vault for Keycloak** signing keys (per D4.5).
- **Standard tier Vault** for non-crypto secrets (connection strings, API keys, OIDC client secrets).
- **All access via Managed Identity** — no service principal secrets stored anywhere.
- **Erasure = destroy the subject's DEK row** in `subject_keys`. Ciphertext remains, becomes irrecoverable. Audit math (totals, balances) preserved because the erased data was PII columns, not amount columns.
- **KEK rotation procedure:** generate new KEK version, re-wrap all DEKs with new KEK, mark old KEK version as decryption-only, decommission after grace period.

**Reversibility:** **One-way door after first encrypted row.** Switching encryption schemes requires re-encryption migration of every encrypted column.

**NFR / Risk links:** ADR-004 (compliance posture), R#3 (GDPR erasure vs audit integrity), Step 2.9 data-lifecycle hardening.

### 4.4 API & Communication Patterns (D4.9–D4.12)

#### D4.9 — Error Model: **RFC 9457 Problem Details + domain code taxonomy + ErrorCatalog registry**

**Decision:** RFC 9457 (`application/problem+json`) as the wire format for all error responses. Every error carries:
- Standard fields: `type`, `title`, `status`, `detail`, `instance`.
- Extensions: `traceId` (W3C trace context — drives D4.20 correlation), `code` (domain code in the form `accountantim.<slice>.<condition>`, e.g., `accountantim.statements.token_revoked`), `errors[]` (RFC 9457 array of per-field violations with JSON Pointers, drives D4.14 server-error mapping).
- A central `ErrorCatalog` registry in `src/Accountantim.Application/Errors/` maps every domain code to its `type` URI, default `title`, HTTP status, and Transloco translation key. **NetArchTest enforces** that every `throw DomainException` references a code that exists in `ErrorCatalog` (compile-time-equivalent guarantee).

**Pre-mortem & first-principles amendments folded in:**
- **`AsyncOperationFailed` event seam** reserved on the bus for failures that occur outside the synchronous request-response path (e.g., outbox handler failures). Notification surfaces in a `NotificationInbox` aggregate the user can read on next session.
- **Sunset header policy** for any external clients introduced post-MVP (initially N/A — internal generated TS clients only).
- **`deduplicated: true` and `returnedExisting: true` flags** in success-response bodies when an idempotent operation returned a cached result rather than executing — closes the silent-merge trust gap.

**Reversibility:** Envelope shape is re-litigable. **Domain code taxonomy is one-way after external clients exist** — codes become a public API. Until then, re-litigable with a search-and-replace.

**NFR / Risk links:** Step 3 ADR-005 (TS generator emits this shape), R#5 (audit-log row references trace_id and error code).

#### D4.10 — API Versioning: **URL-path `/v1/` via `Asp.Versioning` package**

**Decision:** `/v1/` path prefix on all API routes from day one, using `Microsoft.AspNetCore.Mvc.Versioning` (Asp.Versioning) package. **12-month deprecation window** for any version retirement; **maximum 2 concurrent versions** in production. **Public-statement path pinned at `/v1/` forever** (URLs are bookmarkable and live in WhatsApp threads — they cannot be retired).

**Reversibility:** Re-litigable for the *strategy*. **One-way door for the public-statement v1 path** (URL shape is part of the user contract).

**Revisit trigger:** when first external API consumer (third-party integration, mobile app, partner) exists, formalize the deprecation policy in a public versioning doc and add `Sunset` headers to deprecated routes.

**NFR / Risk links:** R#2 (public-statement URL stability), ADR-005 (generator regenerates per version).

#### D4.11 — Idempotency: **`Idempotency-Key` header + per-endpoint natural-key constraints (belt-and-suspenders)**

**Decision:** Two layers, both required:
1. **`Idempotency-Key` HTTP header** middleware: client generates UUID v4 per render, refreshed on form success/reset. Postgres `idempotency_keys` cache table with 24h TTL, Wolverine-swept. Returns 409 on key conflict mid-execution; returns cached response on key-replay-after-success.
2. **Domain natural-key DB constraints** per endpoint, classified as **domain-idempotent** (e.g., debt-confirmation: natural key = `(debt_id, debtor_id)`) vs **domain-creative** (e.g., new-debt: no natural key exists, header is sole guard).

**Race-condition handling (Amelia + Murat convergence from Party Mode):**
- **Row-level lock** (`SELECT … FOR UPDATE`) on the idempotency_keys row when handler enters.
- **TTL sweeper skips in-flight rows** (`WHERE last_seen_at < now() - interval '5 min'`) to prevent racing with active handlers.

**Reversibility:** Re-litigable. The `Idempotency-Key` header convention is cheap to add or remove.

**NFR / Risk links:** Step 2.6 cross-cutting #4 (idempotency + outbox), R#6 (race on ledger writes).

#### D4.12 — Wolverine Transport: **Postgres-only at MVP** → reversal trigger to Azure Service Bus Standard

**Decision:** Wolverine's Postgres-backed transport (same database as the application). Same-transaction outbox: domain mutations + outbox-enqueue commit atomically. 3 retries with exponential backoff. Failures land in a `dead_letter_messages` Postgres table with structured payload + replay tooling.

**Rationale:** Same-transaction outbox is the strongest possible delivery guarantee — no message is enqueued for an aborted transaction, period. Service Bus would split that guarantee across two systems. At MVP scale, Postgres handles the message volume trivially.

**Reversibility:** Re-litigable. Wolverine abstracts the transport; switching is a configuration change + Service Bus provisioning.

**Revisit trigger:** **first cross-process consumer** (e.g., a separate worker that's not the API). Postgres transport doesn't gracefully handle multi-process consumers; that's the moment to migrate to Azure Service Bus Standard.

**NFR / Risk links:** R#5 (audit-trail consistency via outbox), R#8 (rate-feed delivery semantics).

### 4.5 Frontend Architecture (D4.13–D4.15)

#### D4.13 — State Management: **Signals + plain services + Angular `rxResource()` / `resource()`**

**Decision:** No state library. Cross-cutting state lives in signal services (`CurrentUserService`, `LocaleService`, `NotificationService`, `CapabilityTokenService`). Per-feature state via Angular's built-in `rxResource()` and `resource()` primitives. `libs/core` owns shared state; feature folders own their own.

**Hard cascading commitments:**
- **ESLint boundary rule** blocks **all cross-feature imports** except from `libs/*` (mirrors backend NetArchTest intent — closes the loophole where non-state code bypasses the boundary).
- **Sharpened revisit trigger** (Winston + Amelia convergence from Party Mode): first feature requiring derived state spanning ≥2 slices, OR pagination with stale-while-revalidate needs.

**Reversibility:** Re-litigable per slice. Adding NgRx Signal Store or TanStack Query is additive, not replacement.

**NFR / Risk links:** Step 2.2 (zoneless invariant — signal-native primitives only), Step 3 §3.4 (compile-boundary separation).

#### D4.14 — Forms & Validation: **Signal Forms + client hints + server authoritative + FormMutationConfig seam**

**Decision:** Signal Forms exclusively. Validation layered:
- **Client hints** for UX responsiveness (format, required, range) — never trusted.
- **Server authoritative** — RFC 9457 `errors[]` response walked onto Signal Forms nodes via `applyServerErrors(form, problemDetails)` helper that traverses JSON Pointers.
- **`ValidationCatalog`** as a sibling of `ErrorCatalog` (D4.9) — central registry of Transloco translation keys for validation messages. **Quality bar:** every validation key must include plain-language guidance, not just `required` / `invalid`. Enforced at PR review.
- **`FormMutationConfig` interface** (Amelia's Party Mode contribution): explicit contract `{ generateIdempotencyKey, onReset, onSuccess }` consumed by the form helper. Closes the Idempotency-Key × Signal Forms lifecycle race.
- **`Idempotency-Key` UUID v4** generated per form render, refreshed on success/reset, attached via HTTP interceptor.
- **`clientReference` field** (from D4.11 natural-key support) hidden by default in forms, defaults to the Idempotency-Key UUID.

**Form helper location:** `libs/core/form/`.

**Reversibility:** Re-litigable. Reactive Forms could be reintroduced for specific complex slices via a fallback pattern.

**NFR / Risk links:** Step 2.2 (zoneless — Signal Forms is signal-native), R#3 (validation messages must localize via Transloco), D4.9 (server-error wire format).

#### D4.15 — PWA / Offline: **No PWA at MVP** + lightweight offline banner

**Decision:** Pure online SPA. **No service worker, no manifest, no install prompt** at MVP. Browser HTTP cache + AFD edge cache (D4.19) provide all caching. Offline = hard failure with a graceful UX.

**Hard cascading commitments:**
- **Lightweight offline banner** (~40 LOC, Sally's Party Mode contribution): listens on `navigator.onLine`, RTL-aware, replaces the browser's default disconnected page with a banner + retry button. Preserves the "offline = hard failure" policy while fixing the UX cliff.

**Post-v1 roadmap note (NOT a revisit trigger):** installable manifest (`manifest.webmanifest` + 4 icons) is a **separate future feature** for v1.1+ scope, treated as a product bet to make when retention or market evidence justifies. Distinct from "revisit triggers" because the original decision was deliberate, not provisional.

**Original revisit triggers (retained for SW / full offline decisions only):**
- LCP regression on cold-start traffic from MENA mobile.
- Market feedback indicating "no offline" is a signup blocker.
- Retention data showing daily-return penalty attributable to lack of installable shell.

**Reversibility:** Re-litigable. Adding a manifest is ~2 hours of work; service worker is more involved.

**NFR / Risk links:** Step 2.2 (LCP / TTI budgets — service worker overhead is a hidden cost), Step 3 §3.4 (cache-key registry preserved for future SW use).

### 4.6 Infrastructure & Deployment (D4.16–D4.20)

#### D4.16 — Backend Hosting: **Azure Container Apps (ACA)**

**Decision:** ACA in Germany West Central. Aspire 13 deploy target via `azd`. Production: `minReplicas=1` (avoids cold-start on first MENA-morning request). Staging / dev: `minReplicas=0` (true scale-to-zero). KEDA HTTP scaler tuned to D4.6's 80rps sustained / 500rps burst envelope (cache-miss traffic only).

**Cascading commitments:**
- **Container image strategy:** build once, promote staging → prod via ACA revision (no rebuild per environment).
- **Revisions for canary-style traffic splitting** when deploys land.
- **ACA Jobs** decomposition deferred — revisit when first scheduled job exceeds 30s runtime OR more than 5 distinct scheduled jobs exist.

**Reversibility:** Re-litigable to App Service (~1 day of YAML rewrite). Re-litigable to AKS but only at significant scale.

**Revisit trigger:** if monthly compute cost exceeds $150 at steady state, OR ACA's sidecar/Dapr model constrains a slice, reconsider AKS.

**NFR / Risk links:** R#2 (autoscale absorbs burst), Step 3 §3.6 (Aspire-native deploy target).

#### D4.17 — CI/CD: **GitHub Actions + `azd` for deploys + OIDC federated credentials**

**Decision:** GitHub Actions as primary CI/CD. `azd` (Azure Developer CLI) drives deploys. **OIDC federated identity** on an Entra ID workload identity — no stored Azure secrets in GitHub.

**Hard cascading commitments — five design points:**
1. **Three workflows, not one monolith:**
   - `ci.yml` — every PR: lint + typecheck + Vitest + Playwright on affected projects (path-filter on `apps/*` and `src/**`).
   - `deploy-staging.yml` — merge to `main`: `azd deploy` to staging ACA. No approval gate.
   - `deploy-prod.yml` — manual trigger or git tag `v*.*.*`: `azd deploy` to prod ACA. **Environment protection rule: required reviewer (Wahid) + 10-minute wait timer** even for solo-dev — the wait timer alone catches impulsive deploys.
2. **Build-once promotion:** same container image flows staging → prod via ACA revision; never rebuilt.
3. **Pin actions to SHA**, not tag (`uses: azure/login@<sha>`). Dependabot handles updates. Prevents the `tj-actions/changed-files` supply-chain class of incident.
4. **Preview environments deferred.** ACA supports per-revision URLs but cost + Playwright-against-moving-target complexity isn't justified. **Revisit trigger:** add preview envs when there's >1 active collaborator OR a UX change that needs design-review on real staging.
5. **OIDC federated credential** on Entra ID workload identity — zero long-lived Azure credentials in GitHub.

**Reversibility:** Re-litigable. `azd` works identically against Azure DevOps Pipelines.

**NFR / Risk links:** Step 3 §3.4 (bundle-graph gate, OpenAPI-drift gate land in `ci.yml`), §3.7 spike runs in CI.

#### D4.18 — Region: **Germany West Central (Frankfurt)**

**Decision:** Germany West Central as primary region. Paired with Germany North for in-country DR. All Azure resources (ACA, Postgres Flexible Server, Key Vault Premium HSM, AFD origin, SWA, App Insights) in this region.

**Rationale:** Strongest GDPR/data-residency narrative for fintech marketing; mature region with full service parity including Managed HSM (load-bearing for D4.8); paired with Germany North so geo-redundant Postgres replicas would stay in-country. AFD edge layer (D4.19) eats the 15-25ms latency gap to MENA users for cache-hit traffic.

**Reversibility:** **High cost — treat as one-way door unless caught pre-launch.** Postgres migration (logical replication + cutover), ACA rebuild, Key Vault re-key ceremony, CDN origin reconfiguration, DNS — call it 1-2 engineer-weeks.

**Revisit triggers (three):**
- Real-user-monitoring shows p75 Cairo/Dubai TTI >2.5s AND the dominant factor is origin latency (not bundle, not backend) → migrate to Italy North (Milan).
- Managed HSM in Germany West Central is deprecated or hits service-parity blockers → migrate to North Europe or France Central.
- UAE Azure regions (UAE North = Dubai) gain full service parity including Premium HSM AND product pivots to MENA-primary → consider multi-region active-passive with UAE as MENA origin.

**NFR / Risk links:** Step 1 (Azure Europe hard constraint), Step 2.9 (data-lifecycle hardening — region affects data residency), R#3 (compliance perimeter).

#### D4.19 — CDN + WAF + Static Frontend Hosting: **Azure Front Door Standard + Azure Static Web Apps**

**Decision:** Single AFD Standard profile fronts both FE apps (hosted on SWA) and the ACA backend.

**Topology:**
```
app.accountantim.com           → AFD Standard → SWA (owner-workspace root)
app.accountantim.com/api/*     → AFD Standard → ACA backend (no cache; auth-sensitive)
statement.accountantim.com     → AFD Standard → SWA (public-statement root)
statement.accountantim.com/t/{token}  → AFD Standard (cached per-token) → ACA backend on miss
```

**WAF rules at Standard tier from day one:**
- OWASP core ruleset (SQLi, XSS, LFI/RFI, command injection).
- Rate-limit per client IP: **120 req/min on `app.*` paths**, **600 req/min on `statement.*/t/*`** (matches D4.6 burst ceiling + headroom).
- Geo rule: **log-only initially**; promote to allow/block when market data exists. MENA + EU monitored.
- Block requests missing expected headers on API routes (e.g., `Idempotency-Key` on POSTs to money-mutation endpoints).
- Custom rule: block paths matching capability-token probe shapes (`/t/[<6 chars]` or obviously malformed).

**Cache strategy (aligns with D4.6):**
- FE bundles: cache-forever with hashed filenames; 1yr `max-age`.
- `index.html`: short TTL (60s) so new deploys propagate.
- Public-statement responses: cache key = `{token}:{accept-language}`; TTL driven by backend `Cache-Control` header (backend owns TTL policy; edge obeys).
- **CI gate**: assert `Cache-Control` headers on `/t/*` responses match spec (prevents the silent-decay failure mode where >95% hit-rate quietly drops to 70%).

**Reversibility:** Re-litigable. AFD Standard → Premium is a toggle. AFD → Cloudflare is medium cost (DNS flip + asset re-upload).

**Revisit triggers for AFD Premium:**
- Sustained bot traffic >1000 req/hr OR any credential-stuffing pattern → Premium for bot manager.
- Multi-policy WAF need (admin-only rules different from public-statement rules) → Premium for policy-per-route.
- Need to lock ACA ingress to "only from AFD" via private endpoint → Premium.

**Revisit trigger for Cloudflare (option C from facilitation):** if RUM after launch shows p75 MENA TTFB >500ms on cache-hit traffic AND bundle/backend time is ruled out, consider Cloudflare in front of AFD.

**NFR / Risk links:** R#2 (entire decision exists for the public surface), Step 3 §3.4 (bundle-graph gate enforces per-target bundle isolation that makes single-AFD topology safe).

#### D4.20 — Observability: **Azure Monitor + Application Insights** with aggressive sampling

**Decision:** App Insights wired automatically by Aspire 13 OTel auto-instrumentation. Connection string from Key Vault.

**Sampling discipline (the actual cost decision, not the backend choice):**
- **Adaptive sampling** enabled server-side (Aspire default): caps at 5 items/sec per telemetry type.
- **Ingestion sampling at 20%** on the App Insights resource — further cost cap.
- **Force-keep rules** (never sampled): all exceptions, all 5xx responses, all requests with upstream `traceparent Sampled=1`, all Wolverine outbox failures, all auth failures.
- **Budget alert at $40/mo** (70% of $60 soft ceiling) firing to email.

**RFC 9457 `traceId` correlation:**
Backend middleware sets `traceId` in Problem Details from `Activity.Current?.TraceId` (W3C traceparent). Angular HTTP interceptor logs any 4xx/5xx response with its `traceId` to FE App Insights. One Log Analytics KQL query joins FE + BE events by `traceId` — this is the entire operational reason D4.9's taxonomy matters.

**Four dashboards (not forty):**
- **Golden signals:** p50/p95/p99 latency, error rate, throughput, saturation — for both `app.*` and `statement.*`.
- **Wolverine outbox health:** enqueued / processed / failed counts, dead-letter table size, in-flight duration p95.
- **AFD + WAF:** cache-hit ratio (tracking D4.19's >95% target — silent-decay alert), WAF block counts by rule, 4xx-at-edge rate.
- **Business liveness:** signups/day, statement-views/day, idempotency-key conflicts/day.

**Four alerts:**
- p95 backend latency >1s for 10 min → warning.
- Error rate >2% for 5 min → warning.
- Dead-letter table >50 rows → critical (Wolverine is failing).
- AFD cache-hit ratio <90% for 1 hour → warning (D4.19 SLO drift).

**Log retention:** 30 days. Operational, not audit. Audit log is a separate concern (see seam below).

**Reversibility:** Re-litigable. OTel is the abstraction; switching to Grafana Cloud (free tier 50GB/10k series/100GB) is endpoint reconfiguration + dashboard rebuild (~1 week to reproduce dashboards from scratch).

**Revisit triggers:**
- Monthly cost exceeds $75/mo at steady traffic → reassess sampling first; if already aggressive, evaluate Grafana Cloud.
- Multi-region active-active deployment → Grafana Cloud's multi-region story is easier.
- Frontend RUM depth becomes a priority → Datadog or Sentry have better FE UX than App Insights.

**Audit-log seam (deferred to Step 5/6 — flagged here):** A separate `audit_log` Postgres table with append-only constraint (trigger blocking UPDATE/DELETE) captures auth events, capability-token issuance, statement publications, money-affecting mutations. Hash-chained for tamper-evidence. Quarterly archive to immutable Blob storage in Germany West Central. Independent of operational telemetry retention.

**NFR / Risk links:** R#5 (operational visibility into audit-affecting flows — distinct from the audit log itself), Step 2.10 (rehearsals require dashboards and alerts to validate against), D4.9 (traceId correlation hard dependency).

### 4.7 Decision Impact Analysis

#### Implementation Sequence

The decisions admit a partial order driven by reversibility and dependency. Suggested build sequence:

1. **Region + Vault (D4.18, D4.8 Vault provisioning)** — provision Frankfurt resources, Vault Premium with HSM, Vault Standard for non-crypto secrets. One-way doors land first.
2. **Database (D4.1, D4.3)** — Postgres Flexible Server, EF Core migrations + idempotent trigger installer (Aspire MigrationService). Bitemporal triggers verified.
3. **Identity (D4.5)** — Keycloak deployment spike, single-realm setup, Vault SPI for signing keys, SMS SPI for MENA OTP. One-way door — gate behind the spike.
4. **Hosting + CI/CD (D4.16, D4.17)** — ACA environment, GitHub Actions workflows with OIDC, build-once promotion pipeline.
5. **CDN + WAF + Static FE (D4.19)** — AFD Standard profile, SWA for both FE apps, WAF ruleset, cache strategy with CI gate.
6. **API contract (D4.9, D4.10, D4.11)** — error model + `ErrorCatalog`, `/v1/` routing, idempotency middleware. First slice can compile against these.
7. **Wolverine (D4.12)** — Postgres transport + outbox + dead-letter table.
8. **Capability tokens (D4.6)** — schema + middleware + AFD cache-key + URL-rotation tooling. Constant-time-response tests.
9. **Crypto runtime (D4.8 envelope encryption)** — `subject_keys` table, encrypt/decrypt seam in Application layer, KEK rotation procedure.
10. **Frontend foundation (D4.13, D4.14, D4.15)** — signal services, Signal Forms helper + `FormMutationConfig`, offline banner.
11. **Observability (D4.20)** — App Insights wiring, sampling rules, four dashboards, four alerts.

Steps 1–3 must complete before step 6 can produce a working API. Steps 6–9 can interleave with step 10 once contracts exist.

#### Cross-Component Dependencies

| Dependency | Source | Sink | Nature |
|---|---|---|---|
| Bitemporal triggers | D4.3 (migrations) | D4.1 (Postgres-only constructs) | Hard — engine swap rebuilds triggers |
| `traceId` correlation | D4.9 (error model) | D4.20 (App Insights query) | Hard — both must agree on W3C traceparent |
| `Idempotency-Key` lifecycle | D4.11 (middleware) | D4.14 (`FormMutationConfig`) | Hard — front-end seam consumes back-end contract |
| `errors[]` JSON Pointer walk | D4.9 (response shape) | D4.14 (`applyServerErrors`) | Hard — pointer format must match |
| Capability token URL shape | D4.6 (token scheme) | D4.19 (cache key) | Hard — AFD route must match token URL pattern |
| Subject key destruction propagation | D4.8 (envelope encryption) | D4.6 (token invalidation) | Hard — `SubjectKeyDestroyed` event invalidates tokens |
| `ProcessingActivity` ambient | Step 3 §3.5 | D4.20 (logged property), D4.9 (trace context), D4.12 (message header) | Hard — ambient context propagates through every layer |
| Cache-Control headers | D4.6 (backend) | D4.19 (CI gate asserts) | Soft — backend owns TTL, edge obeys, CI catches drift |
| Keycloak realm + Vault SPI | D4.5 (realm signing keys) | D4.8 (Vault custody) | Hard — realm keys live in Vault via SPI |
| OIDC federated credential | D4.17 (GitHub Actions) | D4.16 (ACA deploy) | Hard — deploy workflow needs Entra workload identity |

#### Reversibility Map

| Class | Decisions | Implication |
|---|---|---|
| **One-way door (after first commit / first user / first encrypted row)** | D4.1, D4.5, D4.8, D4.18 | Highest scrutiny. These four set the foundation; all other decisions sit downstream of them. |
| **One-way door (after external clients exist)** | D4.6 (URL shape), D4.9 (domain code taxonomy), D4.10 (public-statement v1 path) | Currently re-litigable; close fast as users land. Wrap in stability commitments before launch. |
| **Re-litigable with bounded migration cost** | D4.2, D4.3, D4.4, D4.7, D4.11, D4.13, D4.14, D4.15, D4.17, D4.19, D4.20 | Each has explicit revisit triggers. Re-evaluation is healthy when triggers fire. |
| **Re-litigable per slice** | D4.4 (CQRS), D4.13 (state lib), D4.14 (Reactive Forms) | Decision can vary per feature without architectural cost. |
| **Triggered revisit (deferred, not decided)** | D4.12 (Service Bus migration on first cross-process consumer), D4.16 (ACA Jobs decomposition), D4.19 (AFD Premium upgrade) | Deferred mechanism with concrete trigger condition. |

#### Revisit Triggers Registry

A single source of truth for all revisit conditions surfaced during Step 4. Operational dashboards (D4.20) should track the metrics underlying each trigger so the team is alerted automatically when one crosses.

| Trigger | Decision | Action when fired |
|---|---|---|
| Postgres CPU >60% sustained AND cache-eligible patterns observed | D4.2 | Add Azure Cache for Redis Standard tier |
| Slice with read p95 >100ms after indexing OR read load >10x write OR shape divergence | D4.4 | Introduce CQRS projection for that slice |
| Sustained bot traffic >1000 req/hr OR credential stuffing OR backend latency degrades during burst | D4.7, D4.19 | Upgrade to AFD Premium; split rate limiting edge + app |
| Monthly compute cost >$150 at steady state OR ACA sidecar/Dapr constraint hit | D4.16 | Reconsider AKS |
| First cross-process consumer | D4.12 | Migrate Wolverine to Azure Service Bus Standard |
| First scheduled job >30s runtime OR >5 distinct scheduled jobs | D4.16 | Decompose into ACA Jobs |
| First feature needing derived state across ≥2 slices OR pagination with stale-while-revalidate | D4.13 | Reassess state library (NgRx Signal Store, TanStack Query) |
| LCP regression on MENA mobile OR market signups blocked on "no offline" OR retention penalty traceable to no installable shell | D4.15 | Reopen PWA scope |
| First external API consumer | D4.10 | Add `Sunset` headers, formalize public versioning policy |
| RUM p75 Cairo/Dubai TTI >2.5s AND origin latency dominant | D4.18 | Migrate to Italy North |
| Managed HSM deprecation in Germany West Central OR service parity blocker | D4.18 | Migrate to North Europe or France Central |
| UAE North service parity + MENA-primary product pivot | D4.18 | Consider multi-region active-passive |
| RUM p75 MENA TTFB >500ms on cache-hit AND bundle/backend ruled out | D4.19 | Consider Cloudflare in front of AFD |
| Multi-policy WAF need (per-route policies diverge) | D4.19 | Upgrade AFD Standard → Premium |
| Need to lock ACA ingress via private endpoint | D4.19 | Upgrade AFD Standard → Premium |
| Monthly App Insights cost >$75 at steady traffic | D4.20 | Reassess sampling; if already aggressive, evaluate Grafana Cloud |
| Multi-region active-active deployment | D4.20 | Reassess App Insights vs Grafana Cloud |
| Frontend RUM depth becomes priority | D4.20 | Evaluate Datadog or Sentry |
| >1 active collaborator OR UX change needing real-staging design review | D4.17 | Add ACA per-revision preview environments |

### 4.8 Cost Profile (MVP Steady-State Estimate)

Rough estimate at single-tenant MVP traffic:

| Resource | Tier | Approx. monthly cost (USD) |
|---|---|---|
| ACA backend (`minReplicas=1` prod) | Consumption | ~$30 |
| Postgres Flexible Server | B1ms / Burstable | ~$30 |
| Azure Front Door | Standard | ~$35 |
| Azure Static Web Apps | Standard | ~$9 |
| Application Insights (with sampling) | Pay-as-you-go | ~$25 |
| Key Vault Premium (HSM-backed KEK) | Premium | ~$5 |
| Key Vault Standard (secrets) | Standard | ~$1 |
| Keycloak ACA workload (`minReplicas=1`) | Consumption | ~$15 |
| Storage (audit log archive, backups) | Cool/Archive | ~$3 |
| **Total** | | **~$150 / month** |

Two cost-revisit triggers fire well below catastrophic levels: the App Insights $75/mo trigger gives early warning on telemetry sprawl; the $200 total-spend trigger from D4.16 catches accidental autoscale events.

### 4.9 Three Silent-Decay SLOs to Instrument From Day One

These three break quietly if no one watches; D4.20's dashboards must surface them:
- **AFD cache-hit ratio** (D4.19 >95% target). Decays as new query parameters multiply cache keys or backend `Cache-Control` headers regress.
- **App Insights monthly cost** (D4.20 $75 trigger). Decays as new log statements flood in with each feature shipped.
- **ACA cold-start p99 on `minReplicas=1`** (D4.16). Rare but possible if autoscaler scales down after idle; alerts on p99 >2s catch it.

### 4.10 Convergent Revisit Window

Three Step 4 triggers converge on "real users hit production": D4.15 (PWA retention data), D4.18 (MENA latency data), D4.19 (Cloudflare-if-MENA-TTFB-slow). All three need RUM data to evaluate honestly.

**Implication for the project plan:** the **first 30-day post-launch retrospective should be structured around these three questions explicitly**, not generic "how's it going." This becomes a Step 7 validation artifact — RUM dashboards land in D4.20 specifically to feed this retrospective.

### 4.11 ADR-Worthy Items Surfaced for Future Documentation

Step 4 produced four decisions whose reversibility class and downstream blast radius merit promotion to formal ADRs in a future review pass (recommended timing: after Step 6 component design closes):

- **ADR-007 — Capability-token pattern for public-statement** (D4.6): codifies the URL-as-capability semantics, the constant-time-response invariant, and the AFD-cache-key contract.
- **ADR-008 — Envelope encryption posture** (D4.8): formalizes the KEK/DEK/IV hierarchy and the erasure flow that operationalizes ADR-004.
- **ADR-009 — Keycloak self-host on ACA** (D4.5): captures the cost-curve trade and the SPI commitments (Vault, SMS, theme).
- **ADR-010 — Region as one-way door** (D4.18): records the latency-vs-residency value judgment and the three revisit triggers.

These are not added now to keep Step 4 momentum; they belong in a post-Step-6 ADR consolidation pass.

## 5. Implementation Patterns & Consistency Rules

### 5.1 Approach

Step 4 locked the *what* (technologies, decisions). Step 5 locks the *how* — the conventions that prevent two AI agents (or two future-you sessions) from making divergent choices that compound into silent inconsistency.

The locked stack from Steps 3-4 (PostgreSQL + EF Core + Wolverine + RFC 9457 + Angular 21 + Signal Forms + Transloco + vertical-slice + monorepo) already eliminates ~40% of the conflict surface. This section addresses what remains, organized by stack layer:

- **§5.2 — Batch B1: Backend Wire-Format Conventions** — externally visible, costly to reverse once external clients exist
- **§5.3 — Batch B2: Backend Code Conventions** — internal, reversible via codemod
- **§5.4 — Batch B3: Frontend Code Conventions** — internal, reversible

Each convention entry follows the same shape:

- **Convention** — the rule, with examples
- **Rationale** — *why this rule, derived from fundamentals (not "industry standard")*
- **Reversibility** — ★☆☆ (one-way door past v1) / ★★☆ (codemod-feasible) / ★★★ (trivially reversible)
- **Enforcement** — the executable gate that prevents silent decay (CI test, architecture test, value-object encapsulation, snapshot test)
- **Revisit triggers** — specific conditions that re-open this decision

---

### 5.2 Batch B1 — Backend Wire-Format Conventions

Four of six conventions in this batch are **one-way doors past v1 launch**. External clients (including Accountantim's own Angular frontend, partner integrations, future generated TypeScript contracts) will pattern-match on these formats; changing them requires API versioning. The enforcement layer (§5.2.7) is non-negotiable — silent drift here is a category of bug we cannot ship.

#### 5.2.1 Database Naming (P1)

**Convention:** All Postgres identifiers are `snake_case`, via the [`EFCore.NamingConventions`](https://github.com/efcore/EFCore.NamingConventions) package (`UseSnakeCaseNamingConvention()` on the `DbContextOptionsBuilder`).

| Aspect | Format | Example |
|---|---|---|
| Tables | plural snake_case | `users`, `journal_entries`, `idempotency_keys` |
| Columns | snake_case | `user_id`, `created_at`, `posted_at` |
| Foreign keys | `<referenced_table_singular>_id` | `user_id` (FK to `users.id`) |
| Indexes | `ix_<table>_<columns>` | `ix_journal_entries_posted_at` |
| Migrations | `YYYYMMDDHHMMSS_<PascalCaseDescription>` | `20260601120000_AddIdempotencyCacheTable` |

**Reserved-word handling:** if a noun collides with a Postgres reserved word (`order`, `user`, `group`, etc.), prefix with the slice — e.g., the `journal-entry` slice's "order" concept becomes `journal_orders`, not `orders`.

**Rationale:** Postgres folds *unquoted* identifiers to lowercase. PascalCase identifiers require double-quoting (`SELECT "UserId" FROM "Users"`) every time. AI agents writing SQL queries pattern-match from open-source training data which is overwhelmingly snake_case — they will reliably produce broken unquoted PascalCase queries. snake_case eliminates the quoting requirement entirely. (Secondary benefit: matches Postgres ecosystem tooling, ad-hoc psql/pgAdmin reconciliation queries.)

**Reversibility:** ★★☆ — possible via codemod + migration, but ugly across hundreds of tables. Decide now.

**Enforcement:**
- CI gate `tests/Infrastructure.Tests/SchemaAudit.cs` — post-migration test asserts every public-schema identifier matches `^[a-z][a-z0-9_]*$` AND is not in the Postgres reserved-word list.
- Architecture test `tests/Architecture.Tests/EntityAttributesTest.cs` — fails build if any entity uses `[Table]` or `[Column]` attributes (which would override the convention).

**Revisit triggers:**
- `EFCore.NamingConventions` becomes unmaintained (no .NET-major releases for 12+ months past a .NET LTS cut).
- A Postgres-specific feature requires identifier patterns this convention blocks.

#### 5.2.2 REST Endpoint Naming (P2)

**Convention:**

| Aspect | Format | Example |
|---|---|---|
| Versioning | URL-path via `Asp.Versioning` (per D4.10) | `/v1/...` |
| Resource case | plural `kebab-case` | `/v1/journal-entries` |
| Resource form | always plural | `/v1/users/{id}` (never `/v1/user/{id}`) |
| Path parameter | `{id}` (ASP.NET Core native syntax) | `/v1/users/{id}` |
| Query parameter | `camelCase` (cascades from §5.2.3) | `?fromDate=2026-01-01&pageSize=50` |

**Action endpoint shape — split rule:**

The action's *response shape* determines the path shape.

| When the action... | Use shape | Example |
|---|---|---|
| Creates a first-class entity with its own lifecycle (id, status, can be polled/listed/cancelled) | **Noun sub-resource** (`POST /collection/{id}/<noun-plural>`) | `POST /v1/transactions/{id}/reversals` → returns `Reversal` entity<br>`POST /v1/journal-entries/{id}/exports` → returns `Export` job<br>`POST /v1/accounts/{id}/statements` → returns `Statement` entity |
| Is a one-shot state mutation with no entity to track | **Verb sub-path** (`POST /collection/{id}/<verb>`) | `POST /v1/users/{id}/email-verification` → 204<br>`POST /v1/users/{id}/activate` → returns updated `User`<br>`POST /v1/sessions/{id}/refresh` → returns new token |

If you can't tell which side an action lives on: ask "would the client want to GET this thing later?" — yes → noun. No → verb.

**Bulk operations:** `POST /v1/<collection>:batch` (Google Cloud-style colon suffix) returning a job entity. Avoids ambiguity with `/v1/<collection>/{id}` parsing the colon as part of an `id`.

**Rationale:** kebab-case URLs are standard for human readability (browser bars, docs, error messages). Plural collections match HTTP semantics (`GET /users` reads as "get the collection"). The noun-vs-verb split prevents the 2027 inconsistency where one team picks `POST /transactions/{id}/reverse` (verb) and another picks `POST /journal-entries/{id}/exports` (noun) for what are structurally different operations.

**Reversibility:** ★☆☆ — once paths are consumed by the FE or any external client, change requires versioning. **One-way door past v1 launch.**

**Enforcement:**
- Spectral lint rule in CI: `paths/*` must match `^/v[0-9]+(/[a-z][a-z0-9-]*)+(/\{[a-z][a-zA-Z0-9]*\})?(:[a-z]+)?$` — enforces version + kebab-case + camelCase parameter + optional bulk colon-suffix.
- Snapshot test `tests/Api.Tests/OpenApiShape.cs` — Verify-style snapshot of the generated OpenAPI spec; PR that changes endpoint shapes fails CI.

**Revisit triggers:**
- First time the noun-vs-verb rule produces an ambiguous case the team can't resolve without consulting external API design references — refine the rule with the new case as a worked example.
- Adoption of GraphQL / RPC endpoints alongside REST (would require parallel conventions).

#### 5.2.3 JSON Field Casing (P5) — THE CASCADE-KING DECISION

**Convention:** `camelCase` globally on the wire, set in `Program.cs`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});
```

The same policy is wired to Swashbuckle so the generated OpenAPI spec matches runtime serialization byte-for-byte. Single source of truth in a `ConfigureJsonGlobally(IServiceCollection)` extension method; no other JSON configuration is permitted anywhere in the codebase.

**Acronym handling:** lowercase entire acronyms in property names — `iban`, `url`, `id`, `userIban` (not `userIBAN`), `iconUrl` (not `iconURL`). This matches `JsonNamingPolicy.CamelCase`'s actual behavior; deviation requires `[JsonPropertyName]` which is forbidden.

**Cascading commitments (this single decision settles three other things):**
- Query parameter casing → `camelCase` (symmetric with body)
- Generated TypeScript contracts → direct mapping with no transformer middleware
- RFC 9457 `errors[]` JSON Pointer paths → `/userId` not `/UserId` or `/user_id`

**Rationale:** JSON has no native casing convention; the wire is case-agnostic. Mapping cost falls on whichever side has weaker tooling. C# → JSON casing transformation is native and free (`JsonNamingPolicy.CamelCase`). TS → JSON casing transformation requires Zod schemas, manual mappers, or lossy `keysToCamelCase()` runtime helpers — painful at scale. Therefore JSON arrives in TS-native casing: put the work where the tooling is.

**Reversibility:** ★☆☆ — **hard one-way door** the moment v1 is consumed by anything (the FE counts).

**Enforcement:**
- Architecture test `tests/Architecture.Tests/JsonAttributesTest.cs` — fails build if `[JsonPropertyName]` appears outside an explicit allowlist (RFC-mandated field names like RFC 9457's `type`/`title`/`status`/`detail`/`instance`/`errors`).
- Per-endpoint snapshot test (Verify) — PR that changes any endpoint's JSON shape unexpectedly fails CI.
- OpenAPI lint in CI (Spectral rule): all `properties.*` keys must match `^[a-z][a-zA-Z0-9]*$`.

**Revisit triggers:**
- Major external partner integration with snake_case-only API consumers (and no willingness to transform their side) — would force per-route override exception, not global flip.

#### 5.2.4 Date and Time (P6, part 1)

**Convention:**

| Concept | C# type | Postgres type | Wire format | Example |
|---|---|---|---|---|
| Instant (a moment in time) | `DateTimeOffset` | `timestamptz` (alias for `timestamp with time zone`) | ISO-8601 with `Z` suffix | `"2026-04-20T14:30:00.000Z"` |
| Date only (no time-of-day) | `DateOnly` | `date` | ISO-8601 date | `"2026-04-20"` |
| Time only (rare; clock time without a date) | `TimeOnly` | `time` | ISO-8601 time | `"14:30:00"` |

**Forbidden:**
- `DateTime` in any DTO, entity, or domain type (use `DateTimeOffset`).
- `timestamp without time zone` columns (use `timestamptz`).
- Server-side date *formatting* for display — UI display is computed in the browser via `Intl.DateTimeFormat` with explicit `timeZone` from user profile (defaulting to `Intl.DateTimeFormat().resolvedOptions().timeZone`).

**Statement period boundaries** are computed in the user's local timezone via a single helper `MonthBoundary(userTimezone, year, month) → (utcStart, utcEnd)`, then persisted/queried as UTC. Mandatory use — no inline `DateTime` boundary arithmetic.

**Rationale:** Time has two physical realities — *instant* (frame-invariant moment) and *wall-clock context* (what some clock showed). They require different types and storage. UTC is the only timezone-stable representation across political DST changes (Egypt and Lebanon have flip-flopped DST policy in the last 5 years; Saudi Arabia/UAE don't observe DST; Lebanon, Jordan, Palestine do; Morocco runs year-round-DST-except-Ramadan — MENA timezone politics are real and unstable). `DateTimeOffset` always carries explicit offset so serialization round-trips cannot lose zone information.

**Reversibility:** ★★★ — server-side reformatting is fully reversible.

**Enforcement:**
- Architecture test — fails build if any DTO or entity uses `DateTime`.
- CI schema-audit — every column with `_at` suffix MUST be `timestamptz`; every `_date` suffix MUST be `date`.
- Property-based test for statement boundaries — for every IANA timezone in Accountantim's supported list, generate transactions at minute offsets across month boundary and assert each lands in the correct period.
- DST-window reconciliation test — assert no two transactions in the same minute when local clock duplicates an hour (October DST in EU; relevant for Lebanon/Jordan/Palestine in MENA).

**Revisit triggers:**
- A jurisdiction we operate in changes timezone policy mid-year (per MENA precedent) — verify transition handling.
- Need for sub-second precision in storage (high-frequency reconciliation feeds) — `timestamptz` supports microseconds, but verify roundtrip with `DateTimeOffset`.

#### 5.2.5 Money (P6, part 2) — THE FINTECH-CRITICAL CONVENTION

**Convention:** Every money value on the wire is a JSON object with two fields:

```json
{ "amount": "1234.56", "currency": "EUR" }
```

| Aspect | Convention |
|---|---|
| `amount` field | JSON string (never JSON number); decimal-string with currency-appropriate precision |
| `currency` field | ISO 4217 three-letter code, uppercase (`"EUR"`, `"AED"`, `"KWD"`, `"USD"`) |
| C# domain type | `Money` value object — `record Money(decimal Amount, string Currency)` with constructor validation against `CurrencyRegistry` |
| Postgres storage | `amount numeric(19, 4) not null, currency char(3) not null check (currency ~ '^[A-Z]{3}$')` |
| Frontend math | MUST go through `libs/core/money` helpers (wraps `decimal.js`) — `addMoney(a, b)`, `formatMoney(m, locale)`, `parseMoney(input, currency)`. Raw `+`/`-`/`parseFloat` on amount values is ESLint-forbidden. |

**`CurrencyRegistry`** is the single source of truth — currency code → minor units. Initial set: EUR (2), USD (2), GBP (2), AED (2), SAR (2), EGP (2), JOD (3), KWD (3), BHD (3), TND (3). All server-side validation runs through this registry.

**Rationale:** Money is a count of units of a currency. Three exactness requirements — unit count must be exact (excludes IEEE-754 floats), encode→decode→encode must round-trip, decoder must know precision. Decimal-string is self-describing for precision (count of digits after dot) and handles arbitrary scale natively. Stripe-style minor-units integers require every consumer to maintain a currency→minor-units table that breaks when ISO 4217 updates or when crypto adds new tokens; for a solo developer serving future bilingual partner integrations, decimal-string self-describes and removes the per-consumer-table maintenance burden. The frontend math disadvantage of decimal-string is mitigated by the mandatory `decimal.js` helper convention (see revisit trigger below).

**Reversibility:** ★☆☆ — **one-way door**.

**Enforcement:**
- Architecture test — every Postgres column with `_amount` suffix MUST be `numeric(19, 4)`. CI gate.
- Architecture test — every C# DTO money field MUST be `Money` value object type, never raw `decimal`.
- ESLint rule (frontend) — `parseFloat`/`parseInt` on any field typed as `Money`/amount is a build error; raw arithmetic operators on `Money` field values are a build error.
- Property-based round-trip test (FsCheck) — for every currency in `CurrencyRegistry`, generate random amounts at currency's max precision; assert serialize → deserialize → arithmetic → serialize is exact.

**Revisit triggers:**
- **Frontend `decimal.js` helper enforcement erodes** (lint rule disabled, raw `+` on amount values found in PR review, `parseFloat` on amount fields appears in commits) — money wire format must be re-litigated; without the helper, minor-units integer is the better choice. *This coupling is load-bearing.*
- **First crypto-asset support requirement** — migrate `_amount` columns from `numeric(19, 4)` to `numeric(38, 18)` or unbounded `numeric`; update `Money` value object precision rules in `CurrencyRegistry`; update schema-audit CI test.
- A jurisdiction adopts a currency we don't have in `CurrencyRegistry` — add to registry with correct minor-units count; redeploy.

#### 5.2.6 Domain Error Code Taxonomy (P7)

**Convention:** Every error code in the `ErrorCatalog` registry (per D4.9) matches the structure `accountantim.<slice>.<condition>` with all segments in `snake_case`:

```
accountantim.journal_entry.duplicate_idempotency_key
accountantim.journal_entry.unbalanced_debits_credits
accountantim.account.insufficient_funds
accountantim.user.email_already_registered
accountantim.capability_token.expired
accountantim.public_statement.not_found
```

Each catalog entry has additional fields:

| Field | Purpose |
|---|---|
| `code` (string) | Human-friendly identifier shown above |
| `id` (GUID) | Immutable cross-version anchor; emitted in RFC 9457 response as `extensions.codeId` |
| `httpStatus` (int) | HTTP status to return (400, 404, 409, 422, ...) |
| `transloco_key` (string) | Localization key for user-facing message |
| `is_externally_visible` (bool) | If true, code rename requires versioning ADR + transitional alias period |
| `aliases` (string[]) | Predecessor codes during deprecation window |

**Rationale:** Snake_case segments are predictable under simple regex (`[a-z_]+`) — friendly to log search, monitoring rules, partner integration pattern-matching, and AI-agent debugging. Hierarchical structure (vs flat identifiers like Stripe's `charge_already_refunded`) aligns with the vertical-slice architecture (per ADR-006): the slice owns its codes and the namespace makes ownership visible. The brand prefix `accountantim.` prevents collision with third-party error catalogs (Keycloak, Wolverine, Postgres SQLSTATE codes) when those bubble up through logs.

**Reversibility:** ★☆☆ — once external clients (or your own partner integrations) pattern-match on these strings, the *code* is a contract. The `id` GUID is the immutable cross-version anchor; codes can be aliased during versioning windows.

**Enforcement:**
- Architecture test (NetArchTest) — every error code matches `^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$`, length ≤ 64 chars, has unique `id` GUID.
- Catalog snapshot test — PR that removes or renames an externally-visible code without an `aliases` entry fails CI.
- Slice-rename governance — renaming a slice that has externally-visible error codes requires an ADR documenting the alias period (minimum 6 months; extends one major version).

**Revisit triggers:**
- Adoption of OpenTelemetry's `error.type` semantic convention as primary error identifier (would shift `code` from primary to legacy field).
- Catalog growth past ~200 entries — re-evaluate whether 3-segment hierarchy is still sufficient.

#### 5.2.7 Enforcement Layer Summary

The conventions above are policed by four categories of executable gate. Silent decay is impossible because divergence becomes a failed PR, not a future bug report.

| Category | What it does | Where it lives |
|---|---|---|
| **CI schema-audit gates** | Asserts post-migration schema shape matches conventions: snake_case identifiers, `_at`→`timestamptz`, `_date`→`date`, `_amount`→`numeric(19, 4)`, currency `char(3)` with regex check | `tests/Infrastructure.Tests/SchemaAudit.cs` (xUnit, runs against ephemeral Postgres in CI) |
| **Architecture tests** | Forbids attribute-based overrides that bypass conventions: no `[Table]`/`[Column]`/`[JsonPropertyName]` outside allowlist; no `DateTime` in DTOs; every money field is `Money` value object; every error code matches taxonomy regex with unique `id` | `tests/Architecture.Tests/*.cs` (NetArchTest) |
| **Value-object encapsulation** | Makes the wrong choice impossible at the type level: `Money` value object with currency-aware validation; `MonthBoundary` helper; single `ConfigureJsonGlobally` extension | `libs/core/Domain/Money.cs`, `libs/core/Domain/CurrencyRegistry.cs`, `libs/core/Time/MonthBoundary.cs`, `Accountantim.Api/Configuration/JsonConfiguration.cs` |
| **Snapshot + property-based tests** | Catches behavioral drift the static checks can't see: Verify-style JSON snapshots per endpoint, OpenAPI spec snapshot, FsCheck round-trip for Money across all currencies, timezone month-boundary fuzz across IANA zones | per-slice `*.Tests` projects + frontend `libs/core/money/__tests__` |

**Lint layer (Spectral for OpenAPI, ESLint for TypeScript):**
- Spectral CI rule: all `paths/*` match the kebab-case + camelCase regex; all `properties.*` match `^[a-z][a-zA-Z0-9]*$`.
- ESLint custom rule (frontend): `parseFloat`/`parseInt`/raw arithmetic on any value typed as `Money` is a build error.

**Estimated one-time scaffolding cost:** ~6-8 hours of work to set up the gates above. After that, every future slice receives enforcement for free — critical for solo-developer workflow where per-PR review is not available.

---

### 5.3 Batch B2 — Backend Code Conventions (Internal, Reversible)

C# language baseline (PascalCase types, camelCase locals, `_camelCase` private fields, `Async` suffix on async methods, `I` prefix on interfaces, `record` for value-objects/DTOs) follows .NET community norms enforced by Roslyn analyzers — not re-stated here. This section covers what's still ambiguous given the FastEndpoints + Wolverine stack.

All conventions in this batch are ★★☆ (codemod-feasible) or ★★★ (trivially reversible). Enforcement is correspondingly lighter than B1.

#### 5.3.1 DTO and Endpoint Naming (P3)

**Pattern:** `{Verb}{Noun}{Role}` where verb is the HTTP-action analogue (`Create`, `Get`, `List`, `Update`, `Delete`, plus domain verbs like `Reverse`, `Post`, `Approve`).

| Component | Naming | Notes |
|---|---|---|
| Endpoint class | `{Verb}{Noun}Endpoint` | Inherits `Endpoint<TRequest, TResponse>` (FastEndpoints) |
| Request DTO | `{Verb}{Noun}Request` (sealed record) | "Request" not "Command" — D4.4 deferred CQRS |
| Response DTO | `{Verb}{Noun}Response` (sealed record) | "Response" not "Result" or "Dto" — symmetric with Request |
| Validator | `{Verb}{Noun}RequestValidator` | `AbstractValidator<{Verb}{Noun}Request>` (FluentValidation) |
| Mapper | static method `{Verb}{Noun}Response.From(entity)` | No named mapper class; static factory |
| Handler | `{Verb}{Noun}Handler` | Wolverine handler; `Handle` method (Wolverine discovery) |
| Domain event | `{Noun}{PastTenseVerb}Event` (sealed record) | `JournalEntryCreatedEvent`, `JournalEntryReversedEvent` — past tense always |

**Worked example** for `journal-entries` create:

```csharp
// Slices/JournalEntries/Create/CreateJournalEntryEndpoint.cs

public sealed record CreateJournalEntryRequest(
    DateOnly TransactionDate,
    Money Amount,
    string Description,
    Guid AccountId);

public sealed record CreateJournalEntryResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Status)
{
    public static CreateJournalEntryResponse From(JournalEntry entry) =>
        new(entry.Id, entry.CreatedAt, entry.Status.ToString());
}

public sealed class CreateJournalEntryRequestValidator
    : AbstractValidator<CreateJournalEntryRequest>
{
    public CreateJournalEntryRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).NotNull();
        // ... domain rules
    }
}

public sealed class CreateJournalEntryEndpoint
    : Endpoint<CreateJournalEntryRequest, CreateJournalEntryResponse>
{
    public override void Configure()
    {
        Post("/v1/journal-entries");
        // auth, idempotency middleware, etc.
    }

    public override async Task HandleAsync(
        CreateJournalEntryRequest req,
        CancellationToken ct)
    {
        // delegate to Wolverine handler or domain service
    }
}
```

**Reversibility:** ★★☆ (codemod-feasible).

**Enforcement:**
- Architecture test (NetArchTest): every endpoint class inherits from `Endpoint<,>`; every handler class declares a `Handle` method (Wolverine discovery requirement).

#### 5.3.2 Slice-Internal Organization (P8)

**Slice granularity:** one folder per action. One Action = one slice = one folder. Each slice contains everything needed for that action — endpoint, DTOs, validator, handler, domain events, slice-private helpers.

**File-per-type, not file-per-feature.** Each public type lives in its own file. Smaller diffs, fewer merge conflicts, IDE navigation matches type search.

**Folder layout:**

```
Slices/
  JournalEntries/
    Create/
      CreateJournalEntryEndpoint.cs        # FastEndpoints class
      CreateJournalEntryRequest.cs         # Request DTO
      CreateJournalEntryResponse.cs        # Response DTO
      CreateJournalEntryRequestValidator.cs # FluentValidation
      CreateJournalEntryHandler.cs         # Wolverine handler
      CreateJournalEntryEvents.cs          # Domain events emitted
    Reverse/
      ReverseJournalEntryEndpoint.cs
      ...
    List/
      ListJournalEntriesEndpoint.cs
      ListJournalEntriesQuery.cs
      ListJournalEntriesResponse.cs
      ...
  Accounts/
    Create/
      ...
```

**Wolverine handler conventions** (Wolverine has discovery rules — deviate at your peril):

- Handler class: `public class {Action}{Noun}Handler`
- Handler method: `public async Task<TResponse> Handle({TRequest} req, ...)`
- Method name **must be `Handle`** (Wolverine discovery breaks if renamed)
- One handler per command/query — no "god handlers" with multiple `Handle` overloads

**Cross-slice helpers:** if a helper is needed in 2+ slices, promote it to `libs/core/` (per ADR-006 vertical-slice rule). Slice-private helpers stay in the slice folder as `internal` types.

**Reversibility:** ★★☆ (codemod-feasible up to ~50 slices; tedious past that).

**Enforcement:**
- Convention check (CI script, **warning** not error in first 3 months): flag slices not matching `{Verb}{Noun}{Role}.cs` file pattern. Promotes to error after MVP launch.
- Architecture test: types in `Slices/X/` cannot reference types in `Slices/Y/` (other than through `libs/core/`). Vertical-slice enforcement.

#### 5.3.3 Test Conventions (P9-backend)

**Test project structure:** one test project per app, mirroring slice paths.

```
tests/
  Accountantim.Api.Tests/
    Slices/
      JournalEntries/
        Create/
          CreateJournalEntryHandlerTests.cs        # Unit
          CreateJournalEntryRequestValidatorTests.cs  # Unit
          CreateJournalEntryEndpointTests.cs       # Integration
  Accountantim.Worker.Tests/
    ...
  Accountantim.Architecture.Tests/                  # NetArchTest, schema-audit, etc.
```

**Class and file naming:** `{ClassNameUnderTest}Tests.cs`.

**Method naming:** `Method_Condition_ExpectedResult`. Reads naturally in test runner output, greppable by behavior.

```csharp
public class CreateJournalEntryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidRequest_ReturnsCreatedResponse() { ... }

    [Fact]
    public async Task Handle_WithDuplicateIdempotencyKey_Returns409Conflict() { ... }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Handle_WithMissingDescription_FailsValidation(string description) { ... }
}
```

**Integration test split:** integration tests live in the same test project as units, distinguished by `[Trait("Category", "Integration")]`. CI runs units first, integration second; locally `dotnet test --filter "Category!=Integration"` runs the fast subset.

Integration tests use `WebApplicationFactory<Program>` + Testcontainers Postgres. Each test class gets a fresh database (cheap with Testcontainers parallel); no test isolation via transactions (too easy to accidentally test the transaction infrastructure instead of the slice).

**Tooling baseline:**

| Tool | Purpose |
|---|---|
| **xUnit** | Test runner |
| **FluentAssertions** | Chained assertion syntax (counts as one logical assertion) |
| **NSubstitute** | Mocking — terse syntax, plays well with `record` types |
| **Testcontainers** | Real Postgres for integration tests |
| **Verify** | Snapshot tests (used in B1 for OpenAPI + per-endpoint JSON) |
| **FsCheck** | Property-based tests (used in B1 for Money round-trip) |

**Forbidden:**
- `Moq` (commercial-license drama in 2023; NSubstitute is the replacement).
- Test isolation via shared `IDisposable` fixtures across test classes (collection-level fixtures hide order dependencies).
- Tests that mock the database (per Wahid's standing rule — integration tests must hit real Postgres via Testcontainers).

**Reversibility:** ★★★ (test renames are IDE-trivial across the board).

**Enforcement:** documented but not machine-enforced — over-engineering for ★★★ reversibility. Code review catches drift.

#### 5.3.4 Enforcement Layer Summary for B2

| Category | What it does | Where it lives |
|---|---|---|
| **Architecture tests** | Endpoint classes inherit `Endpoint<,>`; handlers have `Handle` method; cross-slice references forbidden (vertical-slice enforcement) | `tests/Accountantim.Architecture.Tests/` (NetArchTest) |
| **Convention check (warning)** | Flags slices not matching `{Verb}{Noun}{Role}.cs` file pattern; warning in first 3 months, error after MVP launch | CI script (`tools/check-slice-conventions.ps1`) |
| **Documentation only** | Test method naming pattern, mocking library choice, integration-vs-unit categorization | This document; code review enforces |

Estimated one-time scaffolding cost for B2 enforcement: ~2 hours (architecture tests + convention check script). Substantially lighter than B1 because reversibility is higher.

---

### 5.4 Batch B3 — Frontend Code Conventions (Internal, Reversible)

TypeScript language baseline (PascalCase types, camelCase identifiers, no `any` without explicit `// eslint-disable`, `readonly` on inputs, `const` default) follows `@angular-eslint/recommended` plus standard TS conventions — not re-stated here. This section covers Angular 21-specific choices that the official style guide leaves open or has recently shifted on.

All conventions in this batch are ★★☆ (codemod-feasible via Angular Schematics) or ★★★ (trivially reversible).

#### 5.4.1 Angular Naming and Style-Guide Choices (P4)

**Three Angular 21 style-guide shifts resolved up-front:**

| Shift | Old (pre-v17) | New (v17+) | Accountantim choice |
|---|---|---|---|
| Component class suffix | `JournalEntryListComponent` | `JournalEntryList` | **Keep `Component` suffix** — explicit > terse for AI-agent code generation; matches existing community code; trivially reversible |
| Component file suffix | `journal-entry-list.component.ts` | `journal-entry-list.ts` | **Keep `.component.ts`** — file-grep distinguishes components from services from helpers at a glance |
| Standalone | `@NgModule` registration | `standalone: true` (now default) | **Standalone always** — no `NgModule` files anywhere; matches Angular 21 default |

**Selector prefixes per app/lib** (two-letter, short for HTML readability, no collision with PrimeNG's `p-` or Tailwind utilities):

| Scope | Prefix | Example |
|---|---|---|
| `apps/owner-workspace` | `ow-` | `<ow-journal-entry-list>` |
| `apps/public-statement` | `ps-` | `<ps-statement-card>` |
| `libs/design-system` | `ds-` | `<ds-button>`, `<ds-money-input>` |
| `libs/domain-ui` | `du-` | `<du-account-picker>`, `<du-money-display>` |
| `libs/core` | (no components — services only) | n/a |

**File and class naming** (kebab-case files throughout):

| Type | File pattern | Class/symbol pattern | Example |
|---|---|---|---|
| Component | `{name}.component.ts` | `{Name}Component` | `journal-entry-list.component.ts` → `JournalEntryListComponent` |
| Service | `{name}.service.ts` | `{Name}Service` | `current-user.service.ts` → `CurrentUserService` |
| Directive | `{name}.directive.ts` | `{Name}Directive` | `rtl-aware.directive.ts` → `RtlAwareDirective` |
| Pipe | `{name}.pipe.ts` | `{Name}Pipe` | `money-format.pipe.ts` → `MoneyFormatPipe` |
| Guard (functional) | `{name}.guard.ts` | `{name}Guard` (camelCase function) | `auth.guard.ts` → `authGuard` |
| Resolver (functional) | `{name}.resolver.ts` | `{name}Resolver` (camelCase function) | `account.resolver.ts` → `accountResolver` |
| Type/interface | `{name}.model.ts` | `{Name}` (no suffix) | `journal-entry.model.ts` → `JournalEntry` |
| Constants | `{name}.constants.ts` | `UPPER_SNAKE_CASE` exports | `currency.constants.ts` → `SUPPORTED_CURRENCIES` |

**Signal naming convention:** plain camelCase, no suffix.

| Style | Example | Verdict |
|---|---|---|
| **Plain camelCase** (recommended) | `count`, `journalEntries`, `selectedAccountId` | Matches Angular team's official examples in v17+ docs; reads naturally in templates (`{{ count() }}`) |
| `$` suffix (RxJS-era muscle memory) | `count$` | **Forbidden** — signals are not observables; conflating the two primitives via shared suffix creates confusion |
| `Signal` suffix | `countSignal` | Verbose; nobody in the Angular community does this |

Distinguishability between signals and plain values lives at the *type signature* (`count: Signal<number>` vs `count: WritableSignal<number>` vs `count: number`), not the name. IDE/TS tooling does the work.

**Computed signals:** same naming as signals (`totalAmount`, not `totalAmountComputed`). Distinguishable by `Signal<T>` (read-only) vs `WritableSignal<T>` type.

**Effects:** named function or arrow assigned to a variable describing what the effect *does* — `logCurrentUser`, `syncSelectedAccountToUrl`. Never just `effect1` or anonymous.

**Reversibility:** ★★☆ (Angular Schematics can rename selectors/files at scale).

#### 5.4.2 Test Conventions (P9-frontend)

**Vitest (unit + component tests):**

| Aspect | Convention |
|---|---|
| File location | Colocated next to source: `journal-entry-list.component.spec.ts` |
| File suffix | `.spec.ts` (matches Angular CLI default) |
| Describe block | `describe('JournalEntryListComponent', () => { ... })` — class name as description |
| Test naming | `it('should {behavior} when {condition}')` — reads as English sentence in test runner output |
| Component testing library | **Angular Testing Library** (`@testing-library/angular`) — query by user-visible behavior, not internal structure; resilient to refactoring |
| TestBed usage | Only when Testing Library can't reach the API surface (rare); prefer Testing Library renders |
| Mocking | **Vitest's `vi.fn()` / `vi.mock()`** — Vitest built-ins are sufficient; no separate mocking library |
| Signal testing | Read signals directly (`fixture.componentInstance.count()`); use `TestBed.tick()` / `flushEffects()` for effects |
| Forbidden | `karma`, `jasmine` (legacy Angular defaults — Vitest replaces both) |

**Playwright (E2E):**

| Aspect | Convention |
|---|---|
| File location | `e2e/{flow}/{scenario}.spec.ts` — separate directory at app root, not colocated |
| File naming | `{user-flow}.spec.ts` — e.g., `e2e/journal-entries/create-journal-entry.spec.ts` |
| Test structure | `test.describe('Create Journal Entry Flow', () => { test('user can ...', ...) })` |
| Page Object Model | Use POMs for flows spanning 3+ pages; inline locators for short flows |
| Fixtures | Playwright's built-in fixtures (`test.extend`) — no Cucumber, no separate framework |
| Run target | `apps/owner-workspace` and `apps/public-statement` get separate E2E suites |
| Browser matrix | Chromium (primary) + WebKit (Safari coverage for iOS users in MENA); Firefox optional in CI |

**Reversibility:** ★★★ (test renames are IDE-trivial).

#### 5.4.3 Loading State Pattern (P10)

`rxResource()` and `resource()` (Angular 21) return a `ResourceRef<T>` with a built-in `status` signal:

```typescript
type ResourceStatus = 'idle' | 'loading' | 'reloading' | 'resolved' | 'error' | 'local';
```

**Convention: direct consumption with `@switch` in templates.** No wrapper structural directive.

```typescript
@Component({
  selector: 'ow-journal-entries-page',
  template: `
    @switch (entries.status()) {
      @case ('loading') { <ds-skeleton type="table" rows="5" /> }
      @case ('reloading') { <ds-skeleton type="table" rows="5" overlay /> }
      @case ('error') { <ds-error-state [error]="entries.error()" (retry)="entries.reload()" /> }
      @case ('resolved') {
        @if (entries.value()?.length) {
          <ow-journal-entry-list [entries]="entries.value()!" />
        } @else {
          <ds-empty-state icon="file" titleKey="journalEntries.empty.title" descriptionKey="journalEntries.empty.description" />
        }
      }
    }
  `
})
class JournalEntriesPageComponent {
  entries = rxResource({
    request: () => this.filters(),
    loader: ({ request }) => this.api.listJournalEntries(request)
  });
}
```

**Mandatory cases in `@switch`:** every component using `rxResource` MUST handle `'loading'`, `'reloading'`, `'error'`, and `'resolved'`. The `'resolved'` case MUST further branch on empty/non-empty when the value is a collection. The `'idle'` and `'local'` cases are typically not surfaced to UI (idle is pre-fetch transient, local is for client-side resources).

**Design-system components** that back this pattern (live in `libs/design-system`):

| Component | Purpose |
|---|---|
| `<ds-skeleton type="..." rows="..." overlay?>` | Composed skeletons; wraps PrimeNG `<p-skeleton>` for primitives |
| `<ds-error-state [error]="..." (retry)="...">` | Accepts `RFC9457Problem \| Error`; renders user-friendly localized message via Transloco + ValidationCatalog (per D4.14); shows technical details collapsed; emits `(retry)` if reloadable |
| `<ds-empty-state icon titleKey descriptionKey [action]>` | Localized via Transloco; never blank-space; supports CTA button via `[action]` input |

**Loading state for non-resource async work** (form submissions, mutations not via `rxResource`): use a `pending` signal pattern.

```typescript
class CreateJournalEntryComponent {
  pending = signal(false);

  async onSubmit() {
    this.pending.set(true);
    try {
      await this.api.create(this.form.value);
      this.router.navigate(['/journal-entries']);
    } finally {
      this.pending.set(false);
    }
  }
}
```

Standardized signal name: **`pending`** (not `loading`, not `submitting`, not `isLoading`). Matches Signal Forms' own pending-state nomenclature (per D4.14).

**Forbidden:**
- Boolean signals named `loading`, `submitting`, `isLoading`, `isPending` — use `pending` consistently. Drift here creates the exact "two agents pick different names" problem this section exists to prevent.
- `@if (entries.status() === 'loading')` chains for resource state — use `@switch` for exhaustiveness signaling.
- Wrapper structural directives like `*dsResource` for the four-state pattern — fights Angular 21's direction toward less template magic.

**Reversibility:** ★★★ (component-level pattern; trivially refactorable).

#### 5.4.4 Enforcement Layer Summary for B3

| Category | What it does | Where it lives |
|---|---|---|
| **ESLint custom rule** | Forbid `$` suffix on `Signal<T>` / `WritableSignal<T>` typed values (catches RxJS-era muscle memory) | `tools/eslint-rules/no-dollar-suffix-on-signals.ts` |
| **ESLint custom rule** | Forbid boolean signals named `loading`/`submitting`/`isLoading`/`isPending` — must be `pending` | `tools/eslint-rules/standardize-pending-signal.ts` |
| **`@angular-eslint/component-selector`** | Enforce selector prefix per project (`ow-` / `ps-` / `ds-` / `du-`) | `eslint.config.js` per project |
| **Convention check (CI script, warning)** | Grep `apps/*/src/**/*.component.ts` for `@switch (...status()` patterns; flag missing `'loading'`/`'reloading'`/`'error'`/`'resolved'` cases | `tools/check-resource-exhaustiveness.ts` |
| **Documentation only** | Test method naming pattern, Page Object Model usage threshold, design-system component usage for error/empty states | This document; code review enforces |

Estimated one-time scaffolding cost for B3 enforcement: ~3 hours (2 custom ESLint rules + convention-check script + project ESLint configs). Together with B2's ~2 hours and B1's ~6-8 hours: **~12 hours total one-time investment** to make Step 5's conventions executable across the stack.

---

### 5.5 Step 5 Summary

Step 5 produced **10 conventions across 3 batches** (B1: 6, B2: 3, B3: 3) governing wire format, code organization, and frontend patterns. Of these:

- **4 are one-way doors past v1 launch** (P2 REST paths, P5 JSON casing, P6 Money, P7 error code taxonomy) — these required pre-mortem + first-principles scrutiny in §5.2.
- **6 are reversible** (codemod-feasible or trivially reversible) — these had lighter facilitation in §5.3 / §5.4.

The **enforcement layer** (CI gates, architecture tests, value-object encapsulation, snapshot tests, ESLint rules) makes silent decay impossible: convention drift becomes a failed PR, not a future bug report. **~12 hours total scaffolding cost** for the full enforcement layer.

**Three new revisit triggers added in Step 5** (extending Step 4's Revisit Triggers Registry):

| # | Trigger | Action |
|---|---|---|
| RT-21 | Frontend `decimal.js` money-helper enforcement erodes (lint disabled, raw `+` on amounts found in PR) | Re-litigate P6 money wire format — without the helper, minor-units integer becomes the better choice |
| RT-22 | First crypto-asset support requirement | Migrate `_amount` columns from `numeric(19, 4)` to `numeric(38, 18)` or unbounded; update `Money` value object precision rules; update schema-audit gate |
| RT-23 | Catalog growth past ~200 error codes | Re-evaluate whether the 3-segment hierarchy `accountantim.<slice>.<condition>` is still sufficient |

---

## 6 Project Structure & Boundaries

### 6.1 Approach

Step 6 materializes Steps 3-5 into a concrete, walkable tree. Content organized in four batches:

- **S1 — Repo top-level layout**: root folders, shared config files, how the two stacks coexist in one repo
- **S2 — Backend tree**: .NET solution layout, project graph, vertical-slice internal structure, migrations, tests
- **S3 — Frontend tree**: Angular workspace, apps/libs layout, routing, testing, assets
- **S4 — Cross-cutting**: CI/CD workflows, Docker/infra, docs, contracts-generation pipeline, epic-to-directory mapping

**Step 6 intake decisions (locked before drafting):**

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Repo topology | Single git repo, backend + frontend in sibling directories | Generated TS contracts pipeline writes directly from .NET OpenAPI emitter into `frontend/libs/api-contracts/` — a cross-repo equivalent would require npm publish or a git submodule |
| 2 | Frontend workspace tool | Angular CLI multi-project workspace | 2 apps × 3 libs footprint; Nx payoff starts at ~5+ projects (revisit trigger RT-25) |
| 3 | .NET solution layout | Classic layered `.sln` (AppHost + ServiceDefaults + Api + Domain + Infrastructure) | Aspire is orchestrator, not layout driver; vertical slices live inside Api project as folders |
| 4 | Migration organization | Single `Migrations/` folder at EF project root (EF Core default) | Slice-grouped migrations fight `dotnet ef` tooling; the schema-audit gate from Step 5 already enforces cross-slice isolation at the table level |

### 6.2 Batch S1 — Repo Top-Level Layout

#### 6.2.1 Root Tree

```
accountantim/
├── backend/                      # .NET 10 solution root
│   ├── Accountantim.sln
│   ├── global.json               # .NET SDK pin (e.g. "10.0.100")
│   ├── Directory.Build.props     # shared MSBuild props, analyzers, nullable, warnings-as-errors
│   ├── src/                      # detail in S2
│   └── tests/                    # detail in S2
│
├── frontend/                     # Angular 21 workspace root
│   ├── angular.json              # CLI workspace; sourceRoot overrides to apps/ + libs/
│   ├── package.json              # npm workspace root, dev scripts
│   ├── package-lock.json
│   ├── tsconfig.json             # path aliases (@ds/*, @core/*, @du/*, @contracts/*)
│   ├── tsconfig.app.json
│   ├── tsconfig.spec.json
│   ├── eslint.config.js          # flat config v9 + custom rules from Step 5 B3
│   ├── vitest.config.ts          # workspace-wide base; per-project overrides
│   ├── playwright.config.ts      # Chromium + WebKit
│   ├── tailwind.config.ts        # Tailwind 4, scans apps/** + libs/**
│   ├── apps/                     # detail in S3
│   └── libs/                     # detail in S3
│
├── contracts/                    # reserved for hand-authored OpenAPI overlays (empty initially)
│
├── infra/                        # detail in S4
│   ├── docker-compose.yml        # local Postgres + seed
│   ├── Dockerfile.api
│   ├── Dockerfile.spa
│   └── azure/                    # Bicep / deployment
│
├── docs/                         # detail in S4
│   ├── adrs/                     # ADR-001 .. ADR-010+
│   ├── architecture.md           # this file, eventually relocated here
│   └── runbooks/
│
├── scripts/                      # detail in S4
│   ├── gen-contracts.ps1         # regenerate frontend/libs/api-contracts
│   └── seed-dev-db.ps1
│
├── .github/
│   └── workflows/                # detail in S4
│
├── .claude/                      # Claude Code skills + subagents (already present)
├── .editorconfig                 # governs entire repo (both stacks respect it)
├── .gitignore                    # unified: bin/, obj/, node_modules/, dist/
├── .gitattributes                # text=auto eol=lf + binary markers for fonts/images
├── README.md                     # prereqs → clone → docker-compose up → dotnet run → npm start
├── LICENSE
└── CLAUDE.md                     # AI-agent instructions (if present)
```

#### 6.2.2 Root Config File Inventory

| File | Location | Purpose | Scope |
|---|---|---|---|
| `.editorconfig` | repo root | Tabs/spaces, newlines, charset | Whole repo |
| `.gitignore` | repo root | Unified ignore for both stacks | Whole repo |
| `.gitattributes` | repo root | `* text=auto eol=lf` + binary markers | Whole repo |
| `README.md` | repo root | Quickstart + architecture pointer | Whole repo |
| `global.json` | `backend/` | .NET SDK pin | Scoped — frontend devs not blocked on .NET install |
| `Directory.Build.props` | `backend/` | `<LangVersion>latest</LangVersion>`, nullable, warnings-as-errors, shared analyzers | All .NET projects |
| `package.json` | `frontend/` | npm workspace root, shared devDeps, `gen:contracts` / `test` / `e2e` scripts | Frontend only |
| `angular.json` | `frontend/` | CLI workspace; `sourceRoot` overrides relocate to `apps/` + `libs/` | Frontend only |
| `eslint.config.js` | `frontend/` | Flat v9 + `@angular-eslint` + custom selector-prefix / signal-naming rules (Step 5 B3) | Frontend only |
| `tsconfig.json` | `frontend/` | Path aliases `@ds/*`, `@core/*`, `@du/*`, `@contracts/*` | Frontend only |
| `vitest.config.ts` | `frontend/` | Workspace base; per-project overrides | Frontend only |
| `playwright.config.ts` | `frontend/` | Chromium + WebKit projects | Frontend only |
| `tailwind.config.ts` | `frontend/` | Tailwind 4, scans `apps/**` + `libs/**` | Frontend only |

#### 6.2.3 Angular Workspace Path Customization

Angular CLI default puts apps AND libs under `projects/`. We override `sourceRoot` in `angular.json` to preserve Step 3's naming:

```json
{
  "projects": {
    "owner-workspace":  { "projectType": "application", "root": "apps/owner-workspace",  "sourceRoot": "apps/owner-workspace/src" },
    "public-statement": { "projectType": "application", "root": "apps/public-statement", "sourceRoot": "apps/public-statement/src" },
    "design-system":    { "projectType": "library",     "root": "libs/design-system",    "sourceRoot": "libs/design-system/src" },
    "core":             { "projectType": "library",     "root": "libs/core",             "sourceRoot": "libs/core/src" },
    "domain-ui":        { "projectType": "library",     "root": "libs/domain-ui",        "sourceRoot": "libs/domain-ui/src" },
    "api-contracts":    { "projectType": "library",     "root": "libs/api-contracts",    "sourceRoot": "libs/api-contracts/src" }
  }
}
```

The `libs/api-contracts` library is **generated** from backend OpenAPI emission (pipeline detail in S4) and **committed to source control** — not gitignored — so schema changes surface in PR diffs for review and CI determinism is preserved without regeneration as a critical-path step.

#### 6.2.4 Why Not Root-Level `apps/ + libs/`

A naive monorepo-idiomatic layout flattens both stacks under root-level `apps/` and `libs/`:

```
# REJECTED
accountantim/
├── apps/
│   ├── api/              # .NET
│   ├── owner-workspace/  # Angular
│   └── public-statement/ # Angular
└── libs/
```

This fights .NET tooling: `dotnet` expects `.sln` + sibling projects with relative paths; building from a root containing `node_modules` is slow (file-watcher churn); Rider and VS both assume backend-rooted context when opening the `.sln`. Side-by-side `backend/ + frontend/` respects each stack's native conventions at minimal cost.

#### 6.2.5 One-Way Doors in S1

| Decision | Reversibility | Trigger to revisit |
|---|---|---|
| Single repo (backend + frontend) | Moderate — feasible with `git filter-repo` once PR history exists | RT-24 |
| Side-by-side `backend/ + frontend/` vs root-level `apps/` | Low-cost — directory rename + config updates | Adoption of Nx or similar |
| Angular CLI workspace (vs Nx) | Moderate — migration needs project graph + executor rewrites | RT-25 |
| `api-contracts` committed (vs gitignored) | Low — just stop committing | RT-26 |

#### 6.2.6 New Revisit Triggers (S1)

| ID | Trigger | Meaning |
|---|---|---|
| RT-24 | Team > 5 devs AND backend/frontend release cadence diverges > 2 sprints | Revisit monorepo → polyrepo split |
| RT-25 | Angular workspace reaches 4 apps OR CI time > 15 min | Revisit CLI → Nx migration |
| RT-26 | `libs/api-contracts` > ~500 generated files OR PR diffs become noisy from contracts-only changes | Revisit committed-generated policy |

### 6.3 Batch S2 — Backend Tree

#### 6.3.1 Solution Tree

```
backend/
├── Accountantim.sln
├── global.json
├── Directory.Build.props
├── .editorconfig                      # (inherits from repo root)
│
├── src/
│   ├── Accountantim.AppHost/                     # Aspire 13 orchestrator
│   │   ├── Program.cs                            # builder.AddPostgres() + AddProject<Api>()
│   │   ├── appsettings.json
│   │   └── Accountantim.AppHost.csproj
│   │
│   ├── Accountantim.ServiceDefaults/             # Aspire defaults: OTEL, health, resilience, discovery
│   │   ├── Extensions.cs                         # AddServiceDefaults() extension
│   │   └── Accountantim.ServiceDefaults.csproj
│   │
│   ├── Accountantim.Domain/                      # pure domain — no EF, no Aspire, no ASP.NET
│   │   ├── Abstractions/
│   │   │   ├── IAggregateRoot.cs
│   │   │   ├── DomainEvent.cs
│   │   │   ├── ValueObject.cs
│   │   │   └── Result.cs
│   │   ├── Money/
│   │   │   ├── Money.cs                          # value object (Step 4 D4.6)
│   │   │   ├── Currency.cs
│   │   │   ├── CurrencyRegistry.cs               # EUR/USD/GBP/AED/SAR/EGP=2 minor; JOD/KWD/BHD/TND=3
│   │   │   └── MoneyJsonConverter.cs
│   │   ├── Time/
│   │   │   ├── MonthBoundary.cs                  # (userTimezone, year, month) boundary helper
│   │   │   └── DateOnlyExtensions.cs
│   │   ├── Errors/
│   │   │   ├── ErrorCode.cs                      # record struct + validation (Step 5 B1)
│   │   │   ├── DomainError.cs
│   │   │   ├── ErrorCatalog.cs                   # central registry shape
│   │   │   └── ErrorCatalogRegistry.cs           # assembly-scan aggregator at startup
│   │   ├── Identity/
│   │   │   └── UserId.cs                         # strongly-typed IDs
│   │   └── Accountantim.Domain.csproj
│   │
│   ├── Accountantim.Infrastructure/              # EF Core, Wolverine, integrations
│   │   ├── Persistence/
│   │   │   ├── AccountantimDbContext.cs
│   │   │   ├── Configurations/                   # IEntityTypeConfiguration<T>
│   │   │   │   ├── JournalEntryConfiguration.cs
│   │   │   │   └── ...
│   │   │   ├── Interceptors/
│   │   │   │   └── AuditInterceptor.cs
│   │   │   └── Conventions/                      # snake_case naming convention registration
│   │   ├── Migrations/                           # EF default location (choice 4a)
│   │   │   ├── 20260420120000_InitialSchema.cs
│   │   │   ├── 20260420120000_InitialSchema.Designer.cs
│   │   │   ├── 20260501090000_AddIdempotencyCache.cs
│   │   │   └── AccountantimDbContextModelSnapshot.cs
│   │   ├── Repositories/
│   │   │   ├── JournalEntryRepository.cs
│   │   │   └── ...
│   │   ├── Messaging/
│   │   │   ├── WolverineConfiguration.cs         # Postgres transport + outbox (Step 4 D4.12)
│   │   │   ├── OutboxInterceptor.cs
│   │   │   └── Handlers/                         # cross-slice Wolverine handlers (domain-event consumers)
│   │   ├── Observability/
│   │   │   └── OpenTelemetryExtensions.cs
│   │   ├── External/                             # third-party clients (email, PSP stubs, KYC)
│   │   └── Accountantim.Infrastructure.csproj
│   │
│   └── Accountantim.Api/                         # ASP.NET host — FastEndpoints + vertical slices
│       ├── Program.cs                            # minimal: AddServiceDefaults() + AddFastEndpoints() + AddWolverine()
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Slices/                               # detail in 6.3.2
│       ├── Infrastructure/                       # cross-slice API plumbing — NOT domain logic
│       │   ├── ErrorHandling/
│       │   │   ├── ProblemDetailsMiddleware.cs   # RFC 9457 + ErrorCatalog lookup
│       │   │   └── ExceptionMappers.cs
│       │   ├── Idempotency/
│       │   │   └── IdempotencyMiddleware.cs      # Step 4 D4.11
│       │   ├── ApiVersioning/
│       │   │   └── VersioningConfiguration.cs    # Asp.Versioning URL-path /v1
│       │   ├── Authorization/
│       │   │   └── CapabilityTokenHandler.cs     # opaque tokens for public-statement (D4.6)
│       │   └── Observability/
│       │       └── CorrelationIdMiddleware.cs    # traceId propagation
│       ├── Contracts/                            # cross-slice wire-format base types
│       │   └── MoneyJsonConverterFactory.cs      # global JSON converter registration
│       ├── OpenApi/
│       │   ├── OpenApiConfiguration.cs
│       │   └── ErrorCatalogComponentGenerator.cs # emits error catalog into OpenAPI components/schemas
│       └── Accountantim.Api.csproj
│
└── tests/                                        # detail in 6.3.3
    ├── Accountantim.Domain.Tests/
    ├── Accountantim.Api.Tests/
    └── Accountantim.Api.IntegrationTests/
```

#### 6.3.2 Vertical-Slice Internal Structure

Mirroring Step 5 B2 convention. A representative slice:

```
src/Accountantim.Api/Slices/
├── JournalEntries/
│   ├── _Shared/                              # slice-internal shared — NOT cross-slice
│   │   ├── JournalEntry.cs                   # aggregate root
│   │   ├── IJournalEntryRepository.cs        # slice-owned interface (impl in Infrastructure)
│   │   ├── JournalEntryStatus.cs
│   │   └── JournalEntryErrors.cs             # accountantim.journal_entries.* codes
│   │
│   ├── Create/
│   │   ├── CreateJournalEntryRequest.cs
│   │   ├── CreateJournalEntryResponse.cs
│   │   ├── CreateJournalEntryRequestValidator.cs
│   │   └── CreateJournalEntryEndpoint.cs     # Endpoint<TReq, TResp> — contains or delegates handler
│   │
│   ├── Reverse/
│   │   ├── ReverseJournalEntryRequest.cs
│   │   ├── ReverseJournalEntryResponse.cs
│   │   ├── ReverseJournalEntryRequestValidator.cs
│   │   ├── ReverseJournalEntryEndpoint.cs
│   │   └── JournalEntryReversedHandler.cs    # Wolverine handler — post-commit domain event consumer
│   │
│   ├── List/
│   │   ├── ListJournalEntriesQuery.cs
│   │   ├── ListJournalEntriesResponse.cs
│   │   └── ListJournalEntriesEndpoint.cs
│   │
│   └── GetById/
│       ├── GetJournalEntryByIdResponse.cs
│       └── GetJournalEntryByIdEndpoint.cs
│
├── Accounts/
│   ├── _Shared/
│   │   ├── Account.cs
│   │   ├── IAccountRepository.cs
│   │   └── AccountErrors.cs
│   ├── Create/
│   ├── Update/
│   ├── List/
│   └── GetById/
│
├── Users/
│   ├── _Shared/
│   ├── Register/
│   ├── VerifyEmail/
│   └── GetProfile/
│
└── PublicStatement/                          # capability-token-gated slice (D4.6)
    ├── _Shared/
    │   ├── StatementCapability.cs
    │   └── StatementErrors.cs
    └── GetByToken/
        ├── GetStatementByTokenResponse.cs
        └── GetStatementByTokenEndpoint.cs
```

**Slice-internal conventions (recap of Step 5 B2 for concrete reference):**
- `_Shared/` (underscore prefix) sorts first in alphabetical listings; contains slice-internal types never referenced outside the slice
- `<Action>/` folders named after the operation (imperative verb)
- Each action folder contains at minimum: Request, Response, Validator, Endpoint; Handler optional (present only when Wolverine dispatch is used for domain-event propagation)
- No cross-slice imports except via `Infrastructure/` (e.g. `IMessageBus`) or `Domain/` (value objects, primitives)

#### 6.3.3 Test Project Layout

```
tests/
├── Accountantim.Domain.Tests/                # pure unit tests — no Testcontainers, no Aspire
│   ├── Money/
│   │   ├── MoneyTests.cs
│   │   └── CurrencyRegistryTests.cs
│   ├── Time/
│   │   └── MonthBoundaryTests.cs
│   ├── Errors/
│   │   └── ErrorCatalogRegistryTests.cs
│   └── Accountantim.Domain.Tests.csproj
│
├── Accountantim.Api.Tests/                   # unit tests — slice-mirrored structure
│   ├── Slices/
│   │   ├── JournalEntries/
│   │   │   ├── Create/
│   │   │   │   ├── CreateJournalEntryEndpointTests.cs
│   │   │   │   ├── CreateJournalEntryRequestValidatorTests.cs
│   │   │   │   └── CreateJournalEntryHandlerTests.cs
│   │   │   └── Reverse/
│   │   │       └── ...
│   │   └── Accounts/
│   │       └── ...
│   ├── Infrastructure/
│   │   ├── ErrorHandling/
│   │   │   └── ProblemDetailsMiddlewareTests.cs
│   │   └── Idempotency/
│   │       └── IdempotencyMiddlewareTests.cs
│   ├── Architecture/                         # NetArchTest fitness functions (Step 5 enforcement)
│   │   ├── VerticalSliceBoundariesTests.cs   # no cross-slice namespace refs
│   │   ├── ErrorCatalogCompletenessTests.cs  # every thrown DomainError registered in catalog
│   │   ├── SnakeCaseDbNamingTests.cs
│   │   └── FluentValidationPresenceTests.cs  # every endpoint has a validator
│   └── Accountantim.Api.Tests.csproj
│
└── Accountantim.Api.IntegrationTests/        # Testcontainers-backed — [Trait("Category","Integration")]
    ├── Slices/                               # mirrors src/Accountantim.Api/Slices
    │   ├── JournalEntries/
    │   │   ├── CreateJournalEntryIntegrationTests.cs
    │   │   └── ReverseJournalEntryIntegrationTests.cs
    │   └── PublicStatement/
    │       └── GetStatementByTokenIntegrationTests.cs
    ├── Fixtures/
    │   ├── PostgresFixture.cs                # Testcontainers.PostgreSql + migration apply
    │   ├── ApiFactory.cs                     # WebApplicationFactory<Program>
    │   ├── AspireFixture.cs                  # distributed integration (optional)
    │   └── Builders/
    │       ├── JournalEntryBuilder.cs
    │       └── AccountBuilder.cs
    ├── Infrastructure/
    │   ├── WolverineOutboxIntegrationTests.cs
    │   └── IdempotencyEndToEndTests.cs
    └── Accountantim.Api.IntegrationTests.csproj
```

**Test organization principles:**
- **Split by test category, not slice**: unit tests in `Api.Tests`, integration tests in `Api.IntegrationTests` — xUnit `[Trait("Category", "Integration")]` filter enables CI stage separation (Step 5 B2)
- **Mirror slice folder structure in test projects**: navigation parity — finding `Slices/JournalEntries/Create/CreateJournalEntryEndpoint.cs` means finding `Slices/JournalEntries/Create/CreateJournalEntryEndpointTests.cs`
- **Domain tests are pure**: no Testcontainers, no ASP.NET, no Aspire — they run in milliseconds and gate fast feedback
- **Architecture/ folder in Api.Tests**: fitness functions enforcing Step 5 conventions live with unit tests, not integration tests, because they run at compile+assembly-inspection time (not runtime)

#### 6.3.4 Project Reference Graph

```
AppHost ──→ Api
   └─→ (orchestrates Postgres container at runtime)

Api ──→ ServiceDefaults
   ├─→ Domain
   └─→ Infrastructure

Infrastructure ──→ Domain
Infrastructure ──→ ServiceDefaults   (OTEL, resilience)

Domain ──→ (nothing — pure domain)

ServiceDefaults ──→ (nothing — Aspire defaults only)

Test projects ──→ their respective source projects + testing libs
```

**Invariants enforced by NetArchTest:**
- `Domain` references nothing outside the BCL
- `Api.Slices.<SliceA>` does not reference `Api.Slices.<SliceB>` (cross-slice isolation)
- `Api.Slices.*._Shared` types are not referenced outside their owning slice
- `ErrorCode` instances exist only within slice `_Shared/*Errors.cs` files AND are registered in `ErrorCatalogRegistry` at startup

#### 6.3.5 One-Way Doors in S2

| Decision | Reversibility | Revisit trigger |
|---|---|---|
| Vertical slices as folders (not projects) | Moderate — extracting a slice to its own project is mechanical but breaks existing namespaces | Slice grows past ~50 files OR needs independent deployment |
| Single Api project hosts all slices including PublicStatement | Moderate — public-statement splitting into own Api project is feasible (they'd share Infrastructure + Domain) | D4.6 CDN load > 500rps sustained OR public-facing security boundary hardening required |
| Migrations at EF default location (not slice-grouped) | Low-cost reversible — directory move + `dotnet ef` config | No realistic trigger (schema-audit gate already enforces cohesion) |
| Domain as separate project (not folder in Api) | Moderate — merging would lose NetArchTest boundary enforcement | No expected trigger — keeping Domain isolated is fundamental |

No new revisit triggers in S2 beyond what Step 4 and S1 already registered.

### 6.4 Batch S3 — Frontend Tree

#### 6.4.1 Workspace Tree

```
frontend/
├── [root config files — already listed in S1 §6.2.2]
│
├── apps/
│   ├── owner-workspace/               # primary authenticated SPA — full feature surface
│   └── public-statement/              # capability-token-gated single-view, CDN-cached
│
├── libs/
│   ├── design-system/                 # PrimeNG + Tailwind primitives, ds-* prefix
│   ├── core/                          # signal services, HTTP interceptors, form helpers
│   ├── domain-ui/                     # shared domain widgets (StatementRenderer, MoneyDisplay)
│   └── api-contracts/                 # GENERATED from backend OpenAPI — committed
│
├── e2e/                               # Playwright specs (Chromium + WebKit)
│   ├── owner-workspace/
│   │   ├── authentication.spec.ts
│   │   └── journal-entry-create.spec.ts
│   ├── public-statement/
│   │   └── capability-token-view.spec.ts
│   └── fixtures/
│       ├── api-client.ts              # direct backend calls for test setup
│       └── test-users.ts
│
└── scripts/
    ├── verify-api-contracts.ts        # CI guard: regenerates and diffs
    └── rtl-audit.ts                   # scans Tailwind classes for RTL-unsafe patterns
```

#### 6.4.2 `apps/owner-workspace/` Internal Layout

Feature-folder organization mirroring backend slices:

```
apps/owner-workspace/
├── src/
│   ├── main.ts
│   ├── index.html
│   ├── styles.css                     # Tailwind directives + DS token import + RTL bidi base
│   ├── app/
│   │   ├── app.config.ts              # provideRouter, provideHttpClient, interceptors, Transloco, PrimeNG theme
│   │   ├── app.routes.ts              # top-level routes — all feature routes lazy
│   │   ├── app.component.ts           # shell: <ow-top-nav /> + <router-outlet /> + <ds-toast-outlet />
│   │   ├── app.component.html
│   │   ├── shell/
│   │   │   ├── top-nav/
│   │   │   │   ├── top-nav.component.ts
│   │   │   │   ├── top-nav.component.html
│   │   │   │   └── top-nav.component.spec.ts
│   │   │   ├── side-nav/
│   │   │   └── locale-switcher/
│   │   ├── features/
│   │   │   ├── journal-entries/              # mirrors backend Slices/JournalEntries/
│   │   │   │   ├── journal-entries.routes.ts
│   │   │   │   ├── pages/
│   │   │   │   │   ├── journal-entries-list.page.ts        # <ow-journal-entries-list-page>
│   │   │   │   │   ├── journal-entries-list.page.html
│   │   │   │   │   ├── journal-entries-list.page.spec.ts
│   │   │   │   │   ├── journal-entry-create.page.ts
│   │   │   │   │   └── journal-entry-detail.page.ts
│   │   │   │   ├── components/                              # feature-internal
│   │   │   │   │   ├── journal-entry-form.component.ts
│   │   │   │   │   ├── journal-entry-row.component.ts
│   │   │   │   │   └── journal-entry-filters.component.ts
│   │   │   │   ├── services/
│   │   │   │   │   └── journal-entries-api.service.ts       # wraps generated api-contracts
│   │   │   │   └── i18n/
│   │   │   │       ├── ar.json
│   │   │   │       ├── en.json
│   │   │   │       └── fr.json
│   │   │   ├── accounts/
│   │   │   ├── users/                                       # profile, email verification
│   │   │   └── dashboard/                                   # home landing
│   │   ├── auth/
│   │   │   ├── guards/
│   │   │   │   └── authenticated.guard.ts
│   │   │   ├── pages/
│   │   │   │   ├── sign-in.page.ts
│   │   │   │   └── sign-up.page.ts
│   │   │   └── services/
│   │   │       └── auth-api.service.ts
│   │   └── app.config.routes.ts       # lazy route imports
│   │
│   ├── assets/
│   │   ├── i18n/                      # app-chrome translations (shell, auth, dashboard)
│   │   │   ├── ar.json
│   │   │   ├── en.json
│   │   │   └── fr.json
│   │   ├── fonts/                     # Noto Sans Arabic, Inter
│   │   ├── icons/                     # app-specific; ds icons live in design-system
│   │   └── images/
│   │
│   ├── environments/
│   │   ├── environment.ts             # base (dev)
│   │   ├── environment.staging.ts
│   │   └── environment.production.ts
│   │
│   └── test-setup.ts                  # Vitest + Angular Testing Library setup
│
├── vitest.config.ts                   # extends frontend/vitest.config.ts
├── tsconfig.app.json
├── tsconfig.spec.json
└── public/                            # static served unchanged (favicon, robots.txt)
```

**Feature-folder conventions:**
- One folder per top-level feature; name mirrors backend slice when slice exists (e.g. `features/journal-entries/` ↔ backend `Slices/JournalEntries/`)
- `pages/` — routable components (suffix `.page.ts`, selector `ow-<feature>-<action>-page`)
- `components/` — feature-internal non-routable (suffix `.component.ts`, selector `ow-<name>`)
- `services/` — feature-scoped state and API wrappers (suffix `.service.ts`)
- `i18n/` — Transloco scope per feature; keys namespaced `<feature>.<key>`
- Lazy-loaded via `loadChildren` in `app.routes.ts`; each feature defines its own `<feature>.routes.ts`

#### 6.4.3 `apps/public-statement/` Internal Layout

Single-purpose capability-token-gated view. Much smaller than owner-workspace:

```
apps/public-statement/
├── src/
│   ├── main.ts
│   ├── index.html
│   ├── styles.css
│   ├── app/
│   │   ├── app.config.ts              # minimal — no auth providers, capability-token interceptor only
│   │   ├── app.routes.ts              # single route: /:token → statement.page
│   │   ├── app.component.ts           # shell: just <router-outlet />; no nav chrome
│   │   ├── statement/
│   │   │   ├── statement.page.ts      # selector ps-statement-page; consumes rxResource
│   │   │   ├── statement.page.html
│   │   │   ├── statement.page.spec.ts
│   │   │   └── statement-api.service.ts
│   │   └── not-found/
│   │       └── not-found.page.ts      # 404 for invalid/expired tokens
│   │
│   ├── assets/
│   │   ├── i18n/                      # auto-detected from statement locale
│   │   │   ├── ar.json
│   │   │   ├── en.json
│   │   │   └── fr.json
│   │   ├── fonts/
│   │   └── images/
│   │
│   ├── environments/
│   └── test-setup.ts
│
├── vitest.config.ts
├── tsconfig.app.json
└── public/
```

**Public-statement-specific constraints (recap of D4.6):**
- No authentication — capability token is the credential
- CDN-cacheable: no user-specific chrome, no cookies, no localStorage writes that vary the response
- 180 KB gz bundle budget (tight — tree-shakes PrimeNG aggressively)
- LCP < 2.5s on 4G — public entry must render above-the-fold content from first HTML response where feasible

#### 6.4.4 `libs/design-system/` Internal Layout

```
libs/design-system/
├── src/
│   ├── public-api.ts                  # barrel — only exports intentional surface
│   ├── lib/
│   │   ├── buttons/
│   │   │   ├── button.component.ts                  # ds-button wrapper over p-button
│   │   │   ├── button.component.spec.ts
│   │   │   └── icon-button.component.ts
│   │   ├── forms/
│   │   │   ├── input.component.ts                   # ds-input
│   │   │   ├── textarea.component.ts
│   │   │   ├── select.component.ts
│   │   │   ├── date-picker.component.ts
│   │   │   ├── money-input.component.ts             # decimal.js-aware (Step 5 B1 consumer)
│   │   │   └── form-field.component.ts              # label + error + hint wrapper for Signal Forms
│   │   ├── feedback/
│   │   │   ├── skeleton.component.ts                # ds-skeleton (Step 5 B3)
│   │   │   ├── error-state.component.ts             # ds-error-state
│   │   │   ├── empty-state.component.ts             # ds-empty-state
│   │   │   └── toast-outlet.component.ts
│   │   ├── layout/
│   │   │   ├── card.component.ts
│   │   │   ├── stack.component.ts
│   │   │   └── section.component.ts
│   │   ├── data-display/
│   │   │   ├── table.component.ts                   # ds-table wrapper over p-table
│   │   │   └── badge.component.ts
│   │   ├── overlays/
│   │   │   ├── dialog.component.ts
│   │   │   └── drawer.component.ts
│   │   └── tokens/
│   │       ├── design-tokens.css                    # CSS custom properties — single source
│   │       ├── tokens.ts                            # TS-accessible token constants
│   │       └── primeng-theme.ts                     # Aura preset override
│   │
│   └── styles/
│       ├── rtl-utilities.css                        # logical-property helpers (padding-inline-start etc.)
│       └── tailwind-base.css                        # base layer additions
│
├── ng-package.json
├── package.json
├── tsconfig.lib.json
└── vitest.config.ts
```

**Design-system conventions:**
- **Zero i18n**: no Transloco scope — all consumer strings passed in as `@Input` signals; library stays locale-agnostic
- **PrimeNG wrapper pattern**: `ds-*` components wrap `p-*` components with project-specific defaults, token application, and simplified APIs
- **`public-api.ts` barrel is authoritative**: anything not exported from here cannot be imported by consumers; ESLint enforces (Step 5 B3)

#### 6.4.5 `libs/core/` Internal Layout

```
libs/core/
├── src/
│   ├── public-api.ts
│   ├── lib/
│   │   ├── state/                                  # signal services
│   │   │   ├── current-user.service.ts             # signal<CurrentUser | null>
│   │   │   ├── locale.service.ts                   # signal<Locale> + RTL detection
│   │   │   ├── notification.service.ts             # toast queue signal
│   │   │   └── capability-token.service.ts         # public-statement bootstrap token
│   │   ├── http/
│   │   │   ├── correlation-id.interceptor.ts       # propagates traceId (Step 4 D4.9)
│   │   │   ├── idempotency-key.interceptor.ts      # attaches UUID v4 per form render (D4.14)
│   │   │   ├── capability-token.interceptor.ts     # public-statement auth
│   │   │   ├── error-mapper.interceptor.ts         # RFC 9457 → DomainError objects
│   │   │   └── api-base-url.interceptor.ts         # prepends environment.apiUrl
│   │   ├── forms/
│   │   │   ├── apply-server-errors.ts              # walks errors[] JSON Pointers onto Signal Forms (D4.14)
│   │   │   ├── validation-catalog.ts               # Transloco key mapping for server codes
│   │   │   └── idempotency-key-factory.ts          # per-render UUID v4, refresh on success/reset
│   │   ├── resources/
│   │   │   └── resource-status.ts                  # shared rxResource status type
│   │   └── errors/
│   │       ├── domain-error.ts                     # frontend DomainError type
│   │       └── error-catalog-registry.ts           # Transloco key lookup for codes
│   │
│   └── test-utils/                                 # re-exported for consumers
│       ├── mock-http-context.ts
│       └── signal-forms-test-helpers.ts
│
├── ng-package.json
├── package.json
├── tsconfig.lib.json
└── vitest.config.ts
```

#### 6.4.6 `libs/domain-ui/` Internal Layout

Shared domain widgets used by **both** apps:

```
libs/domain-ui/
├── src/
│   ├── public-api.ts
│   ├── lib/
│   │   ├── money/
│   │   │   ├── money-display.component.ts          # du-money; decimal.js + Transloco plurals
│   │   │   └── money-display.component.spec.ts
│   │   ├── date/
│   │   │   ├── date-display.component.ts           # du-date; Intl + locale-aware
│   │   │   ├── month-selector.component.ts         # du-month-selector
│   │   │   └── date-range-display.component.ts
│   │   ├── statement/
│   │   │   ├── statement-renderer.component.ts     # du-statement-renderer — THE shared component
│   │   │   ├── statement-header.component.ts
│   │   │   ├── statement-line-item.component.ts
│   │   │   └── statement-totals.component.ts
│   │   ├── journal-entry/
│   │   │   └── journal-entry-row.component.ts      # du-journal-entry-row
│   │   └── identity/
│   │       └── user-avatar.component.ts            # du-user-avatar
│   │
│   ├── assets/
│   │   └── i18n/                                   # Transloco scope: "domain-ui"
│   │       ├── ar.json
│   │       ├── en.json
│   │       └── fr.json
│   │
│   └── test-setup.ts
│
├── ng-package.json
├── package.json
├── tsconfig.lib.json
└── vitest.config.ts
```

**`domain-ui` rule:** a component belongs here iff it renders domain concepts (money, dates, journal entries, statements) AND is consumed by more than one app OR reserved for cross-app consumption per MVP roadmap. Single-app components stay in `apps/<app>/src/app/features/<feature>/components/`.

#### 6.4.7 `libs/api-contracts/` Internal Layout (Generated)

```
libs/api-contracts/
├── src/
│   ├── public-api.ts                  # re-exports all generated types
│   └── generated/                     # GENERATED — do not hand-edit
│       ├── models/                    # interface per OpenAPI schema
│       │   ├── create-journal-entry-request.ts
│       │   ├── create-journal-entry-response.ts
│       │   ├── journal-entry.ts
│       │   ├── money.ts               # { amount: string, currency: string }
│       │   ├── problem-details.ts     # RFC 9457 shape
│       │   └── ...
│       ├── services/                  # one class per controller/slice
│       │   ├── journal-entries.service.ts
│       │   ├── accounts.service.ts
│       │   └── ...
│       └── enums/
│           ├── journal-entry-status.ts
│           └── ...
│
├── ng-package.json
├── package.json
├── tsconfig.lib.json
├── GENERATION.md                      # pipeline description + regen instructions
└── .generated-checksum                # CI uses this to detect stale output
```

**`api-contracts` rules:**
- `src/generated/` gitignored from edit (committed but marked `linguist-generated=true` in `.gitattributes`)
- Regeneration script `scripts/gen-contracts.ps1` (repo root) runs backend OpenAPI dump → TS generation; CI re-runs and fails if diff appears (stale-contracts guard)
- Consumers import typed services; slice API wrappers in `apps/owner-workspace/src/app/features/<feature>/services/` are thin adapters that add frontend-specific behaviors (signal wrapping, error mapping)

#### 6.4.8 Tests — Per-Project Organization

| Test type | Location | Config | Runner |
|---|---|---|---|
| Unit (component/service/pipe) | Co-located `*.spec.ts` next to source | `<project>/vitest.config.ts` | Vitest + Angular Testing Library |
| Unit (architecture/boundary) | `frontend/scripts/` + ESLint custom rules | `eslint.config.js` | ESLint (pre-commit + CI) |
| E2E | `frontend/e2e/<app>/*.spec.ts` | `frontend/playwright.config.ts` | Playwright (Chromium + WebKit) |

**E2E fixture strategy:**
- `frontend/e2e/fixtures/api-client.ts` calls backend directly (bypasses UI) for test data setup — keeps E2E specs focused on user flows, not test-bed assembly
- `frontend/e2e/fixtures/test-users.ts` maintains a registry of known test identities with stable capability tokens
- Both browsers run in CI; WebKit catches Safari-specific issues for MENA iOS users (Step 3 mobile-reality anchor)

#### 6.4.9 Routing Structure (Owner Workspace)

```
/ (shell guard: authenticated)
├── /dashboard
├── /journal-entries
│   ├── ''              (list)
│   ├── /new
│   └── /:id            (detail)
├── /accounts
│   ├── ''
│   ├── /new
│   └── /:id
└── /profile

/sign-in               (no shell — standalone)
/sign-up
/verify-email/:token

# public-statement app — separate origin/subdomain, no shell
/:capabilityToken      (statement render)
/expired
```

Lazy loading: every feature under `/` loads via `loadChildren` — initial bundle contains only shell + auth + dashboard.

#### 6.4.10 One-Way Doors in S3

| Decision | Reversibility | Revisit trigger |
|---|---|---|
| Feature-folder organization mirroring backend slices | Low — folder rename + route update | No expected trigger — mirror is load-bearing for ADR-006 cross-stack legibility |
| `domain-ui` as separate library (vs duplicating components across apps) | Low — extraction is mechanical | If `public-statement` drops statement-rendering entirely and shares nothing with owner-workspace |
| `api-contracts` generated into Angular library (vs npm package) | Moderate — npm path requires publishing pipeline | Team splits frontend into its own repo (RT-24) |
| Single `app.component.ts` shell per app (vs nested shell routes) | Low-cost | Navigation complexity grows past 3 levels |
| E2E at workspace root (vs per-app folders) | Low | If apps diverge enough to need separate Playwright configs |

#### 6.4.11 New Revisit Triggers (S3)

| ID | Trigger | Meaning |
|---|---|---|
| RT-27 | `libs/domain-ui` component count exceeds ~25 OR import graph shows feature-specific coupling | Reconsider whether domain-ui is a monolith — split into `domain-ui-money`, `domain-ui-statement`, etc. |
| RT-28 | `apps/public-statement` bundle size exceeds 180 KB gz | Audit PrimeNG imports, consider non-PrimeNG primitive rendering for public-statement, or split design-system into `design-system-core` + `design-system-rich` |
| RT-29 | Transloco translation key count per app exceeds ~500 OR RTL-EN-FR translation drift appears in production | Externalize translations to a managed service (Crowdin, Phrase, Lokalise) |

### 6.5 Batch S4 — Cross-Cutting

#### 6.5.1 Contracts Generation Pipeline

The load-bearing seam between backend and frontend (Step 4 D4.3 + S1 §6.2.3 commitment).

**Pipeline:**

```
backend/src/Accountantim.Api  ──(dotnet run /openapi)──→  openapi.json
         │
         └─── scripts/gen-contracts.ps1 ──(ng-openapi-gen)──→  frontend/libs/api-contracts/src/generated/
                                                                       ├── models/
                                                                       ├── services/
                                                                       └── enums/
```

**Generator choice:** `ng-openapi-gen` — emits typed Angular services consuming `HttpClient`, which our feature-level `*-api.service.ts` wrappers then adapt to signal-based resource consumption. Rationale: a solo MVP dev cannot afford hand-written services across 5+ slices; generated services minimize ongoing cost at the price of regeneration churn, which we mitigate by committing the output (RT-26 covers the eventual revisit).

**Script shape (`scripts/gen-contracts.ps1`):**

```powershell
# 1. Build backend
dotnet build backend/src/Accountantim.Api

# 2. Dump OpenAPI spec
dotnet run --project backend/src/Accountantim.Api -- --generate-openapi-doc
  > backend/artifacts/openapi.json

# 3. Run ng-openapi-gen
npx ng-openapi-gen `
  --input backend/artifacts/openapi.json `
  --output frontend/libs/api-contracts/src/generated `
  --ignoreUnusedModels true `
  --serviceSuffix Service

# 4. Update checksum file for CI stale-detection
Get-FileHash backend/artifacts/openapi.json -Algorithm SHA256
  | Out-File frontend/libs/api-contracts/.generated-checksum
```

**Stale-contracts CI guard:** on every PR, GitHub Actions re-runs `gen-contracts.ps1` and executes `git diff --exit-code frontend/libs/api-contracts/`. Non-zero exit fails the build with a diff-in-log. Developers regenerate locally and commit.

#### 6.5.2 CI/CD Workflows

```
.github/workflows/
├── pr.yml                 # pull_request triggers → lint + unit + build + arch-tests
├── main.yml               # push to main → full test + deploy staging
├── contracts-guard.yml    # pull_request → regen contracts, fail on diff
├── e2e.yml                # nightly + pre-deploy → Playwright matrix (Chromium + WebKit)
├── release.yml            # tag push (v*.*.*) → deploy production
└── security-scan.yml      # weekly + on-demand → CodeQL + dependency scan
```

**`pr.yml` stages:**

```yaml
jobs:
  backend:
    - dotnet build --configuration Release
    - dotnet test --filter 'Category!=Integration'
    - dotnet test --filter 'Category=Integration'  # Testcontainers-backed
    - dotnet format --verify-no-changes            # style enforcement

  frontend:
    - npm ci --prefix frontend
    - npm run lint --prefix frontend
    - npm run test:ci --prefix frontend            # Vitest, per-project
    - npm run build --prefix frontend              # all projects, fails on bundle-budget violation

  contracts:
    - pwsh scripts/gen-contracts.ps1
    - git diff --exit-code frontend/libs/api-contracts/

  schema-audit:                                     # Step 5 B1 enforcement
    - dotnet run --project backend/tools/SchemaAudit -- --check
```

**Branch protection:** all four jobs gate PR merge. E2E runs separately (slower, nightly) to keep PR feedback under 8 minutes.

#### 6.5.3 Local Development

**Single command to start everything:**

```bash
# Backend + Postgres + OTEL collector
dotnet run --project backend/src/Accountantim.AppHost

# Frontend (separate terminal per app)
npm run start:owner --prefix frontend       # ng serve owner-workspace, port 4200
npm run start:public --prefix frontend      # ng serve public-statement, port 4201

# Regenerate contracts after backend change
npm run gen:contracts --prefix frontend     # wraps scripts/gen-contracts.ps1
```

**Aspire AppHost responsibilities (`backend/src/Accountantim.AppHost/Program.cs`):**
- Provisions Postgres container (via Testcontainers.Aspire or equivalent)
- Applies EF migrations on startup
- Seeds dev data from `scripts/seed-dev-db.ps1`
- Starts Api project with connection string wired
- Wires OTEL collector for local traces

**Proxy configuration** (`frontend/apps/owner-workspace/proxy.conf.json`):

```json
{
  "/v1/*": { "target": "http://localhost:5000", "secure": false, "logLevel": "debug" },
  "/openapi/*": { "target": "http://localhost:5000", "secure": false }
}
```

Avoids dev-only CORS config on the backend.

#### 6.5.4 Infrastructure (Docker + Azure Bicep)

**Docker images:**

```
infra/
├── Dockerfile.api                     # multi-stage: dotnet sdk → dotnet runtime + Api DLL
├── Dockerfile.spa                     # multi-stage: node build → nginx serve static
├── docker-compose.yml                 # local: postgres + api (infra-only reference)
└── docker-compose.dev.yml             # dev: postgres + seed only (app runs via Aspire)
```

**Azure Bicep (`infra/azure/`):**

```
infra/azure/
├── main.bicep                         # parameters + module orchestration
├── modules/
│   ├── postgres.bicep                 # Flexible Server + firewall + databases
│   ├── app-service-api.bicep          # Linux container App Service for Api
│   ├── static-web-app-owner.bicep     # owner-workspace hosting
│   ├── static-web-app-public.bicep    # public-statement hosting behind Front Door CDN
│   ├── cdn.bicep                      # Front Door profile + caching rules (D4.6 — 80/500 rps + p95<300ms)
│   ├── app-insights.bicep             # OTEL sink
│   ├── key-vault.bicep                # secrets + capability-token signing keys
│   └── service-bus.bicep              # empty stub — enable on RT for D4.12 reversal
└── parameters/
    ├── staging.bicepparam
    └── production.bicepparam
```

**Env targets:**
- **Local** — Aspire + Postgres container
- **Staging** — full Azure stack, ephemeral DB snapshot reset weekly
- **Production** — Azure (EU regions per MENA+EU data residency); Service Bus stub exists but unactivated until RT trigger fires

#### 6.5.5 Documentation

```
docs/
├── architecture.md                    # this document, relocated from _bmad-output/ at Step 8 close
├── prd.md                             # PRD snapshot
├── adrs/
│   ├── ADR-001-capability-tokens.md
│   ├── ADR-002-key-shred-erasure.md
│   ├── ADR-003-vertical-slice-architecture.md   (ADR-006 — renumbered)
│   ├── ADR-007-decimal-string-money.md          (from Step 5 B1 — deferred consolidation)
│   ├── ADR-008-rfc-9457-error-envelope.md       (from Step 4 D4.9)
│   ├── ADR-009-idempotency-key-strategy.md      (from Step 4 D4.11)
│   ├── ADR-010-wolverine-postgres-transport.md  (from Step 4 D4.12)
│   └── template.md                              # ADR template for future decisions
├── runbooks/
│   ├── incident-response.md
│   ├── database-migration.md
│   ├── key-rotation.md                          # capability-token + key-shred keys
│   └── deployment-rollback.md
├── development/
│   ├── getting-started.md                       # one-page quickstart
│   ├── slice-cookbook.md                        # how to add a new slice (backend + frontend)
│   ├── testing-guide.md
│   └── i18n-guide.md                            # adding Transloco keys + RTL checklist
└── api/
    └── openapi.json                             # published snapshot (CI artifact from main branch)
```

#### 6.5.6 Environment Configuration

**Backend (`backend/src/Accountantim.Api/`):**

```
appsettings.json                  # defaults + schema
appsettings.Development.json      # dev overrides (OTEL console exporter, verbose logs)
appsettings.Staging.json          # staging overrides
appsettings.Production.json       # production overrides (minimal — prefer Key Vault)
```

Secrets resolved via:
1. Azure Key Vault (production + staging)
2. `.NET user-secrets` (local dev)
3. **Never** from environment files in version control

**Frontend (`frontend/apps/<app>/src/environments/`):**

```
environment.ts                    # dev defaults (apiUrl: '/v1' via proxy)
environment.staging.ts            # staging apiUrl + feature flags
environment.production.ts         # production apiUrl + feature flags
```

`angular.json` fileReplacements wire env per `--configuration` flag.

#### 6.5.7 Epic-to-Directory Mapping

Maps PRD epics to concrete backend slices + frontend features:

| Epic (PRD) | Backend slice | Frontend feature | Shared components |
|---|---|---|---|
| **User onboarding & auth** | `Slices/Users/Register` + `Slices/Users/VerifyEmail` + `Slices/Users/SignIn` | `apps/owner-workspace/src/app/auth/` | `libs/core/state/current-user.service.ts` |
| **Journal entries (core ledger)** | `Slices/JournalEntries/*` (Create/Reverse/List/GetById) | `apps/owner-workspace/src/app/features/journal-entries/` | `libs/domain-ui/journal-entry/` + `libs/domain-ui/money/` |
| **Accounts management** | `Slices/Accounts/*` | `apps/owner-workspace/src/app/features/accounts/` | — |
| **Public statement share** | `Slices/PublicStatement/GetByToken` + `Slices/Accounts/GenerateCapabilityToken` | `apps/public-statement/src/app/statement/` + share UI inside `apps/owner-workspace/src/app/features/accounts/` | `libs/domain-ui/statement/` (shared between both apps — **this is why domain-ui exists**) |
| **Monthly close / period boundaries** | `Slices/Periods/*` | `apps/owner-workspace/src/app/features/dashboard/` | `libs/domain-ui/date/month-selector.component.ts` |
| **Multi-currency support** | Cross-cutting — `Domain/Money/` + slice-internal money handling | Cross-cutting — `libs/domain-ui/money/money-display.component.ts` + `libs/design-system/forms/money-input.component.ts` | `libs/core/resources/` decimal.js helpers |
| **i18n (AR/EN/FR + RTL)** | Cross-cutting — error code Transloco keys in `ValidationCatalog` | Cross-cutting — `libs/core/state/locale.service.ts` + per-scope i18n files | — |

**Rule when adding a new epic:** the first scaffolding commit creates both sides in lockstep — backend slice folder + frontend feature folder + optional `domain-ui` entries if cross-app. CI fitness function (future) verifies any new `Slices/<Slice>/` folder has a corresponding `apps/owner-workspace/src/app/features/<slice-kebab>/` or an explicit exemption.

#### 6.5.8 Integration Boundaries

**API Boundaries (external):**

| Boundary | Path pattern | Auth | Cacheability |
|---|---|---|---|
| Owner-facing API | `/v1/*` | Cookie session + CSRF | `no-store` |
| Public statement API | `/v1/public/statement/{token}` | Opaque capability token | Cacheable by token (CDN — D4.6) |
| Health/metrics | `/health`, `/metrics` | Network-restricted | `no-store` |
| OpenAPI docs | `/openapi/*` | Public (non-prod only) | Long-TTL cache |

**API Boundaries (internal via Wolverine):**
- Same-process dispatch now (D4.12 Postgres transport)
- Outbox table in same DB transaction as domain mutations — zero ghost messages
- Reversal trigger: first cross-process consumer → Azure Service Bus Standard

**Component Boundaries (frontend):**
- App ↔ lib: via `@ds/*`, `@core/*`, `@du/*`, `@contracts/*` aliases — deep imports forbidden
- Feature ↔ feature: forbidden; cross-feature state in `libs/core/state/`
- ESLint enforcement via `@typescript-eslint/no-restricted-imports` rules derived from Step 5 B3

**Component Boundaries (backend):**
- Slice ↔ slice: forbidden (NetArchTest enforced)
- Slice ↔ Domain: allowed
- Slice ↔ Infrastructure: via DI interfaces defined in slice `_Shared/`
- `_Shared/` contents: never referenced outside owning slice

**Data Boundaries:**
- Postgres is single source of truth (D4.2 — no Redis)
- EF Core DbContext is the only write path outside migrations
- Migrations are append-only; rollback via forward-migration, never destructive down
- CDN layer caches public-statement responses only (by capability token)
- No cross-slice tables without an explicit ADR entry

#### 6.5.9 Internal Communication Patterns

| Pattern | When used | Transport |
|---|---|---|
| HTTP request/response | Client → Api | ASP.NET + FastEndpoints |
| In-process messaging (commands) | Endpoint → handler (when endpoint explicitly dispatches) | Wolverine in-memory |
| Domain events | Aggregate mutation → downstream projection | Wolverine Postgres transport + outbox |
| Scheduled work | Idempotency cache cleanup, capability-token TTL expiry | Wolverine scheduler |
| Frontend reactivity | UI state updates | Angular Signals + rxResource |
| Frontend cross-feature state | Current user, locale, notifications | `libs/core/` signal services |

#### 6.5.10 External Integrations (MVP scope)

| Integration | Status | Location | Notes |
|---|---|---|---|
| Email (verification, password reset) | Stub in MVP | `Infrastructure/External/EmailClient.cs` | Concrete provider selection deferred; logs in dev, stubbed in staging |
| Payment Service Provider | Deferred post-MVP | reserved namespace `Infrastructure/External/Psp/` | — |
| KYC / compliance | Deferred post-MVP | reserved namespace `Infrastructure/External/Kyc/` | MENA+EU reg-differentials — ADR when triggered |
| OpenTelemetry sink | Azure Application Insights in prod; console in dev | `Infrastructure/Observability/` | Via ServiceDefaults |
| Azure Key Vault | Prod + staging secret resolution | `ServiceDefaults` | — |

#### 6.5.11 Development Workflow Integration

**Adding a new slice (backend + frontend in lockstep):**

1. Backend: create `Slices/<NewSlice>/_Shared/<Aggregate>.cs` + repository interface + errors + initial action folder
2. Backend: add EF configuration in `Infrastructure/Persistence/Configurations/`
3. Backend: run `dotnet ef migrations add Add<NewSlice>`
4. Backend: write unit + integration tests
5. Frontend: run `npm run gen:contracts` after backend OpenAPI changes
6. Frontend: create `apps/owner-workspace/src/app/features/<new-slice-kebab>/` with routes + pages + services
7. Frontend: add Transloco keys in per-feature `i18n/` folder
8. Frontend: write unit tests (Vitest) + E2E (Playwright)
9. Update `docs/development/slice-cookbook.md` if new pattern emerges

**Build artifacts:**
- Backend: `dotnet publish` → Docker image → Azure App Service
- Frontend: `ng build <app> --configuration production` → `dist/<app>/` → Azure Static Web App (owner) / Azure Blob + Front Door (public)

**Deployment flow:**
- Push to `main` → staging auto-deploy (via `main.yml`)
- Tag `v*.*.*` → production deploy (via `release.yml`)
- Rollback: redeploy previous tag; DB changes roll forward via new migration

#### 6.5.12 One-Way Doors in S4

| Decision | Reversibility | Revisit trigger |
|---|---|---|
| `ng-openapi-gen` for TS contracts generation | Moderate — switching generators creates a large one-time diff | Generated output becomes unwieldy OR gen-tool goes unmaintained |
| Single-repo Aspire AppHost for local dev | Low — standalone docker-compose is easy fallback | If Aspire's local-dev friction exceeds its orchestration value |
| GitHub Actions for CI/CD | Moderate — migration to Azure DevOps Pipelines mostly mechanical | Team adopts Azure DevOps for other reasons |
| ADR renumbering at Step 6 (007-010 consolidation) | Low — all ADRs still in draft | N/A |

#### 6.5.13 New Revisit Triggers (S4)

| ID | Trigger | Meaning |
|---|---|---|
| RT-30 | `gen-contracts.ps1` runtime exceeds 30s OR generator churn causes weekly contract-only PR floods | Consider switching to `openapi-typescript` + hand-rolled services, or adopt a contract-first OpenAPI definition file |
| RT-31 | PR CI time exceeds 12 min on 95th percentile OR flaky E2E blocks merges weekly | Split E2E into PR-gated critical-path + nightly-full suite |
| RT-32 | Azure Bicep templates exceed ~800 lines OR environment drift appears | Introduce Terraform OR adopt Azure Verified Modules more aggressively |

### 6.6 Step 6 Summary

**Produced:**
- Complete project tree across 4 batches (S1 repo-top + S2 backend + S3 frontend + S4 cross-cutting)
- 13 root-config files inventoried
- 5 .NET projects + 3 test projects in backend solution
- 2 Angular apps + 4 libraries in frontend workspace
- 7 PRD epics mapped to concrete backend slices + frontend features + shared components
- 3 boundary categories articulated: API (external + internal) + Component (frontend + backend) + Data
- Contracts generation pipeline fully described with stale-detection CI guard
- Local dev single-command story via Aspire AppHost
- Docker + Azure Bicep infrastructure layout
- Documentation structure with planned ADR-007/008/009/010 consolidation

**Total revisit triggers now registered:** RT-01 through RT-32 — 14 from Step 4, 3 from Step 5, 9 from Step 6.

**Step 6 closes with the project tree fully walkable from a single reading of `architecture.md`.**

---

## 7. Architecture Validation Results

### 7.1 Validation Approach

Option β layered validation — four sequential batches, each gated on explicit user acceptance: **V1 Coherence → V2 Coverage → V3 Readiness → V4 Gap Analysis & Resolution**. The V4 draft was subsequently hardened, scope-cut, re-parameterized, and pruned through **seven Advanced Elicitation rounds** applied sequentially: Pre-mortem Analysis → Red Team vs Blue Team → Challenge from Critical Perspective → Shark Tank Pitch → 5 Whys Deep Dive → Hindsight Reflection → Self-Consistency Validation.

Each elicitation round produced traceable deltas in §7.5 (resolution plan), §7.9 (revisit triggers), and §7.10 (proposed ADRs). The commit shape reflects **~85% self-consistent first-principles reconstruction** with ~15% elicitation-sourced additions, each marked contingent where composed rather than derived.

### 7.2 Coherence Validation (V1) ✅

**Decision Compatibility:** All Category 1-6 decisions verified to compose without conflict. Technology versions compatible (Angular 21 zoneless + Signals + Signal Forms; PrimeNG 21 + Tailwind 4; .NET 10 + FastEndpoints 7.x + Wolverine + Aspire 13; Postgres Flexible Server + temporal_tables; Keycloak 26+ on Azure Container Apps).

**Pattern Consistency:** Vertical-slice mirror between backend (`Accountantim.Api/<Slice>/`) and frontend (`apps/owner-workspace/src/app/features/<slice>/`) holds across all 9 PRD epic categories. RFC 9457 Problem Details envelope flows end-to-end through FastEndpoints emission → Signal Forms `applyServerErrors` → Transloco ValidationCatalog lookup.

**Structure Alignment:** Repository layout (`backend/` + `frontend/` + `infra/` + `docs/`) supports both Angular workspace and .NET solution conventions. Aspire AppHost orchestrates local dev across the polyglot stack.

**T1 Resolution (DDD + VSA reconciliation):** Research-validated consensus — Shared Kernel holds stable, aggregate-free primitives (Money, Currency, strongly-typed IDs, Integration Event contracts); each slice owns its own `Domain/` folder containing its aggregate root + child entities + slice-local value objects. Enforced by **4 NetArchTest fitness tests** (§7.5.3) and recorded in **ADR-011** (§7.10).

**Ubiquitous Language Mapping:** PRD vocabulary (Debt / Contact / Statement / User) diverges from architecture vocabulary (JournalEntry / Account / Statement / Owner). Canonicalization policy recorded in **ADR-013**: public-facing surfaces follow PRD; internal code follows architecture; translation enforced via Glossary.cs string-ban scanner (§7.5.4).

### 7.3 Requirements Coverage Validation (V2) ✅

All 9 PRD functional-requirement categories (FR1-FR51) mapped to concrete architectural support: User Management, Contact Management, Debt Management, Currency & Exchange, Debt Transfer, Statements & Dashboard, Statement Sharing, Administration, Real-Time Notifications. Non-functional requirement categories (Performance, Security, Scalability, Accessibility, Integration, Reliability) covered by corresponding Category-3/4 decisions and §7.5 remediations.

**Gap classification from V2:**
- **C1-C7 Critical** — architectural decisions or spikes required before scaffold; all converted to §7.5 remediations or Day 0 gates.
- **I1-I9 Important** — specifications that can run parallel to first 2-3 slices; tracked as §7.5 items with deferral permitted.
- **N1-N6 Nice-to-have** — enhancements captured as revisit triggers (RT-33 through RT-46) rather than blocking remediations.

**SignalR stack addition:** FR50-51 (real-time notifications) necessitated SignalR addition to the Azure service topology; addressed in §4 with capacity sizing in §5.

### 7.4 Implementation Readiness Validation (V3) ⚠️ CONDITIONALLY-SCAFFOLDABLE

**Overall Status:** **CONDITIONALLY-SCAFFOLDABLE** — *not* "READY FOR IMPLEMENTATION." The downgrade is honest rather than pejorative: 2 outstanding spikes + 7 micro-ADRs + user-declared inputs mean the scaffold is ~85% ready; remaining ~15% is time-bounded remediation with clear completion criteria.

**Remediation Window:** ~6 **focused days** of pre-scaffold work. Calibrated honestly for solo intermediate-level developer with day-job and life-bandwidth costs: **~18-24 calendar days**. Calendar ceiling: **3 weeks** measured from remediation start. Ceiling breach at check-in triggers rating degrade to **NOT-READY / DEFER-SCAFFOLD** pending re-scoping.

**Confidence Level:** Medium-High. High on derived-decision surfaces; medium on elicitation-sourced additions (marked contingent in §7.5).

**Key Strengths:**
- 7 micro-ADRs grounded in V1/V2/V3 findings, each with explicit trade-off record
- 14 revisit triggers covering known one-way doors
- First slice definition deliberately exercises critical primitives (Money VO, idempotency, bitemporal write, cross-slice integration event)
- Abandonment branches pre-defined for each Day 0 gate (no success-theater)
- Elicitation rounds documented as process artifact enabling post-launch audit

**Known Weaknesses (named, not eliminated):**
- **Single-observer enforcement gap** — solo-dev + self-enforced CI gates + RTs form a system where the only observer is the same person who benefits from ignoring them. Mitigated (§7.5.6) via git pre-push hook + async-reviewer wiring; not eliminated.
- **FastEndpoints and Keycloak are single points of failure** — abandonment branches exist but migration cost materializes if invoked mid-scaffold.
- **Composed constants still present** — 3 user-declared TBDs (burn tolerance, signal milestones, pool safety_factor) block scaffold commit finality.

#### 7.4.1 Cost-Floor Check

Before committing to the full Azure stack at scaffold completion, calculate Month 1 zero-user floor across: ACA minimum replicas, Postgres Flexible Server tier, Azure Front Door, App Insights ingestion, Key Vault.

**Declared monthly burn tolerance:** **€0/month (free-tier-only stack)** over 12-month horizon.

**Descope cascade already applied — free-tier-only stack adopted at scaffold start:**
1. ~~Azure Front Door~~ → **Azure Static Web Apps free tier** (built-in global CDN) for both owner-workspace and public-statement SPAs
2. **App Insights** → free tier (5GB/month ingestion) with aggressive sampling
3. **Postgres Flexible Server** → Burstable B1ms (free 12 months on new Azure subscription)
4. **Azure Container Apps** → Consumption workload profile with scale-to-zero mandatory (idle replicas = €0)
5. **Azure Container Registry** → replaced with **GitHub Container Registry** (free public + private)
6. **Key Vault** → Standard tier (nearly-free at low transaction volume)
7. **Keycloak** → runs on shared ACA Consumption environment (scale-to-zero when idle)

**Architectural implication (flagged as §4 revision candidate — not retroactive):**
- **D4.19 Azure Front Door** → superseded by Azure Static Web Apps built-in CDN
- **D4.6 capacity targets (80rps sustained / 500rps burst for public-statement)** → attainable within ASWA free tier (100GB/month bandwidth); burst behavior unverified at free-tier, remains under RT-33
- These are not rewritten in §4 during Step 8 completion; invoke `bmad-correct-course` when ready to cascade the §4 edits formally

**Scaffold no-go gate:** If any single service in the free-tier stack exceeds its free allowance projection within the first 90 days, remediation pause + re-model + user decision: raise tolerance OR descope further OR defer that slice.

**12-month expiration cliff:** Postgres Burstable B1ms free benefit expires at Month 13. Post-expiration floor: **~€12-15/month** absolute minimum (just Postgres). Captured as **RT-47**.

#### 7.4.2 Signal Milestones

| Milestone | Target | Status |
|---|---|---|
| First-dogfood-user (self-use) | **Day 35 calendar** | Declared |
| First-external-beta-user | **Day 55 calendar** | Declared |
| Descope trigger (descope review fires if dogfood unreached) | **Day 42 calendar** | Declared |

*Mechanism derived from timeline-pressure-gate reasoning; calendar dates declared by Wahid on 2026-04-21 as sensible defaults for solo-dev fintech MVP cadence. Day 42 = 7-day buffer past dogfood target — tight enough to force attention on remediation slip, loose enough for normal life-interruption.*

### 7.5 Resolution Plan

#### 7.5.0 Pre-Scaffold Build-vs-Adopt Falsification Search

**2-hour timebox, precedes Day 0 gates.**

Framed as an **open assumption** — the scaffold defaults to "we are building this from scratch"; this search exists to **falsify** that default. Targets: GitHub search for Nx workspace + FastEndpoints + Wolverine + Keycloak-on-ACA starter templates.

**Adopt threshold:** any template that replaces ≥40% of Day 0 + Day 1-5 effort. If adopted, §7.5.1-§7.5.3 are re-scoped against the template's baseline. If no template clears the threshold, proceed to §7.5.1.

**Deliverable:** one-line adoption decision recorded inline at §7.5.0 before Day 0 gates commence.

#### 7.5.1 Day 0 Gates (Two Spikes)

Both gates must produce a go/no-go decision before first slice ships.

**Gate 1 — FastEndpoints × .NET 10 source-gen spike.**
*Validation target:* source-gen + endpoint discovery + RFC 9457 Problem Details emission + OpenAPI contract generation compose cleanly on .NET 10.
*Timebox:* 3 days maximum.
*Abandonment branch:* minimal API pre-evaluated as primary fallback → Carter / Fast-alt libraries as tertiary → defer source-gen to Phase 2 if neither resolves within 5 days cumulative.
*Revisit trigger:* **RT-40**.

**Gate 2 — Keycloak-on-ACA session-affinity spike.**
*Validation target:* session persistence survives ACA replica autoscale events; sticky-session cookie vs externalized session store decision resolved.
*Timebox:* 3 days maximum.
*Abandonment branch:* IdentityServer (self-host) as primary fallback → Auth0 (managed) as tertiary; both pre-evaluated for feature parity with §4.5 requirements.
*Revisit trigger:* **RT-39**.

*ADR-017 (dual-endpoint-convention proof) and ADR-018 (integration event governance) were elicitation-surfaced as candidate Day 0 gates and subsequently **demoted to post-Slice-1 triggers** — they become urgent when their respective subjects materialize in real cross-slice traffic, not before.*

#### 7.5.2 First Slice Definition

**Primary:** Contacts/Create with opening-balance.
*Rationale:* exercises Money VO + idempotency with financial consequence + RFC 9457 error emission + bitemporal write + emits `ContactCreated` integration event consumed by a no-op Debts projection handler (exercises NetArchTest fitness test #3 end-to-end).

**Fallback:** Debts/Create if PRD constraints preclude opening-balance on Contact.

*Revisit trigger:* **RT-41** — if first slice fails to exercise idempotency with financial consequence, replace slice or add a second gate slice before proceeding.

#### 7.5.3 Fitness Tests (4, CI-Wired)

| # | Invariant | Enforcement |
|---|---|---|
| 1 | Shared Kernel purity — no dependency on `Accountantim.Api` | NetArchTest |
| 2 | Slice isolation — no cross-slice `Domain/` dependencies | NetArchTest |
| 3 | Cross-slice communication only via `Accountantim.Domain.Events.IntegrationEvents` | NetArchTest |
| 4 | Shared Kernel holds no `IAggregateRoot` types | NetArchTest |

**Enforcement convention — CI-gate-first rule:** for each fitness test, CI wiring (required check) lands **before** test content is marked done. Authored-but-unwired is treated as unfinished.

**Local mechanism:** git pre-push hook blocks feature PR if `tests/Architecture/` contains uncommitted authored tests. *Chosen mechanism for solo-dev context; alternatives: pre-commit hook, CI required-check only — swap per workflow preference.*

**Fitness test #5 (Shared Kernel type whitelist) — NOT implemented.** Preserved as manual norm in ADR-011 ("adding types to SK requires ADR amendment"); automated implementation deferred until first drift incident provides signal that automation is warranted.

#### 7.5.4 Glossary Executable Contract

**Implementation:** `Glossary.cs` in Shared Kernel — `public static class UL { public const string Owner = "owner"; ... }` — paired with a string-ban fitness scanner.

**Scope at scaffold time — 3 surfaces only:**
1. API routes
2. Claim names (Keycloak claim mappings)
3. Postgres column names

*Rationale:* these three surfaces cover **wire-format + identity + persistence** — the surfaces where UL drift becomes externally wire-breaking. Additional surfaces (OTel attributes, Wolverine envelope headers, Transloco keys, migration filenames, log message templates) added reactively on first drift incident (RT-34).

#### 7.5.5 Ubiquitous Language Mapping (ADR-013)

| PRD Term | Architecture Term | Surface Policy |
|---|---|---|
| Debt | JournalEntry | Internal code uses JournalEntry; public API routes/labels use Debt |
| Contact | Account | Internal code uses Account; public API routes/labels use Contact |
| Statement | Statement | No divergence |
| User | Owner | Internal code uses Owner (domain); public claim mapping uses user_id where identity-platform convention requires |

Enforced via §7.5.4 Glossary.cs + string-ban scanner on the 3 scaffold-time surfaces.

#### 7.5.6 Single-Observer Risk — Explicit Acknowledgment

**Risk:** In solo-dev context, all fitness tests, revisit triggers, and CI gates are observed and enforced by the same person who benefits from ignoring them. Self-enforcement has a single failure mode: the observer forgets, defers, or disables.

**Mitigations (layered, not eliminated):**
- **Git pre-push hook** — blocks feature PR if architectural tests are uncommitted (§7.5.3)
- **Async reviewer wiring** — async reviewer of choice (Claude Code agent / ChatGPT / human async reviewer) reviews all scaffold-era PRs; not a scheduled ritual but an event-driven hook
- **Weekly status commit** — **NOT adopted.** Previously proposed; dropped because weekly rituals are uncompliable for a solo dev with life-bandwidth noise

**Revisit trigger RT-36** — any NetArchTest disabled >48h without an ADR amendment triggers session-wide architecture review.

#### 7.5.7 Ops-Readiness Deferral (Explicit)

**Not ready at scaffold completion — intentionally deferred to Phase 2:**
- No runbook for production incidents
- No alerting thresholds wired
- No on-call plan
- No runtime SLO dashboard

**Trigger to initiate Phase 2 ops-readiness:** first production user acquired → **RT-44** fires.

*This is named explicitly rather than hidden. Attempting to gold-plate operations before the first user is premature. Attempting to gold-plate after is also risk — RT-44 exists to force the conversation at the right moment.*

#### 7.5.8 Seed-Data, Backup, Rotation (Pre-First-Paying-User Gates)

Three items caught by Self-Consistency round — fresh-reconstruction would derive them from V2 coverage, but they were missed by elicitation rounds until the final check:

1. **Dogfood seed-data strategy** — authored before Day 25 dogfood milestone. Approach options (pick one): hand-authored seed SQL / Wolverine seed job / dev-mode endpoint. Output: `docs/development/dogfood-seed.md` + actual seed artifact.
2. **Backup/restore rehearsal — RT-46 hard gate.** One full Postgres point-in-time-restore rehearsal completed before first paying user. Drill includes KMS-encrypted field round-trip validation (envelope encryption per-subject DEK rotation survives restore).
3. **Secrets rotation dry-run — ADR-020.** Keycloak signing-key rotation + Key Vault KEK rotation rehearsed end-to-end before first paying user. No rotation attempted in production without prior rehearsal.

#### 7.5.9 Additional §7.5 Items (Late-Caught Gaps)

- **Transloco key governance policy** — authored before first public-statement slice ships. Covers RTL (Arabic) + FR variant drift on shared form-error keys. Output: `libs/core/i18n/TRANSLOCO_GOVERNANCE.md` + validation pipeline in CI.
- **Postgres pool sizing floor** — `MaxPoolSize` set in initial ACA manifest via derivation formula: `MaxPoolSize = (Postgres max_connections budget) ÷ (ACA max_replicas) ÷ safety_factor`, where **`safety_factor = 2.0`** (balanced fintech default; declared 2026-04-21). Example at Postgres Burstable B1ms (max_connections = 85) + ACA max_replicas = 3 → MaxPoolSize ≈ 14 per replica, leaving ~30 connection headroom for Keycloak + Wolverine worker + admin tooling. Prevents autoscale-driven pool exhaustion (hindsight-caught Month 2 pain).

### 7.6 Architecture Completeness Checklist

**✅ Requirements Analysis**
- [x] Project context thoroughly analyzed (PRD + product briefs + UX spec)
- [x] Scale and complexity assessed (MENA+EU fintech, solo dev, daily-use)
- [x] Technical constraints identified (Azure Europe, 180 KB gz, LCP<2.5s, 80rps/500rps public path)
- [x] Cross-cutting concerns mapped (auth, i18n, audit, observability, security)

**✅ Architectural Decisions**
- [x] Critical decisions documented with versions (§1-§4)
- [x] Technology stack fully specified (§3)
- [x] Integration patterns defined (§4.9-§4.12 error/versioning/idempotency/transport)
- [x] Performance considerations addressed (§4.2 Postgres, §4.6 CDN, §5 capacity)

**✅ Implementation Patterns**
- [x] Naming conventions established (§6 + §7.5.5 UL mapping)
- [x] Structure patterns defined (§6 vertical-slice mirror)
- [x] Communication patterns specified (§4.9 RFC 9457, §4.12 Wolverine events)
- [x] Process patterns documented (§4.11 idempotency, §7.5.3 fitness tests)

**✅ Project Structure**
- [x] Complete directory structure defined (§6 repo tree all 4 batches)
- [x] Component boundaries established (§6.5.8 integration boundaries)
- [x] Integration points mapped (§6.5.9-§6.5.11)
- [x] Requirements-to-structure mapping complete (§6.5.7)

**✅ Validation**
- [x] Coherence verified (§7.2)
- [x] Requirements coverage verified (§7.3)
- [x] Implementation readiness assessed honestly (§7.4 — CONDITIONALLY-SCAFFOLDABLE)
- [x] Gap analysis and resolution plan documented (§7.5)
- [x] Elicitation-round hardening applied (7 rounds) with contingency markers

### 7.7 Readiness Assessment Summary

**Overall Status:** CONDITIONALLY-SCAFFOLDABLE
**Confidence:** Medium-High
**User-declared inputs (resolved 2026-04-21):** burn tolerance €0/month (free-tier-only), dogfood Day 35, beta Day 55, descope-trigger Day 42, pool safety_factor 2.0 — ✅ cleared
**Blockers before first slice ships:** 2 Day 0 gates (FastEndpoints spike, Keycloak spike) + scaffold-search falsification task
**Blockers before first paying user:** RT-46 backup-restore rehearsal + ADR-020 rotation dry-run
**§4 revision candidates triggered by €0 tolerance:** D4.19 (Azure Front Door → Azure Static Web Apps) + free-tier substitutions for D4.6/ACR/App Insights — cascade via `bmad-correct-course` when ready

### 7.8 Implementation Handoff

**AI Agent Guidelines:**
- Follow all architectural decisions as documented; when two sections disagree, **§7 overrides earlier sections** (it was validated against them)
- Fitness tests (§7.5.3) are hard gates, not advisory — disabling any requires ADR amendment
- §7.5.0 scaffold-search precedes Day 0 gates — do not skip even if it returns empty
- The 3 user-declared TBDs in §7.4.1 / §7.4.2 / §7.5.9 must be filled before scaffold commit; do not substitute placeholder values

**First Implementation Priority (ordered):**
1. Complete §7.5.0 scaffold-search (2h timebox) — may restructure all subsequent items
2. Execute Day 0 Gate 1 (FastEndpoints spike) + Day 0 Gate 2 (Keycloak spike) — serial or parallel based on available focused time
3. Author 4 fitness tests + wire to CI (CI-gate-first rule)
4. Implement first slice: Contacts/Create with opening-balance (§7.5.2)
5. Emit `ContactCreated` integration event + no-op Debts projection handler consumer
6. Author §7.5.4 Glossary.cs + 3-surface string-ban scanner
7. Author §7.5.5 ADR-013 UL mapping as micro-ADR
8. Proceed to slice #2 (per PRD epic priority) only after fitness tests #1-#4 all green in CI

### 7.9 New Revisit Triggers (S7)

| ID | Trigger | Action |
|---|---|---|
| RT-33 | Shared Kernel aggregate-ban violation detected (fitness test #4) | Re-examine slice boundary; move aggregate to slice `Domain/` |
| RT-34 | Ubiquitous Language drift observed on a surface not covered by 3-surface scanner | Expand Glossary scanner to that additional surface |
| RT-35 | Ops incident in first 30 production days | Accelerate §7.5.7 ops-readiness work ahead of organic RT-44 |
| RT-36 | NetArchTest disabled >48h without ADR amendment | Session-wide architecture review |
| RT-37 | First cross-slice integration event contract breakage | ADR-018 (Integration Event Governance) authoring becomes urgent |
| RT-38 | First slice ships >Day 30 calendar from remediation start | §7.5 remediation descope review mandatory |
| RT-39 | Keycloak-on-ACA Day 0 gate exceeds 3-day timebox | Invoke abandonment branch: IdentityServer / Auth0 |
| RT-40 | FastEndpoints Day 0 spike inconclusive beyond 3 days | Switch to minimal API pre-evaluated fallback |
| RT-41 | First slice fails to exercise idempotency with financial consequence | Replace first slice or add second gate slice before proceeding |
| RT-43 | Cross-slice integration event envelope shape diverges between slices | ADR-018 authoring escalated to immediate |
| RT-44 | First production user acquired | Begin Phase 2 ops-readiness (runbook + alerts + on-call + SLO dashboard) |
| RT-45 | Actual Azure Month 1 bill >20% over declared tolerance after 3 months live | Mandatory stack descope review |
| RT-46 | First paying user within 7-day window | Backup/restore rehearsal completion (hard gate; blocks user onboarding if unmet) |
| RT-47 | Month 13 reached (12-month Azure free-tier benefits expire — primarily Postgres Burstable B1ms) | Mandatory tolerance revision + stack re-model against new floor (~€12-15/month absolute minimum) |

*RT-42 intentionally absent — superseded by git pre-push hook mechanism (§7.5.3).*

**Cumulative revisit triggers now registered:** RT-01 through RT-47 (14 from Step 4, 3 from Step 5, 9 from Step 6, 15 from Step 7 = 41 total, minus RT-42 = **40 active**).

### 7.10 Proposed ADRs (Micro Format)

**Tiered convention adopted:** *micro-ADR* = 1-page format (Decision / Reason / Consequence) for internal decisions; full-form ADR reserved for decisions requiring external consensus. Tiering reduces authoring cost ~60% for intermediate-level solo dev while preserving decision-trail integrity.

| ID | Title | Format | Scope |
|---|---|---|---|
| ADR-011 | Shared Kernel architecture (DDD + VSA reconciliation) | Micro | Domain organization + fitness test enforcement |
| ADR-013 | Ubiquitous Language mapping (PRD ↔ Architecture) | Micro | Translation table + 3-surface enforcement |
| ADR-016 | Fitness-function enforcement policy | Micro | CI required-check + local git pre-push hook |
| ADR-017 | Dual-endpoint-convention proof (post-Slice-1 trigger) | Micro | FastEndpoints / minimal API parity — authored when second endpoint convention needed |
| ADR-018 | Integration Event Contract Governance (post-Slice-1 trigger) | Micro | Versioning + retry policy + dead-letter escalation — authored when first cross-slice consumption materializes |
| ADR-019 | Bitemporal schema-evolution playbook | Micro | Additive vs breaking shape-change paths on temporal tables |
| ADR-020 | Secrets rotation runbook | Micro | Keycloak signing-key rotation + Key Vault KEK rotation |

### 7.11 Section 7 Summary

**Validation methodology:** Option β layered (V1 Coherence + V2 Coverage + V3 Readiness + V4 Gap Analysis), hardened through 7 Advanced Elicitation rounds and signed off with user confirmation at each batch.

**Final shape:**
- **Day 0 gates:** 2 (FastEndpoints spike + Keycloak-on-ACA spike) — each with pre-defined abandonment branches
- **Fitness tests CI-wired:** 4 + 1 manual norm (ADR-011)
- **Glossary scanner surfaces:** 3 at scaffold time (routes, claims, DB columns) — 7-surface expansion reactive
- **Proposed micro-ADRs:** 7 (ADR-011, 013, 016, 017, 018, 019, 020)
- **Revisit triggers:** 14 new (RT-33 through RT-46, minus RT-42) — total registry: 39 active
- **Readiness rating:** CONDITIONALLY-SCAFFOLDABLE with calendar-ceiling degrade rule

**User-declared inputs (filled 2026-04-21):**

```yaml
monthly_burn_tolerance_eur: 0        # §7.4.1 — free-tier-only stack; RT-47 at Month 13
dogfood_target_day_calendar: 35      # §7.4.2
external_beta_target_day_calendar: 55  # §7.4.2
descope_trigger_day_calendar: 42     # §7.4.2 — 7-day buffer past dogfood
pool_safety_factor: 2.0              # §7.5.9 — balanced fintech default
```

**Architectural implication:** €0/month triggers §4.19 Azure Front Door → Azure Static Web Apps free-tier CDN swap + 6 other free-tier stack substitutions (detailed in §7.4.1). These are **§4 revision candidates** — flagged for `bmad-correct-course` pass when ready to cascade formally; not retroactively edited during Step 8 completion.

#### 7.11.1 Day-180 Voice — Hindsight Anchor Appendix

*From the Hindsight Reflection round. Kept as a reference predicting which §7 items will earn their keep vs. prove to be ceremony, so post-launch retrospective can audit.*

**Predicted load-bearing (will have saved pain by Day 180):**
- Day 0 spikes (FastEndpoints + Keycloak) — real decisions that couldn't be deferred; session-affinity discovery alone prevents a prod 3am page
- Fitness tests #1 (SK purity) + #2 (slice isolation) CI-wired — catch late-night solo-dev drift at the PR gate
- First slice = Contacts/Create with opening balance — exercises Money + idempotency + bitemporal + integration event before compounding complexity
- Micro-ADR format — keeps decision record without dissertation cost
- §7.4.1 cost-floor check — prevents Month 1 invoice panic

**Predicted ceremony (likely ignored or redundant):**
- Async-reviewer ritual — aspirational; adoption depends on daily habit not architectural mandate
- Day-180 voice appendix itself — ironic, but likely unread after Month 1
- Signal-milestone dates (if over-specified without user input) — generic dates without personal commitment have no gravity

**Predicted late-caught gaps (Self-Consistency round rescued these — kept in §7.5.8):**
- Dogfood seed-data strategy
- Backup/restore rehearsal before first paying user (RT-46)
- Secrets rotation dry-run (ADR-020)

---

**Step 7 closes with a validated, honestly-rated architecture document — CONDITIONALLY-SCAFFOLDABLE pending 3 user-declared inputs and 2 Day 0 spikes.**
