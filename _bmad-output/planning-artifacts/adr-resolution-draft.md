---
title: ADR Resolution Draft — Adversarial Review Findings
date: 2026-04-21
merged_date: 2026-04-22
author: Wahid (with Claude assistance)
status: MERGED — all R-01..R-10 folded into architecture.md and prd.md; R-07 landed under the reconciled "cache-sizing posture" pattern (500 rps retained as explicit cache-sizing design posture — not an SLO, not an autoscale trigger) rather than the strict-removal variant proposed in the original draft below; R-09 adds RT-48 (French locale trigger) to the main RT table. Retained as historical record of the resolution pass.
target_documents:
  - _bmad-output/planning-artifacts/architecture.md
  - _bmad-output/planning-artifacts/prd.md
source: Adversarial Review (bmad-review-adversarial-general) on architecture.md
---

# ADR Resolution Draft

This document proposes resolutions for the 10 findings classified as **Blocker** or **Contract-Drift** in the 2026-04-21 adversarial review. Each entry is a self-contained mini-ADR with a concrete edit list. Nothing in `architecture.md` or `prd.md` has been changed yet — that happens after review.

**Legend**

- **R-01…R-05** — Blocker-class resolutions (new ADRs required).
- **R-06…R-10** — Contract-drift reconciliations (amendments, no new ADR).
- **ADR-021…ADR-025** — New decision records (next free numbers after the existing ADR-001…ADR-020 sparse range).

---

## R-01 · Identity Provider — Replace Keycloak with Auth0 Free Tier

**Finding (AR-06):** Keycloak on scale-to-zero ACA has a 20–40s JVM cold-start, incompatible with PRD NFR-P1 LCP <2.5s on the login/public-statement routes.

**Decision:** Use **Auth0 free tier** (7,500 MAU, unlimited logins) as the MVP identity provider. Keycloak is removed from the scaffold entirely.

**Rationale:**
- Eliminates cold-start conflict — Auth0 is a managed edge service.
- Reclaims one container slot on ACA (reduces free-tier squeeze).
- No MVP feature requires a self-hosted IDP; Auth0 covers OIDC, MFA, password reset flows.
- Keycloak was a DiTracker carryover, not a requirement.

**Consequences:**
- New dependency on a third-party identity vendor (acceptable at MVP).
- OIDC integration via `Microsoft.AspNetCore.Authentication.OpenIdConnect`.
- Custom claims (tenant, ProcessingActivity hints) via Auth0 Actions.
- Magic-link flow (FR42) shifts to Auth0 Passwordless — removes need for a separate email-link implementation on our side.
- **Auth0 Custom Domain on `faktuboh.com`** (decision 2026-04-21): hosted login served at `auth.faktuboh.com`. Requires CNAME + TXT DNS records and Auth0-managed certificate provisioning at tenant setup. All callback URLs, token audiences, and CORS origins must be registered against this host from day one (changing later invalidates all issued tokens).

**Revisit trigger (RT-new):** MAU crosses 5,000 (approaching 7,500 cap) **or** Auth0 Essentials pricing exceeds €25/month at projected volume.

**Edits required:**
- `architecture.md §4` — replace Keycloak references with Auth0 in decision D4.x (identity). Record custom domain: `auth.faktuboh.com`.
- `architecture.md §6.5` — remove Keycloak container from ACA topology; add Auth0 SDK config slot; document callback/audience registration checklist.
- `architecture.md §7.4.1` — drop Keycloak warm-up concern from cost/edge section.
- Add **ADR-021: Identity Provider — Auth0 Free Tier for MVP (Custom Domain: auth.faktuboh.com)**.
- Supersede any prior Keycloak-specific ADR (mark as SUPERSEDED, don't delete).

---

## R-02 · Bitemporal Audit — Application-Level, Not `temporal_tables`

**Finding (AR-14):** Azure PostgreSQL Flexible Server does not allow the `temporal_tables` extension. D4.1 (bitemporal storage) is blocked.

**Decision:** Implement bitemporal storage at the **application layer** using EF Core `SaveChanges` interceptors plus `<entity>_history` companion tables. Entities opt in via the marker interface **`IBitemporal`**. No PG extension required.

**Rationale:**
- Keeps Azure PG Flexible Server as the managed-DB choice.
- Pattern is well-documented, deterministic, and testable.
- Avoids DB lock-in to any provider that happens to allow `temporal_tables`.
- Marker interface (`IBitemporal`) over attribute: interceptor uses `entry.Entity is IBitemporal` — no reflection, no attribute lookup on the hot path. Also more testable (can be mocked/faked per entity in unit tests).

**Opt-in contract:**
```csharp
public interface IBitemporal
{
    DateTimeOffset ValidFrom { get; set; }
    DateTimeOffset? ValidTo { get; set; }
    DateTimeOffset RecordedAt { get; set; }
}
```

**Schema shape (per entity implementing `IBitemporal`):**
- Source table carries `valid_from timestamptz NOT NULL`, `valid_to timestamptz NULL`, `recorded_at timestamptz NOT NULL`.
- History table (`<entity>_history`) is insert-only; interceptor writes previous row on UPDATE/DELETE.
- Read-side: `AsOf(DateTimeOffset)` query extension returns the view at a given wall-clock.

**Consequences:**
- Modest perf cost (one extra insert per mutation on audited entities).
- Developer discipline required — interceptor must be registered on every `DbContext`.
- Fitness test: "audit round-trip" must verify history row is written for every update to a flagged entity.

**Revisit trigger (RT-new):** Any migration to a DB that supports `temporal_tables` natively (e.g., SQL Server, SQL Edge, or if Azure PG ever allows the extension).

**Edits required:**
- `architecture.md §4` — reword D4.1 from "PostgreSQL temporal_tables extension" to "Application-level bitemporal via EF Core interceptors + `*_history` tables."
- `architecture.md §5` — add bitemporal convention block (column names, interceptor contract, `AsOf` query shape).
- `architecture.md §7.4` — add "audit round-trip" fitness test.
- Add **ADR-022: Bitemporal Storage — Application-Level Pattern**.

---

## R-03 · Email Provider — Resend Free Tier, Replace Stub

**Finding (AR-05):** PRD FR3 (password reset email) and FR42 (20 magic-link emails/user/hour) are unresolvable against the architecture's stubbed email provider. With R-01 (Auth0 Passwordless), most magic-link traffic shifts off our infra, but transactional emails remain.

**Decision:** Use **Resend** (`resend.com`) on the free tier (3,000 emails/month, 100/day). All transactional emails (non-auth notifications) route through Resend. Auth-related emails (password reset, magic links) are delegated to Auth0 per R-01.

**Rationale:**
- Free tier is well inside MVP volume (expected ~500 emails/month at beta).
- Modern DX: DKIM/DMARC setup in minutes; simple REST API; first-class .NET client.
- Separating auth-email (Auth0) from transactional-email (Resend) is a clean seam and matches each vendor's strength.

**Consequences:**
- One more vendor integration (offset by R-01 removing Keycloak email config).
- Environment variable `RESEND_API_KEY` joins the secrets list.
- Failure mode: if Resend is down, transactional emails queue in Wolverine's outbox and retry — **no lost sends**.

**Revisit trigger (RT-new):** Monthly volume exceeds 2,500 emails (≥83% of free tier) **or** Resend Pro pricing becomes material.

**Edits required:**
- `architecture.md §6.5.10` — remove "stub email; console log only" language; insert Resend integration slot with Wolverine-outbox pattern. Configure **EU-region sending** (`region: eu-west-1`) for GDPR data residency.
- `architecture.md §4` — add D4.x: Transactional email provider = Resend (EU region).
- `prd.md FR3` — note that password reset is delegated to the IDP (Auth0 per R-01), not a bespoke email flow.
- `prd.md FR42` — note that magic-link delivery is via Auth0 Passwordless; our per-user rate limiting piggybacks on Auth0's built-in brute-force protection with a safety-net middleware for any non-Auth0 links.
- Add **ADR-023: Transactional Email — Resend Free Tier (EU Region)**.

---

## R-04 · Server Framework — Drop FastEndpoints, Use Minimal APIs

**Finding (AR-10, AR-11):** FastEndpoints 7.x is marked "spike pending" yet embedded in example code. Compatibility with .NET 10 + Aspire 13 is unverified. Solo-dev schedule (Day 35 dogfood) cannot absorb a failed spike.

**Decision:** Build on **ASP.NET Core Minimal APIs** + **FluentValidation** + **source-generated OpenAPI**. Problem-details handling uses **.NET 10 built-ins** (`AddProblemDetails()` + `IExceptionHandler`) — no Hellang dependency. FastEndpoints is removed from scope; no spike required.

**Rationale:**
- Minimal APIs are first-party, guaranteed compatible with .NET 10 and Aspire 13.
- Vertical-slice folder layout (ADR-006) is framework-agnostic — slice pattern survives the swap.
- `TypedResults` + source-gen OpenAPI give strongly-typed responses without a third-party layer.
- FluentValidation covers input validation with the same ergonomic surface FastEndpoints would have provided.
- Saves the ~2-day spike window and removes one .NET 10 compat unknown.
- **Hellang.Middleware.ProblemDetails ruled out:** the library predates `AddProblemDetails()` (.NET 8+) and `IExceptionHandler` (.NET 8+). On .NET 10, built-in primitives cover ~90% of Hellang's original value. DIY is ~30 LOC (one `IExceptionHandler` per domain exception class + one `ProblemDetailsFactory` extension to stamp the `faktuboh.<slice>.<condition>` error code onto the `type` URI). Avoids a third-party dependency on the error-response path and keeps problem-details shape under our direct control.

**Consequences:**
- Slice files contain explicit `MapGroup`/`MapPost` registration instead of FE's discovery.
- Registration file per slice keeps routes co-located with handlers.
- Error-code convention (`faktuboh.<slice>.<condition>`) emitted via a custom `ProblemDetailsFactory` + per-exception `IExceptionHandler` implementations (one per domain-exception class). Centralized registration in `Program.cs`: `builder.Services.AddProblemDetails()` + `builder.Services.AddExceptionHandler<DomainExceptionHandler>()`.

**Revisit trigger (RT-new):** FastEndpoints publishes a version explicitly supporting .NET 10 + Aspire 13 **and** we have a concrete pain point that FE solves that Minimal APIs doesn't.

**Edits required:**
- `architecture.md §3` — remove FE spike from Day 0 spikes list; mark as resolved-by-decision.
- `architecture.md §5.3.1` — rewrite `CreateJournalEntryEndpoint` example as Minimal API handler + validator.
- `architecture.md §6.1` — update backend project structure to Minimal APIs conventions.
- `architecture.md §7` — drop FE compat from scaffoldability gate.
- Supersede ADR-003 (FE spike) with **ADR-024: Server Framework — Minimal APIs (supersedes ADR-003)**.

---

## R-05 · Secret/Key Management — Key Vault Standard, Not Premium HSM

**Finding (AR-15):** Envelope-encryption design (KEK/DEK/IV) with Key Vault Premium HSM violates the €0/month burn tolerance. Premium HSM is ~€3.30/key/month plus ops.

**Decision:** Use **Azure Key Vault Standard** (software-protected keys). KEK stored in Standard Key Vault; DEKs wrapped using the Key Vault crypto API. HSM protection deferred to post-MVP.

**Rationale:**
- Key Vault Standard is effectively free at MVP volume (€0.03/10K operations, <€1/month projected).
- FIPS 140-2 Level 1 is sufficient for pre-GA ledger; Level 3 (HSM) is compliance theater until we have enterprise customers asking.
- Envelope pattern (KEK wraps DEK; DEK encrypts payload) survives the HSM→software move unchanged — only the KEK's backing store changes.

**Consequences:**
- Compliance posture reads: "FIPS 140-2 Level 1, upgradeable to Level 3 via Key Vault Premium without schema or code change."
- Rotation cadence can remain quarterly; automation unchanged.

**Revisit trigger (RT-new):** First enterprise customer RFP asks for HSM-backed keys **or** a regulatory requirement changes.

**Edits required:**
- `architecture.md §4` — update envelope-encryption decision: "Key Vault Standard" replaces "Key Vault Premium HSM."
- `architecture.md §7.4.1` — remove HSM cost line; add Key Vault Standard line (~€1/mo).
- Add **ADR-025: Key Management — Key Vault Standard for MVP**.

---

## R-06 · Bundle Budget Reconciliation — 180 KB gz Wins

**Finding (AR-01):** PRD NFR-P1 sets 250KB; architecture §6.5.4 sets 180KB gz for `/public-statement`. Unreconciled.

**Decision:** **180 KB gz** is the MVP contract for the `/public-statement` route. PRD is amended to match. Other routes inherit a 250 KB gz target.

**Rationale:** LCP <2.5s on Slow 4G at cold-start demands the tighter number on the critical public route. Authenticated routes have warmer caches and can spend more.

**Edits required:**
- `prd.md NFR-P1` — split into two bullets: "Public statement route ≤180 KB gz; authenticated app shell ≤250 KB gz."
- `architecture.md §6.5.4` — explicit reference to the PRD amendment.
- Bundle-size fitness test (existing) verifies both.

---

## R-07 · Throughput Target Reconciliation — 83 rps Sustained Only

**Finding (AR-02):** PRD's 5,000 RPM (≈83 rps) conflicts with architecture's invented "500 rps burst, safety_factor=2.0."

**Decision:** **83 rps sustained, p99 ≤ 1.5s** is the only throughput contract. Fitness test loads at **166 rps for 10 minutes** (2× headroom). The 500 rps burst claim is withdrawn.

**Rationale:** The 500 rps number has no empirical basis, isn't in the PRD, and would exceed free-tier ACA limits under ACA's per-container concurrency defaults. Real bursts at MVP scale are small; 2× headroom is the honest test.

**Edits required:**
- `architecture.md §4.17` — rewrite throughput decision to match PRD NFR-SC4 with the explicit 2× headroom fitness test.
- `architecture.md §7` — adjust fitness test harness (tool, duration, dataset) with pass/fail numbers.

---

## R-08 · State Management Reconciliation — Signals Only, With Escape Hatch

**Finding (AR-03):** Architecture D4.13 mandates signals only; PRD allows "NgRx or Signals." Architecture narrowed without a supersession note.

**Decision:** **Signals only for MVP.** An escape hatch is added: "If a feature's state complexity demonstrably exceeds signals (documented failure mode), NgRx Signal Store is the fallback — no Redux-style NgRx." PRD is updated to match.

**Rationale:** Signals are Angular-native, zoneless-friendly, and avoid the NgRx boilerplate tax. The escape hatch prevents a future forced-rewrite if one feature genuinely needs orchestration.

**Edits required:**
- `architecture.md D4.13` — add the escape-hatch paragraph; reference Signal Store specifically (not classic NgRx).
- `prd.md` — amend "NgRx or Angular Signals" to "Angular Signals (Signal Store permitted as documented escape hatch)."

---

## R-09 · Locale Set Reconciliation — AR + EN Only for MVP

**Finding (AR-04):** Architecture scaffolds `fr.json`; PRD scopes MVP to AR + EN.

**Decision:** MVP ships **AR + EN only**. French scaffolding is removed. A revisit trigger is added for French upon EU expansion beyond Ireland/Netherlands.

**Rationale:** Keeps translation workload honest. French brings CSA-specific compliance language and native-speaker review that we don't have budget for.

**Edits required:**
- `architecture.md §6.5.2` — remove `fr.json` from the translation file list.
- `architecture.md` RT table — add **RT-new-fr-locale**: "First paid signup from France, Belgium, or Luxembourg."

---

## R-10 · Money Scale Reconciliation — Dual Precision

**Finding (AR-13):** `numeric(19,4)` loses precision on gold-base FX conversions requiring ≥6 decimal places.

**Decision:** **Dual precision**:
- User-facing balances: `numeric(19,4)` — standard fiat precision.
- Internal FX rates, gold-base pivots, intermediate conversion products: `numeric(28,8)`.

Rounding to user-facing precision happens **only** at the edge (API response serialization), never in storage or computation.

**Rationale:** Prevents silent precision loss on metal conversions. The split also gives a clean rule for developers: "if it's shown to a user, it's `(19,4)`; if the system computes with it, it's `(28,8)`."

**Edits required:**
- `architecture.md §5` — add dual-precision rule to Money conventions; document rounding-at-edge policy.
- `architecture.md §5` — add JSON shape note: `amount` is always the user-facing `(19,4)` string; internal `(28,8)` values never leave the server.
- Add a schema-convention fitness test: no column named `*amount*`, `*rate*`, or `*balance*` may be `numeric(19,4)` if it participates in FX computation.

---

## Summary Matrix

| # | Title | New ADR | Files Touched | Rough Edit Effort |
|---|---|---|---|---|
| R-01 | Auth0 replaces Keycloak | ADR-021 | architecture §4, §6.5, §7.4.1 | 30 min |
| R-02 | App-level bitemporal | ADR-022 | architecture §4, §5, §7.4 | 30 min |
| R-03 | Resend for transactional email | ADR-023 | architecture §4, §6.5.10; prd FR3, FR42 | 20 min |
| R-04 | Minimal APIs replaces FastEndpoints | ADR-024 (supersedes ADR-003) | architecture §3, §5.3.1, §6.1, §7 | 45 min |
| R-05 | Key Vault Standard, not Premium | ADR-025 | architecture §4, §7.4.1 | 15 min |
| R-06 | 180 KB gz wins | — | architecture §6.5.4; prd NFR-P1 | 10 min |
| R-07 | 83 rps sustained only | — | architecture §4.17, §7 | 10 min |
| R-08 | Signals only + escape hatch | — | architecture D4.13; prd state-mgmt line | 10 min |
| R-09 | AR + EN only | — | architecture §6.5.2, RT table | 5 min |
| R-10 | Dual Money precision | — | architecture §5 | 15 min |

**Estimated total merge effort:** ~3.5 hours of focused doc work once this draft is approved.

---

## Open Questions for Wahid

**Answered 2026-04-21:**

1. ✅ **R-01 (Auth0 tenant)** — Custom domain on **`faktuboh.com`**. Hosted login at `auth.faktuboh.com`. CNAME + TXT DNS records and certificate provisioning handled by Auth0 at tenant setup.
2. ✅ **R-03 (Resend region)** — **EU-region sending enabled** (`region: eu-west-1`) for GDPR data residency.
3. ✅ **R-04 (Problem-details library)** — **DIY on .NET 10 built-ins** (`AddProblemDetails()` + `IExceptionHandler`). No Hellang dependency.
4. ✅ **R-02 (Bitemporal opt-in)** — **Marker interface `IBitemporal`**. Interceptor uses `entry.Entity is IBitemporal` — no reflection on the hot path.

**Still open:**

5. ⏳ **Revisit-trigger discipline** — five new RTs land from R-01..R-05. Want me to also propose a cull of the existing 40 RTs (reduce to ≤10 must-revisit) as a separate task, or leave the existing RT table untouched?

**New clarifier (raised during resolution):**

6. ⏳ **Faktuboh vs Faktuboh branding** — `faktuboh.com` is a new term in the doc set. Is **Faktuboh** (a) the MVP's actual brand name superseding "Faktuboh", (b) a parent/umbrella brand with Faktuboh as a product under it, or (c) just a domain you already own that we're pointing Auth0 at for convenience? The answer affects whether I also update `_bmad/bmm/config.yaml` (`project_name: Faktuboh`) and the title blocks of `architecture.md` / `prd.md`, or leave those as "Faktuboh" with Auth0 sitting on a separate brand domain.

---

## Next Step

On your approval, I merge R-01…R-10 into `architecture.md` and `prd.md` in a single pass, add the five new ADRs in-place (or in a separate `adrs/` folder if you'd prefer — say the word), and write a concise change summary at the top of `architecture.md` noting the resolution pass and its date.
