# Story 7.6: Share nudge — material-threshold balance crossing

Status: ready-for-dev

## Story

As a **user whose balance with a contact crosses a configurable material threshold**,
I want **a reminder to share the statement when a denomination crosses >€50-equivalent**,
so that **high-value balances are reconciled proactively** (FR52c) [Source: epics.md Epic 7 Story 7.6; prd.md FR52c].

**Covers requirements:** Epic 7 — FR52c (material-threshold nudge), UX-DR11 ContextualNudge `threshold-crossing` kind, threshold configurability seam (Phase-2 UI; MVP fixed default per delegated decision) [Source: epics.md Story 7.6].

**Why this story now:** Third and final nudge variant. The trigger is balance-magnitude not time/count, so the eligibility check is the most computationally-involved of the three. Threshold-crossing is the most "fintech-grade" nudge — for a money-tracking product, it's the one that signals "this matters enough to reconcile."

## Acceptance Criteria

### AC1 — Nudge eligibility — `GET /v1/nudges/threshold-crossing?contactId={id}`

**Given** the user lands on the contact detail page or dashboard
**When** the page calls the eligibility endpoint
**Then** the handler resolves eligibility:
1. For each denomination held with the contact, compute current absolute balance (sum of receivables minus payables, absolute value)
2. Convert to EUR-equivalent using a static conversion table for MVP (no live FX): `EUR=1, USD=0.92, GBP=1.17, AED=0.25, SAR=0.245, EGP=0.019, JOD=1.30, KWD=3.0, BHD=2.45, TND=0.30, gold-gram=60, silver-gram=0.7` (values approximate; exact values pinned in `Faktuboh.Application/Currency/EurEquivalentTable.cs` with last-updated comment)
3. Find the highest-EUR-equivalent denomination
4. If it crosses the user's configured threshold (default €50 per delegated decision; MVP uses fixed default), the nudge is eligible
5. "Crosses" means: any time the balance has just risen above threshold for the first time since the last share OR last dismiss — implemented via a watermark in `nudge_threshold_state` table tracked per `(user_id, contact_id, denomination)`
6. Check `nudge_dismissals` for `(user_id, contact_id, 'threshold-crossing')` row with `suppress_until > now()` — if so, return `{ eligible: false }`
7. Otherwise, return `{ eligible: true, kind: 'threshold-crossing', contactId, denomination, balance, eurEquivalent, threshold }`
**And** the endpoint p95 < 150ms (slightly higher than other nudges due to balance computation; uses cached per-contact balance from Story 2.4) [Source: epics.md Story 7.6 AC; FR52c].

### AC2 — `nudge_threshold_state` table

**Given** the threshold-crossing detection requires per-(user, contact, denomination) state
**When** migration `YYYYMMDDHHMMSS_AddNudgeThresholdStateTable.cs` lands
**Then** the table:
```sql
CREATE TABLE nudge_threshold_state (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    contact_id uuid NOT NULL,
    denomination text NOT NULL,
    last_above_threshold_at timestamptz NULL,
    last_below_threshold_at timestamptz NULL,
    last_evaluated_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_nudge_threshold_state_user_contact_denom
    ON nudge_threshold_state(user_id, contact_id, denomination);
```
**And** the eligibility handler updates this row on each evaluation:
- If currentEur > threshold AND `last_above_threshold_at` is null OR `last_below_threshold_at > last_above_threshold_at` → just-crossed; update `last_above_threshold_at = now()`, mark eligible
- If currentEur > threshold AND already-above (`last_above_threshold_at` set) → not-just-crossed, not eligible (already nudged before — wait for crossing-again)
- If currentEur ≤ threshold → update `last_below_threshold_at = now()`, not eligible
**And** on share or dismissal, update `last_above_threshold_at = NULL` (effectively reset; the next crossing will re-trigger) [Source: epics.md Story 7.6 implicit threshold-state semantics].

### AC3 — `ContextualNudge kind="threshold-crossing"` renders per UX-DR11

**Given** eligibility returns `{ eligible: true }`
**When** `<ow-contextual-nudge kind="threshold-crossing">` renders
**Then** the component shows:
- Text: "[Contact name]'s [denomination] balance crossed [threshold-equivalent]. Share to reconcile?" (Transloco key `nudges.threshold_crossing.body` with interpolation)
- Primary action: "Share now"
- Secondary action: "Dismiss"
- Same inline-card layout per UX-DR11
**And** the displayed value uses the user's locale formatting (Arabic numerals or Western per Transloco) and proper denomination glyph from `libs/domain-ui/denomination-icon` (Story 1.9 component) [Source: ux-design-specification.md UX-DR11].

### AC4 — Dismiss action — same cooldown rule

**Given** the user taps "Dismiss"
**When** `POST /v1/nudges/threshold-crossing/dismiss` is called
**Then** the handler:
1. Calls shared `NudgeDismissalService.RegisterDismissalAsync(userId, contactId, "threshold-crossing")` (from Story 7.5 extraction)
2. Resets `nudge_threshold_state.last_above_threshold_at = NULL` for this `(user, contact, denomination)` so a future crossing re-triggers
3. Returns `200 OK` with `{ suppressedUntil }`
**And** dismissal of `threshold-crossing` does NOT affect the other kinds [Source: Story 7.4 AC4; delegated decisions].

### AC5 — Threshold configurability seam (Phase-2 UI; MVP fixed default)

**Given** the threshold is configurable per user (FR52c) but UI is Phase-2 per delegated decision
**When** the eligibility handler reads the threshold
**Then** it reads from `users.share_nudge_threshold_eur` column (default 50)
**And** the column exists from migration `YYYYMMDDHHMMSS_AddUserShareNudgeThresholdColumn.cs` with `numeric(10, 2) NOT NULL DEFAULT 50`
**And** at MVP, NO UI surfaces this — value is fixed at default for all users
**And** the column is read on every eligibility check (no caching at MVP)
**And** Phase-2 UX adds a profile setting (deferred — out of MVP scope) [Source: delegated decisions; epics.md Story 7.6 AC threshold configurable].

### AC6 — Telemetry — same events with `kind="threshold-crossing"`

**Given** AR-047 telemetry primitive
**When** events fire
**Then** `nudge_shown` / `nudge_clicked` / `nudge_dismissed` emit with `nudge_kind: "threshold-crossing"` and additional payload field `eur_equivalent_bucket: "<100" | "<500" | "<5000" | ">5000"` to track the magnitude distribution
**And** PiiGuard validates [Source: Story 0.14].

### AC7 — Suppression hierarchy when multiple kinds eligible

**Given** the user might trigger multiple kinds simultaneously
**When** all three are checked
**Then** the priority is: `share-first-statement` (highest) → `threshold-crossing` → `staleness-reminder` (lowest)
**And** if any higher-priority nudge is eligible, lower-priority nudges are NOT shown
**And** the priority is enforced in the FE composition layer (the BE eligibility endpoints are independent + dumb)
**And** an integration test asserts: contact with 2 debts (`share-first-statement` eligible) + balance > €50 (`threshold-crossing` would be eligible) → only `share-first-statement` renders [Source: Story 7.5 AC priority; design decision for this story].

### AC8 — EUR equivalent table is pinned + dated

**Given** FX rates drift over time
**When** the EUR equivalent table in `Faktuboh.Application/Currency/EurEquivalentTable.cs` is defined
**Then** the file includes a `LAST_UPDATED` constant + a comment explaining the table is for nudge-trigger purposes only (not authoritative for any user-facing amount)
**And** a yearly review reminder is added to `docs/runbooks/eur-equivalent-table-review.md` (rotation owner: Wahid)
**And** unit test asserts every supported denomination from `CurrencyRegistry` has an entry in `EurEquivalentTable` (regression guard) [Source: design decision for this story].

## Tasks / Subtasks

### Backend — Eligibility endpoint

- [ ] **T1 (AC: 1, 2, 5)** — Create slice action `Slices/Nudges/ThresholdCrossing/Eligibility/`
  - [ ] `EligibilityRequest.cs`, `EligibilityResponse.cs`, `EligibilityHandler.cs`
  - [ ] Handler reads per-contact balance from existing read model (Story 2.4 per-denomination summary)
  - [ ] Reads `users.share_nudge_threshold_eur`
  - [ ] Updates `nudge_threshold_state` per AC2 semantics
  - [ ] Endpoint `MapGet("/v1/nudges/threshold-crossing", ...)` with shared rate-limit policy

### Backend — Dismiss endpoint

- [ ] **T2 (AC: 4)** — Create slice action `Slices/Nudges/ThresholdCrossing/Dismiss/`
  - [ ] Calls `NudgeDismissalService.RegisterDismissalAsync(userId, contactId, "threshold-crossing")` (from Story 7.5)
  - [ ] Also resets `nudge_threshold_state.last_above_threshold_at = NULL` for the contact's denominations
  - [ ] Endpoint `MapPost("/v1/nudges/threshold-crossing/dismiss", ...)`

### Backend — Migrations

- [ ] **T3 (AC: 2)** — Migration `YYYYMMDDHHMMSS_AddNudgeThresholdStateTable.cs` per AC2
- [ ] **T4 (AC: 5)** — Migration `YYYYMMDDHHMMSS_AddUserShareNudgeThresholdColumn.cs`
  - [ ] `ALTER TABLE users ADD COLUMN share_nudge_threshold_eur numeric(10, 2) NOT NULL DEFAULT 50;`
- [ ] **T5 (AC: 1)** — Update `nudge_dismissals.nudge_kind` CHECK constraint to include `'threshold-crossing'` (verify Story 7.4 already includes it — it does per Story 7.4 AC1)

### Backend — EUR equivalent table

- [ ] **T6 (AC: 8)** — Create `backend/src/Faktuboh.Application/Currency/EurEquivalentTable.cs`
  - [ ] Static dictionary mapping `Currency` → EUR rate
  - [ ] `LAST_UPDATED` constant (date string)
  - [ ] Comment block explaining the table is for nudge-trigger purposes only — not authoritative for user-facing amounts
  - [ ] Method `decimal ConvertToEur(Money money)` with explicit `MidpointRounding.ToEven` rounding
- [ ] **T7 (AC: 8)** — Create `docs/runbooks/eur-equivalent-table-review.md`
  - [ ] Annual review checklist (1 Jan)
  - [ ] Update procedure
  - [ ] Owner: Wahid

### Backend — Telemetry

- [ ] **T8 (AC: 6)** — Extend `NudgeShownTelemetry` to include optional `EurEquivalentBucket` field
  - [ ] Backward-compatible (nullable for share-first + staleness)
  - [ ] threshold-crossing emit always populates it

### Backend — Threshold-state semantics

- [ ] **T9 (AC: 2)** — Implement the watermark logic in `EligibilityHandler`:
  - [ ] Read or insert `nudge_threshold_state` row
  - [ ] Compute `currentEur`
  - [ ] Branches per AC2 algorithm
  - [ ] Atomic update via `INSERT ... ON CONFLICT (user_id, contact_id, denomination) DO UPDATE` (single round-trip)

### Frontend — Component variant

- [ ] **T10 (AC: 3)** — Extend `<ow-contextual-nudge>` to handle `kind="threshold-crossing"` with new copy + denomination icon slot
- [ ] **T11 (AC: 1, 4)** — Extend `nudges.client.ts`:
  - [ ] `getThresholdEligibility(contactId): rxResource<EligibilityResponse>`
  - [ ] `dismissThreshold(contactId): Observable<DismissResponse>`

### Frontend — Suppression-hierarchy composer

- [ ] **T12 (AC: 7)** — Create `apps/owner-workspace/src/app/core/nudges/nudge-priority.service.ts`
  - [ ] Composes the three eligibility resources
  - [ ] Returns the highest-priority eligible nudge or null
  - [ ] Single `<ow-contextual-nudge>` instance bound to this composed signal — guarantees only one nudge ever renders per contact context

### Tests — Backend

- [ ] **T13 (AC: 1, 2)** — Integration tests for eligibility
  - [ ] No balance → not eligible
  - [ ] Balance < threshold → not eligible
  - [ ] Balance just-crossed (first time above) → eligible + state updated
  - [ ] Balance above threshold but already-above (state already set) → not eligible
  - [ ] Crossing-again sequence: above → below → above → eligible on second crossing
  - [ ] Suppressed via dismissal → not eligible even if just-crossed
  - [ ] Multi-denomination: contact has €30 + $80 (≈ €74); $-balance crosses → eligible with denomination=USD
- [ ] **T14 (AC: 4)** — Dismiss test verifies `last_above_threshold_at` reset
- [ ] **T15 (AC: 8)** — Unit test asserts every `CurrencyRegistry` entry has an `EurEquivalentTable` entry
- [ ] **T16 (AC: 6)** — Telemetry assertion `AssertEventEmitted("nudge_shown")` with `eur_equivalent_bucket` populated

### Tests — Frontend

- [ ] **T17 (AC: 3)** — Vitest component test for `<ow-contextual-nudge kind="threshold-crossing">`
- [ ] **T18 (AC: 7)** — Vitest test for `nudge-priority.service.ts`
  - [ ] All three eligible → returns share-first
  - [ ] Only threshold + staleness eligible → returns threshold
  - [ ] Only staleness eligible → returns staleness
  - [ ] None eligible → returns null
- [ ] **T19 (AC: 3)** — Playwright E2E: seed contact with debt > €50; visit detail page → nudge appears; dismiss → nudge disappears; record more debt to push higher; visit again → not eligible (already nudged)
- [ ] **T20 (AC: 3)** — Axe-core: zero violations

## Dev Notes

### Critical guardrails (do not violate)

1. **EUR equivalent table is for nudge triggering only.** Never use it to display amounts to users. Authoritative display amounts use the user's stored denomination + Money VO from Epic 2/3. Drift in the EUR table affects nudge timing only — it does not corrupt the ledger.
2. **Threshold-state semantics — "just crossed", not "currently above".** Without the watermark, the nudge would re-fire on every page load while balance stays above threshold — annoying. The `last_above_threshold_at` + `last_below_threshold_at` pair is the watermark.
3. **Dismissal resets the watermark.** After dismiss, the next crossing fires again (after the suppress-until window). Without this, dismissed-while-above stays dismissed forever — user signal lost.
4. **Annual EUR table review is a runbook commitment.** Without it, the table drifts to inaccuracy over time + nudges fire at wrong magnitudes. Owner: Wahid; cadence: 1 Jan.
5. **Priority composition lives in the FE.** The BE eligibility endpoints are independent + dumb. This avoids cross-slice coupling at the BE and keeps each endpoint testable in isolation.
6. **No live FX at MVP.** Live FX would require a paid API or batch sync — over-engineering for nudge magnitude. Static table is good-enough; runbook handles the drift.
7. **Per-user threshold column ships disabled (no UI).** The column exists + the read happens, but no user can change the value at MVP. Phase-2 adds the profile UI. This is the "interface seam" pattern from AR-049.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/Nudges/ThresholdCrossing/
│   │       ├── Eligibility/
│   │       │   ├── EligibilityRequest.cs                       # NEW
│   │       │   ├── EligibilityResponse.cs                      # NEW
│   │       │   └── EligibilityHandler.cs                       # NEW
│   │       └── Dismiss/
│   │           ├── DismissRequest.cs                           # NEW
│   │           ├── DismissResponse.cs                          # NEW
│   │           └── DismissHandler.cs                           # NEW
│   ├── Faktuboh.Application/
│   │   ├── Currency/
│   │   │   └── EurEquivalentTable.cs                           # NEW
│   │   └── Telemetry/Events/
│   │       └── NudgeShownTelemetry.cs                          # EDIT (+EurEquivalentBucket)
│   └── Faktuboh.Infrastructure/Persistence/Migrations/
│       ├── YYYYMMDDHHMMSS_AddNudgeThresholdStateTable.cs       # NEW
│       └── YYYYMMDDHHMMSS_AddUserShareNudgeThresholdColumn.cs  # NEW
└── tests/Faktuboh.Api.Tests/Slices/Nudges/
    └── ThresholdCrossingTests.cs                               # NEW (Integration)

frontend/
├── apps/owner-workspace/src/app/core/nudges/
│   ├── nudges.client.ts                                        # EDIT (+threshold methods)
│   └── nudge-priority.service.ts                               # NEW
└── libs/domain-ui/contextual-nudge/
    ├── contextual-nudge.component.ts                           # EDIT (+threshold kind)
    └── contextual-nudge.component.spec.ts                      # EDIT

docs/runbooks/
└── eur-equivalent-table-review.md                              # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 (table coverage test) |
| Integration (BE) | Testcontainers Postgres + crossing-sequence test |
| Unit (FE) | Vitest + signal testing |
| E2E | Playwright |

### Project Structure Notes

This story closes the `Nudges/` slice for MVP. After this, three sibling action folders (`ShareFirstStatement/`, `StalenessReminder/`, `ThresholdCrossing/`) coexist under one slice — sharing the dismissal table + service + component. Per slice-isolation discipline, the slice does NOT import `Contact` aggregate types; the `contactId` is treated as a raw `Guid` everywhere.

### Architecture compliance

- [epics.md Epic 7 Story 7.6](../planning-artifacts/epics.md)
- [ux-design-specification.md UX-DR11](../planning-artifacts/ux-design-specification.md)
- [prd.md FR52c, NFR-S5](../planning-artifacts/prd.md)
- [Story 7.4](./7-4-share-nudge-after-first-3-debts-per-contact.md)
- [Story 7.5](./7-5-share-nudge-7-day-unshared-staleness.md)
- [Story 2.4](./2-4-list-contacts-with-per-denomination-balance-summary.md) — per-denomination balance source
- [Story 1.9](./1-9-phase-1-component-api-design-system-library.md) — denomination icon

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | (No new packages) | Pure .NET arithmetic + EF Core |

### Testing requirements

- **Crossing-sequence test (above → below → above)** — non-obvious; without it, the "just-crossed" semantics could fire repeatedly OR never re-fire after a dip-below.
- **Multi-denomination test** — verifies the highest-EUR-equivalent denomination is selected when multiple are above threshold.
- **EUR table coverage test** — regression guard if `CurrencyRegistry` adds a new currency without updating the table.
- **Priority composition test** — without it, multiple nudges could render simultaneously, violating UX-DR11 single-nudge-per-context rule.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 7.4 — `nudge_dismissals` table + component
- Story 7.5 — `NudgeDismissalService` extraction
- Story 2.4 — per-denomination balance summary (read source)
- Story 1.9 — denomination icon component
- Story 0.14 — telemetry primitive

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.6
- [_bmad-output/planning-artifacts/ux-design-specification.md UX-DR11](../planning-artifacts/ux-design-specification.md)
- [_bmad-output/planning-artifacts/prd.md FR52c, NFR-S5](../planning-artifacts/prd.md)
- [Story 7.4](./7-4-share-nudge-after-first-3-debts-per-contact.md)
- [Story 7.5](./7-5-share-nudge-7-day-unshared-staleness.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
