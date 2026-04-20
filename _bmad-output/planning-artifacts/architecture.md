---
stepsCompleted: [1, 2, 3]
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
