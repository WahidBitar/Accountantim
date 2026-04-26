# Story 4.8: Signup CTA designed as primary UX with funnel instrumentation

Status: ready-for-dev

## Story

As **Wahid**,
I want **the statement page's signup CTA designed as the primary conversion moment — above-the-fold after the debt summary — with the full viral funnel instrumented**,
so that **Phase-A viral coefficient is measurable from the first day of dogfood** (FR38, Victor's Epic 4 reframe).

**Covers requirements:** Epic 4 — FR38, Critical Behavioral Flow #2 (Statement Link-to-Signup Conversion), AR-047 telemetry primitive, delegated decision "primary UX focus, not a bullet" [Source: epics.md Epic 4 Story 4.8; ux-design-specification.md Flow 3].

**Why this story now:** This is THE conversion moment for Phase A viral coefficient. Stories 4.5 + 4.6 render the statement and record the confirmation; 4.8 turns the viewing recipient into a signed-up user. Per party-mode Victor reframe: this is primary UX, not a footer bullet. Full funnel instrumentation so viral-coefficient hypothesis (≥0.3) is measurable.

## Acceptance Criteria

### AC1 — Signup CTA renders prominently post-confirmation

**Given** a recipient has confirmed the statement (Story 4.6)
**When** `StatementViewComponent` state transitions to `confirmed`
**Then** the signup CTA renders **directly below** `ReconciliationMeta`, NOT in the footer:
- Headline: "Track your own debts — sign up free" (EN) / "تتبع ديونك — سجل مجانًا" (AR)
- Sub-copy: 1 line explaining value prop
- Primary button: "Sign up" (EN) / "سجل الآن" (AR)
- Minimum 44×44pt tap target per mobile accessibility
**And** the CTA is designed as a visually distinct card (card shadow, brand color accent) — NOT as a plain footer link
**And** an `@defer` reveals the CTA with a subtle 300ms fade (not bounce — dignified per UX-DR31)
**And** the `<aside role="complementary" aria-label="Signup invitation">` landmark wraps the CTA for SR navigation [Source: epics.md Story 4.8 AC; ux-design-specification.md Flow 3].

### AC2 — One-click flow pre-fills recipient name from statement

**Given** the statement payload includes the recipient's name
**When** the user taps the CTA
**Then** the flow navigates to `https://app.faktuboh.com/signup?referred_by_statement_token=<token>&prefilled_name=<recipientName>&locale=<ar|en>`
**And** the `referred_by_statement_token` query param is the originating capability token (tracked for attribution; Story 4.8 is the only place this value leaves the public app)
**And** the Auth0 hosted signup form pre-fills the "name" field with `prefilled_name`
**And** after signup, a backend trigger writes `users.referred_by_statement_token = <token>` — attribution sealed [Source: epics.md Story 4.8; delegated decisions — pre-fill name + attribution column].

### AC3 — Full funnel telemetry — 5 events in sequence

**Given** AR-047 primitive exists
**When** the recipient journeys through the funnel
**Then** 5 events fire in order:

1. **`statement_viewed`** — fires in Story 4.5 on GET completion
2. **`cta_viewed`** — fires when the CTA enters the viewport (IntersectionObserver, threshold 0.5)
3. **`cta_clicked`** — fires on tap
4. **`viewer_signup_from_share`** — fires post-signup on owner-workspace when `?referred_by_statement_token` is present
5. **`first_debt_recorded`** — fires when a user with `referred_by_statement_token != null` records their first debt

**And** each event includes `token_hash_prefix8` to correlate across the funnel (not the raw token — PII-safe)
**And** `PiiGuard.AssertNoPii` validates all payloads
**And** integration test `AssertEventEmittedInOrder([...])` verifies the full sequence [Source: epics.md Story 4.8 AC; Story 0.14 AR-047].

### AC4 — A/B harness rotates 3-5 CTA copy variants

**Given** Victor's reframe requires A/B testing from day one
**When** the CTA renders
**Then** the variant is selected deterministically from the token hash (`variant = token_hash_first_byte % variant_count`)
**And** variant copy configured via env var `FAKTUBOH_CTA_VARIANTS_JSON`:

```json
[
  { "id": "v1-direct", "headline_en": "Track your own debts — sign up free", "headline_ar": "..." },
  { "id": "v2-reciprocity", "headline_en": "Ahmed is tracking this — track your side free", "headline_ar": "..." },
  { "id": "v3-scarcity", "headline_en": "Like this? Track your debts free forever", "headline_ar": "..." },
  { "id": "v4-benefit", "headline_en": "Never lose track of debts — free", "headline_ar": "..." },
  { "id": "v5-simple", "headline_en": "Sign up — track your debts", "headline_ar": "..." }
]
```

**And** the `cta_viewed` telemetry includes `variant_id` property
**And** the App Insights dashboard shows conversion rate per variant (`cta_clicked / cta_viewed`) [Source: epics.md Story 4.8 AC; delegated decisions — A/B harness].

### AC5 — Viral-coefficient dashboard in App Insights

**Given** viral coefficient is the Phase-A success metric
**When** the dashboard is built
**Then** a KQL-backed panel computes `viewer_signup_from_share / statement_viewed` per cohort (cohort = day of first statement view)
**And** a second panel computes the 7-day funnel: `statement_viewed → cta_viewed → cta_clicked → viewer_signup_from_share → first_debt_recorded` with drop-off percentages at each step
**And** a third panel shows variant performance: conversion rate per variant
**And** the dashboard is bookmarked in App Insights + linked from `docs/ops/viral-coefficient-dashboard.md` [Source: epics.md Story 4.8 AC; architecture.md §4.6 D4.20].

### AC6 — Attribution column on `users` table

**Given** signup attribution must persist across sessions
**When** migration `YYYYMMDDHHMMSS_AddReferredByStatementTokenToUsers.cs` lands
**Then** `users` table gains column `referred_by_statement_token bytea NULL` (hash of the originating token)
**And** a user's first signup record captures this via an Auth0 Action + backend endpoint `POST /v1/users/attribution` that the signup flow calls
**And** once set, the column is immutable (trigger blocks UPDATE if non-null) — attribution cannot be manipulated post-hoc
**And** subsequent `first_debt_recorded` event queries the column to emit [Source: epics.md Story 4.8 AC; architecture.md §4.6 D4.20 audit-log pattern].

### AC7 — Post-signup nudge shows "You were viewing [lender's name]'s statement"

**Given** the referred user lands on their empty Glance after signup
**When** `referred_by_statement_token` is present
**Then** a `ContextualNudge` (UX-DR11) renders with kind `pending-confirmation` and custom copy:
- EN: "You were viewing [lender's name]'s statement — record your side of this debt?"
- AR: "كنت تشاهد كشف حساب [name] — سجل دينك معه؟"
**And** tapping the nudge opens the "Add debt" flow with the lender's name pre-filled
**And** the nudge appears exactly once per user (dismissed flag in local state)
**And** telemetry `nudge_viewer_side_of_debt_shown` + `nudge_viewer_side_of_debt_acted` fire [Source: ux-design-specification.md Flow 3 Critical Behavioral #2; delegated decisions — UX-DR11 pending-confirmation kind].

### AC8 — `ContextualNudge` `pending-confirmation` kind lands in libs/domain-ui

**Given** UX-DR11 originally declared 4 kinds; Epic 4 adds `pending-confirmation`
**When** `libs/domain-ui/contextual-nudge/contextual-nudge.component.ts` is extended
**Then** `NudgeState.kind` union includes `'pending-confirmation'`
**And** the component accepts an optional `customCopy: { headline: string; actionLabel?: string }` input for this kind only
**And** Story 1.9 Phase 1 component API contract is preserved (single-owner rule) — kind additions are compatible [Source: ux-design-specification.md Component 7 ContextualNudge; delegated decisions].

### AC9 — RFC 9457 attribution endpoint errors

**Given** `POST /v1/users/attribution` is the attribution write path
**When** errors arise
**Then** codes:
- `faktuboh.users.attribution_already_set` (409) — second attempt on same user
- `faktuboh.users.invalid_referral_token` (400) — malformed or non-existent token
**And** the endpoint runs post-Auth0-signup via webhook or frontend-initiated call
**And** is idempotent via natural key `user_id` [Source: architecture.md §4.4 D4.9].

### AC10 — Conversion funnel QA synthetic nightly

**Given** silent-failure of the funnel = silent Phase-A failure
**When** a nightly synthetic runs (GitHub Actions workflow)
**Then** the synthetic:
1. Creates a test owner + contact + debt + issues a token
2. Visits the statement URL (Playwright headful context)
3. Taps the interstitial
4. Taps Confirm
5. Taps the signup CTA → Auth0 signup flow (test tenant)
6. Records a first debt
7. Polls App Insights for all 5 funnel events within 60s
**And** any missing event fails the workflow + emails `wahid.wb@gmail.com` [Source: architecture.md §4.9 silent-decay SLOs].

## Tasks / Subtasks

### Frontend — CTA component

- [ ] **T1 (AC: 1)** — Create `apps/public-statement/src/app/features/statement-view/signup-cta.component.ts`
  - [ ] Standalone, OnPush, zoneless
  - [ ] Inputs: `variant: CtaVariant`, `recipientName: string`, `token: string`, `locale: 'ar'|'en'`
  - [ ] Output: `ctaClicked`
  - [ ] `<aside role="complementary">` landmark
  - [ ] Design tokens from `libs/design-system`
- [ ] **T2 (AC: 1)** — Hook CTA into `statement-view.component.ts` post-confirm state
  - [ ] `@defer (on idle; prefetch on viewport)` block renders `<ps-signup-cta>`
- [ ] **T3 (AC: 3)** — Wire IntersectionObserver for `cta_viewed` telemetry
  - [ ] Fire once per session
  - [ ] Threshold 0.5

### Frontend — Pre-fill flow

- [ ] **T4 (AC: 2)** — On CTA click, build URL and navigate:
  - [ ] `window.location.href = "https://app.faktuboh.com/signup?referred_by_statement_token=${token}&prefilled_name=${encodeURIComponent(recipientName)}&locale=${locale}"`

### Frontend — A/B harness

- [ ] **T5 (AC: 4)** — Create `CtaVariantService` in `apps/public-statement/src/app/features/statement-view/`
  - [ ] Reads variants from environment config (injected at build time from `FAKTUBOH_CTA_VARIANTS_JSON` env var)
  - [ ] `selectVariant(tokenHash): CtaVariant` using first-byte modulo variant count
  - [ ] Returns full variant object for rendering

### Owner-workspace — Post-signup handling

- [ ] **T6 (AC: 2, 6)** — Create Auth0 Action or backend endpoint `POST /v1/users/attribution`
  - [ ] Accepts `{ referredByStatementToken: string }`
  - [ ] Writes `users.referred_by_statement_token = SHA256(raw_token)`
  - [ ] Idempotent — second call returns 409
- [ ] **T7 (AC: 7)** — Extend owner-workspace's Glance to read `referred_by_statement_token` on user record
  - [ ] If non-null, render `ContextualNudge` with kind `pending-confirmation` + lender name (resolved via token lookup)
  - [ ] Nudge persists until dismissed or first debt recorded
- [ ] **T8 (AC: 7)** — Lender name resolution
  - [ ] New endpoint `GET /v1/users/attribution/lender-name` — returns the lender's name from the originating statement
  - [ ] Rate-limited (5 per user per hour); called once on Glance load

### Domain-ui — ContextualNudge extension

- [ ] **T9 (AC: 8)** — Extend `libs/domain-ui/contextual-nudge/contextual-nudge.component.ts`
  - [ ] Add `'pending-confirmation'` to kind union
  - [ ] Accept optional `customCopy` input
  - [ ] Update component spec

### Backend — Attribution

- [ ] **T10 (AC: 6)** — Migration `YYYYMMDDHHMMSS_AddReferredByStatementTokenToUsers.cs`
  - [ ] Column `referred_by_statement_token bytea NULL`
  - [ ] Trigger blocking UPDATE once non-null
- [ ] **T11 (AC: 6, 9)** — Create endpoint `POST /v1/users/attribution` handler
  - [ ] Validates `referredByStatementToken`
  - [ ] Writes the hashed token
- [ ] **T12 (AC: 9)** — Register RFC 9457 codes:
  - [ ] `faktuboh.users.attribution_already_set` (409)
  - [ ] `faktuboh.users.invalid_referral_token` (400)

### Telemetry

- [ ] **T13 (AC: 3)** — Define all 5 event record types in `Faktuboh.Application/Telemetry/Events/`
  - [ ] `StatementViewedEvent` (already in 4.5)
  - [ ] `CtaViewedEvent { TokenHashPrefix8, VariantId, Locale }`
  - [ ] `CtaClickedEvent { TokenHashPrefix8, VariantId }`
  - [ ] `ViewerSignupFromShareEvent { OpaqueUserId, TokenHashPrefix8, VariantId }`
  - [ ] `FirstDebtRecordedEvent { OpaqueUserId, TokenHashPrefix8, Attributed: bool }`
- [ ] **T14 (AC: 3)** — Wire frontend emission
  - [ ] `cta_viewed` + `cta_clicked` fire from public-statement SPA via App Insights JS SDK
  - [ ] `viewer_signup_from_share` fires from owner-workspace post-signup
  - [ ] `first_debt_recorded` fires from backend `CreateDebtHandler` when attribution present

### Dashboards

- [ ] **T15 (AC: 5)** — Viral-coefficient App Insights workbook
  - [ ] KQL queries for cohort viral coefficient
  - [ ] 7-day funnel with drop-off
  - [ ] Per-variant conversion
  - [ ] Document in `docs/ops/viral-coefficient-dashboard.md`

### Nightly synthetic

- [ ] **T16 (AC: 10)** — Create `.github/workflows/viral-funnel-synthetic.yml`
  - [ ] Nightly cron
  - [ ] Playwright flow + App Insights event polling
  - [ ] Email on failure

### Tests

- [ ] **T17 (AC: 3)** — `AssertEventEmittedInOrder(["statement_viewed", "cta_viewed", "cta_clicked"])` integration test
- [ ] **T18 (AC: 4)** — Variant selection unit test — deterministic from token hash; equal distribution over 1000 tokens
- [ ] **T19 (AC: 6)** — Attribution column immutability test
- [ ] **T20 (AC: 7)** — Vitest component test for the post-signup nudge
- [ ] **T21 (AC: 1, 2)** — Playwright E2E: full flow from statement view → CTA → pre-filled signup form

## Dev Notes

### Critical guardrails

1. **This is the primary UX moment, not a footer.** The design must be visually distinct — card shadow, brand color. If it looks like an afterthought, conversion drops 40%+.
2. **Pre-fill recipient name is load-bearing.** Removing a single onboarding field lifts signup completion significantly. Not optional.
3. **Attribution is immutable once set.** Trigger at DB level. Prevents manipulation or "I meant to attribute to a different campaign" drift.
4. **Funnel sequence is ordered.** Test asserts ordering (not just existence). Out-of-order events = broken funnel = broken metric.
5. **A/B variants deterministic from token.** Same token = same variant, even across page refreshes. Random-per-render = unstable attribution.
6. **No PII in telemetry.** Recipient name is in the URL (needed for prefill) but NOT in telemetry payloads. `PiiGuard` enforces.
7. **Nightly synthetic is silent-failure insurance.** The funnel is the Phase-A success metric — a silent break = no signal = no product direction.

### Source tree

```
frontend/apps/public-statement/src/app/features/statement-view/
├── signup-cta.component.ts                                   # NEW
├── signup-cta.component.html                                 # NEW
├── signup-cta.component.scss                                 # NEW
├── cta-variant.service.ts                                    # NEW
└── statement-view.component.ts                               # MODIFY (wire CTA + IO)

frontend/apps/owner-workspace/src/app/features/
├── dashboard/glance/glance.component.ts                      # MODIFY (render referred nudge)
└── signup/signup-attribution.service.ts                      # NEW (calls /v1/users/attribution)

libs/domain-ui/contextual-nudge/
├── contextual-nudge.component.ts                             # MODIFY (+ pending-confirmation kind)
└── contextual-nudge.component.spec.ts                        # MODIFY

backend/
├── src/Faktuboh.Api/Slices/Users/Attribution/
│   ├── AttributionEndpoint.cs                                # NEW
│   ├── SetAttributionHandler.cs                              # NEW
│   ├── GetLenderNameForReferredUserHandler.cs                # NEW
│   └── SetAttributionRequest.cs                              # NEW
├── src/Faktuboh.Application/
│   ├── Errors/ErrorCatalog.cs                                # MODIFY (+2 codes)
│   └── Telemetry/Events/
│       ├── CtaViewedEvent.cs                                 # NEW
│       ├── CtaClickedEvent.cs                                # NEW
│       ├── ViewerSignupFromShareEvent.cs                     # NEW
│       └── FirstDebtRecordedEvent.cs                         # NEW
└── src/Faktuboh.Infrastructure/Persistence/Migrations/
    └── YYYYMMDDHHMMSS_AddReferredByStatementTokenToUsers.cs   # NEW

.github/workflows/viral-funnel-synthetic.yml                   # NEW

docs/ops/viral-coefficient-dashboard.md                        # NEW
```

### Architecture compliance

- **FR38** — signup CTA on statement page.
- **Critical Behavioral Flow #2** (Statement Link-to-Signup Conversion) — full flow implemented.
- **AR-047 telemetry primitive** — 5 events in the funnel.
- **ADR-021 Auth0** — signup flow goes through Auth0 hosted page with query-string pre-fill.
- **UX-DR11 ContextualNudge** — `pending-confirmation` kind added (single-owner preserved).

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Angular | 21 | IntersectionObserver native API |
| App Insights JS SDK | latest | Frontend event emission |
| Auth0 | — | Hosted page query-string pre-fill |

### Testing requirements

- **Unit:** Variant selection determinism + distribution.
- **Integration:** Funnel event ordering; attribution immutability.
- **E2E:** Full recipient-to-first-debt journey (Playwright).
- **Synthetic:** Nightly real-environment check.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.3 — Auth0 tenant + Actions (for attribution-on-signup trigger)
- Story 0.14 — telemetry primitive
- Story 1.9 — Phase 1 `ContextualNudge` component
- Story 4.5 — StatementView renders
- Story 4.6 — Confirm button + state transition
- Story 4.7 — (no direct dependency, parallel)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.8
- Critical Behavioral Flow #2 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Flow 3 Viral loop — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Flow 3
- UX-DR11 ContextualNudge — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component 7
- AR-047 telemetry primitive — [_bmad-output/implementation-artifacts/0-14-phase-a-telemetry-primitive.md](./0-14-phase-a-telemetry-primitive.md)
- FR38 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- This story is Phase A's primary success-metric enabler. Without it, viral-coefficient is unmeasurable.
- `cta_viewed` uses IntersectionObserver — zoneless-compatible.

### File List

_Populated during implementation._
