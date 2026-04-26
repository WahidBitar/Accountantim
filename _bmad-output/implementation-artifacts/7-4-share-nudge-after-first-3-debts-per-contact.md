# Story 7.4: Share nudge — after first 3 debts per contact

Status: ready-for-dev

## Story

As a **user recording multiple debts with a new contact**,
I want **to be prompted (non-blocking) to share the statement after each of the first 3 debts recorded per contact**,
so that **the viral loop activates during the highest-engagement moments** (FR52a) [Source: epics.md Epic 7 Story 7.4; prd.md FR52a].

**Covers requirements:** Epic 7 — FR52a (first-3-debts share nudge), UX-DR11 ContextualNudge `share-first-statement` kind, dismiss-cooldown table foundation (consumed also by Stories 7.5, 7.6) [Source: epics.md Story 7.4; ux-design-specification.md UX-DR11].

**Why this story now:** First of three nudge variants. Establishes the `nudge_dismissals` table + dismissal logic shared by Stories 7.5 + 7.6. The `share-first-statement` trigger (debt-count threshold) is the simplest of the three — best place to lay the foundation.

## Acceptance Criteria

### AC1 — `nudge_dismissals` Postgres table

**Given** the dismissal-cooldown rule applies across all three nudge variants per delegated decision
**When** migration `YYYYMMDDHHMMSS_AddNudgeDismissalsTable.cs` lands
**Then** the table has columns:
```sql
CREATE TABLE nudge_dismissals (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    contact_id uuid NOT NULL,
    nudge_kind text NOT NULL CHECK (nudge_kind IN ('share-first-statement', 'staleness-reminder', 'threshold-crossing')),
    dismissed_at timestamptz NOT NULL DEFAULT now(),
    suppress_until timestamptz NULL,
    consecutive_dismiss_count smallint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_nudge_dismissals_user_contact_kind
    ON nudge_dismissals(user_id, contact_id, nudge_kind);
CREATE INDEX ix_nudge_dismissals_suppress_until
    ON nudge_dismissals(suppress_until) WHERE suppress_until IS NOT NULL;
```
**And** the table is NOT bitemporal — dismissal state is operational metadata, not auditable financial data; ADR-022 history-tracking would be over-engineering
**And** an integration test asserts the unique constraint prevents duplicate `(user_id, contact_id, nudge_kind)` rows [Source: delegated decisions; epics.md Story 7.4 dismissal cooldown spans 7.5, 7.6].

### AC2 — Nudge eligibility — `GET /v1/nudges/share-first-statement?contactId={id}`

**Given** the user lands on the dashboard or contact detail page
**When** the page calls the nudge eligibility endpoint
**Then** the handler resolves eligibility:
1. Count debts for `(user_id, contact_id)` from `journal_entries` — if count is 0 OR > 3, return `{ eligible: false }`
2. Check if user has shared at least one statement for this contact (`statements WHERE contact_id = ? AND owner_subject_id = ?`) — if yes, return `{ eligible: false }`
3. Check `nudge_dismissals` for matching `(user_id, contact_id, 'share-first-statement')` row with `suppress_until > now()` — if so, return `{ eligible: false }`
4. Otherwise, return `{ eligible: true, kind: 'share-first-statement', contactId, debtCount: <1|2|3> }`
**And** the endpoint is fast (< 100ms p95) — debt count uses an indexed query on `(contact_id, owner_subject_id)`
**And** an integration test covers all 4 branches [Source: epics.md Story 7.4 AC; FR52a].

### AC3 — `ContextualNudge kind="share-first-statement"` renders per UX-DR11

**Given** the eligibility check returns `{ eligible: true }`
**When** the `<ow-contextual-nudge>` component renders
**Then** the component (consumed from `libs/domain-ui` per UX-DR11) shows:
- Text: "Share the statement with [contact name]?" (Transloco key `nudges.share_first_statement.body`)
- Primary action: "Share" — opens the share flow (Story 4.4) for the relevant contact
- Secondary action: "Dismiss" — calls `POST /v1/nudges/share-first-statement/dismiss`
- Layout: inline card per UX-DR11, NOT a modal
- Non-blocking — does not steal focus or block other UI
**And** the nudge appears at top of contact detail page or as a dashboard tile per UX-DR11 placement rules
**And** Arabic + English Transloco keys cover all copy [Source: ux-design-specification.md UX-DR11].

### AC4 — Dismiss action with cooldown rule

**Given** the user taps "Dismiss"
**When** `POST /v1/nudges/share-first-statement/dismiss` is called with body `{ contactId }`
**Then** the handler:
1. Looks up existing `nudge_dismissals` row for `(user_id, contact_id, 'share-first-statement')`
2. If none, inserts with `consecutive_dismiss_count = 1`, `suppress_until = NULL` (next page load eligibility re-evaluates)
3. If exists with `consecutive_dismiss_count < 3`, updates `consecutive_dismiss_count = consecutive_dismiss_count + 1`, `dismissed_at = now()`
4. If `consecutive_dismiss_count = 3` after the increment, sets `suppress_until = now() + interval '30 days'` per delegated decision
5. Returns `200 OK` with `{ suppressedUntil: ISO8601 | null }`
**And** the nudge disappears from the UI (silent success — UI mutates local state)
**And** an integration test verifies: 3 consecutive dismissals → 4th eligibility check returns `{ eligible: false }` for 30 days [Source: delegated decisions; prd.md FR52].

### AC5 — Auto-clear nudge after 3rd debt OR after sharing

**Given** the conditions per AC2 step 1 + step 2
**When** the user records the 4th debt for the contact OR shares any statement for the contact
**Then** subsequent eligibility checks return `{ eligible: false }`
**And** the nudge disappears from the UI on next page load (no real-time update needed at MVP — passive removal acceptable)
**And** integration tests cover both auto-clear paths [Source: epics.md Story 7.4 AC auto-clear].

### AC6 — Telemetry — `nudge_shown`, `nudge_clicked`, `nudge_dismissed`

**Given** AR-047 telemetry primitive
**When** each event happens
**Then** events emit:
- `nudge_shown` — `{ opaque_user_id, contact_id_hash, nudge_kind: 'share-first-statement', debt_count }`
- `nudge_clicked` — `{ opaque_user_id, contact_id_hash, nudge_kind, action: 'share' }`
- `nudge_dismissed` — `{ opaque_user_id, contact_id_hash, nudge_kind, consecutive_count, suppressed_until_bucket: '30d' | null }`
**And** PiiGuard validates all payloads [Source: Story 0.14].

### AC7 — Eligibility endpoint is rate-limited per user

**Given** the eligibility endpoint may be polled aggressively (page load, route change)
**When** a single user calls more than 60 times per minute
**Then** the endpoint returns 429 RFC 9457 `faktuboh.nudges.rate_limit_exceeded` with `Retry-After: 60`
**And** the rate-limit policy is per-`user_id` (not per-IP) using ASP.NET Core rate-limiting middleware [Source: prd.md NFR-S5].

## Tasks / Subtasks

### Backend — Migration + table

- [ ] **T1 (AC: 1)** — Create migration `YYYYMMDDHHMMSS_AddNudgeDismissalsTable.cs` with the SQL from AC1
- [ ] **T2 (AC: 1)** — Define `NudgeDismissal` entity in `Faktuboh.Domain/Nudges/`
  - [ ] Plain entity (NOT `IBitemporal`)
  - [ ] Repository interface `INudgeDismissalRepository` with `GetAsync(userId, contactId, kind)`, `UpsertAsync(...)`

### Backend — Eligibility endpoint

- [ ] **T3 (AC: 2, 3, 5)** — Create slice `Slices/Nudges/ShareFirstStatement/Eligibility/`
  - [ ] `EligibilityRequest.cs` — `(Guid ContactId)`
  - [ ] `EligibilityResponse.cs` — `(bool Eligible, string? Kind, Guid? ContactId, int? DebtCount)`
  - [ ] `EligibilityHandler.cs` — runs the 4 checks per AC2
  - [ ] Endpoint `MapGet("/v1/nudges/share-first-statement", ...)` with `.RequireAuthorization()` + per-user rate limit policy

### Backend — Dismiss endpoint

- [ ] **T4 (AC: 4)** — Create slice `Slices/Nudges/ShareFirstStatement/Dismiss/`
  - [ ] `DismissRequest.cs` — `(Guid ContactId)`
  - [ ] `DismissResponse.cs` — `(DateTimeOffset? SuppressedUntil)`
  - [ ] `DismissHandler.cs` — implements upsert + 3-strikes-30-days rule
  - [ ] Endpoint `MapPost("/v1/nudges/share-first-statement/dismiss", ...)`
  - [ ] Idempotency-Key middleware required

### Backend — Telemetry

- [ ] **T5 (AC: 6)** — Define telemetry events in `Faktuboh.Application/Telemetry/Events/`
  - [ ] `NudgeShownTelemetry`, `NudgeClickedTelemetry`, `NudgeDismissedTelemetry`
- [ ] **T6 (AC: 6)** — Wire emission: eligibility endpoint emits `nudge_shown` when `eligible: true` returned; dismiss handler emits `nudge_dismissed`; share-flow opening from nudge emits `nudge_clicked`

### Backend — Rate limit

- [ ] **T7 (AC: 7)** — Configure per-user rate-limit policy `NudgesEligibilityPolicy` in `Program.cs`
  - [ ] 60 requests / minute / `user_id`
  - [ ] Returns 429 with RFC 9457 + `Retry-After` header
  - [ ] Register `faktuboh.nudges.rate_limit_exceeded` in `ErrorCatalog`

### Frontend — Component + service

- [ ] **T8 (AC: 3)** — Create or extend `libs/domain-ui/contextual-nudge/contextual-nudge.component.ts` to support `kind="share-first-statement"`
  - [ ] If the component already exists from prior UX work, add the kind case; otherwise scaffold the component with kind-discriminated rendering
  - [ ] Standalone, OnPush, signal inputs `kind: InputSignal<NudgeKind>`, `payload: InputSignal<NudgePayload>`
  - [ ] Bilingual via Transloco
- [ ] **T9 (AC: 2, 5)** — Create `apps/owner-workspace/src/app/core/nudges/nudges.client.ts`
  - [ ] `getShareFirstEligibility(contactId): rxResource<EligibilityResponse>`
  - [ ] `dismissShareFirst(contactId): Observable<DismissResponse>`
- [ ] **T10 (AC: 3)** — Wire nudge into contact detail page + dashboard tile
  - [ ] Resource subscription per contactId
  - [ ] Render `<ow-contextual-nudge>` only when `eligible: true`
  - [ ] On "Share" action, navigate to share flow per Story 4.4 routes
  - [ ] On "Dismiss" action, call `dismissShareFirst` + locally suppress (no re-render of nudge)

### Tests — Backend

- [ ] **T11 (AC: 2)** — Integration tests for eligibility endpoint
  - [ ] 0 debts → not eligible
  - [ ] 1, 2, 3 debts + no share + no dismissal → eligible
  - [ ] 4+ debts → not eligible
  - [ ] Has shared a statement → not eligible
  - [ ] Dismissed with `suppress_until > now()` → not eligible
- [ ] **T12 (AC: 4)** — Integration tests for dismiss endpoint
  - [ ] First dismiss → consecutive_count = 1, suppress_until null
  - [ ] Second dismiss → count = 2
  - [ ] Third dismiss → count = 3, suppress_until = now() + 30d
  - [ ] Fourth eligibility check → not eligible (suppressed)
  - [ ] After 31 days (mock clock) → eligible again, count reset to 1 on next dismiss
- [ ] **T13 (AC: 7)** — Rate-limit test: 61st request in a minute returns 429 + Retry-After
- [ ] **T14 (AC: 6)** — `AssertEventEmitted` for each of the three telemetry events

### Tests — Frontend

- [ ] **T15 (AC: 3)** — Vitest component test for `<ow-contextual-nudge kind="share-first-statement">`
  - [ ] Renders correct copy (AR + EN)
  - [ ] "Share" action calls share-flow service
  - [ ] "Dismiss" action calls nudges client + hides component
- [ ] **T16 (AC: 3, 5)** — Playwright E2E: record 1st debt for new contact → nudge appears; record 4th → nudge disappears; share → nudge disappears
- [ ] **T17 (AC: 3)** — Axe-core: zero WCAG 2.1 AA violations on the nudge

## Dev Notes

### Critical guardrails (do not violate)

1. **`nudge_dismissals` is shared across Stories 7.5 + 7.6.** The `nudge_kind` discriminator + the unique constraint on `(user_id, contact_id, nudge_kind)` are the seam. Stories 7.5 + 7.6 add new kinds without altering the table.
2. **3-strikes-30-days rule applies per (user_id, contact_id, nudge_kind).** Dismissing the share-first-statement nudge does NOT affect the staleness-reminder nudge for the same contact.
3. **Eligibility is read-heavy + may be called on every page load.** Index `(contact_id, owner_subject_id)` on `journal_entries` MUST exist (verify Epic 3 already created it). Endpoint p95 < 100ms per AC2.
4. **Per-user rate limit, not per-IP.** Per-IP would penalize shared NAT (mobile carriers); per-user is correct (Auth0 subject_id is the key).
5. **Telemetry `nudge_shown` fires on eligibility-eligible response, not on actual UI render.** This may slightly over-count (response computed but page closed before render), but the alternative — a separate `/v1/nudges/.../shown` POST — adds complexity for a marginal accuracy gain. Document this in the telemetry payload schema.
6. **Auto-clear is passive (next page load).** Real-time clearing via SignalR is over-engineering at MVP. The user records a debt, returns to dashboard — the nudge naturally re-evaluates and disappears.
7. **The component is consumed unchanged from `libs/domain-ui` per Story 1.9 single-owner rule.** If `<ow-contextual-nudge>` doesn't exist yet, this story scaffolds it per UX-DR11; if it does (from a prior epic), this story extends the kind enum only.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/Nudges/ShareFirstStatement/
│   │       ├── Eligibility/
│   │       │   ├── EligibilityRequest.cs                       # NEW
│   │       │   ├── EligibilityResponse.cs                      # NEW
│   │       │   └── EligibilityHandler.cs                       # NEW
│   │       └── Dismiss/
│   │           ├── DismissRequest.cs                           # NEW
│   │           ├── DismissResponse.cs                          # NEW
│   │           └── DismissHandler.cs                           # NEW
│   ├── Faktuboh.Domain/Nudges/
│   │   ├── NudgeDismissal.cs                                   # NEW
│   │   ├── NudgeKind.cs                                        # NEW (enum-like static class)
│   │   └── INudgeDismissalRepository.cs                        # NEW
│   ├── Faktuboh.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/NudgeDismissalConfiguration.cs   # NEW
│   │   │   └── Migrations/YYYYMMDDHHMMSS_AddNudgeDismissalsTable.cs # NEW
│   │   └── Repositories/NudgeDismissalRepository.cs            # NEW
│   └── Faktuboh.Application/Telemetry/Events/
│       ├── NudgeShownTelemetry.cs                              # NEW
│       ├── NudgeClickedTelemetry.cs                            # NEW
│       └── NudgeDismissedTelemetry.cs                          # NEW
└── tests/Faktuboh.Api.Tests/Slices/Nudges/
    ├── EligibilityTests.cs                                     # NEW
    └── DismissTests.cs                                         # NEW

frontend/
├── apps/owner-workspace/src/app/core/nudges/
│   └── nudges.client.ts                                        # NEW
└── libs/domain-ui/contextual-nudge/
    ├── contextual-nudge.component.ts                           # NEW or EDIT
    ├── contextual-nudge.component.html                         # NEW or EDIT
    └── contextual-nudge.component.spec.ts                      # NEW or EDIT
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3, FluentAssertions |
| Integration (BE) | Testcontainers Postgres + `WebApplicationFactory<Program>` + mock clock for 30-day cooldown |
| Unit (FE) | Vitest + Angular Testing Library |
| E2E | Playwright Chromium |

### Project Structure Notes

This story scaffolds a brand-new `Nudges/` slice. Per vertical-slice discipline, the slice owns the dismissal logic + table + endpoints + telemetry. Stories 7.5 + 7.6 add sibling action folders (`StalenessReminder/`, `ThresholdCrossing/`) to this slice — they do NOT spawn separate slices, since they share the dismissal table.

### Architecture compliance

- [epics.md Epic 7 Story 7.4](../planning-artifacts/epics.md) — story definition
- [ux-design-specification.md UX-DR11](../planning-artifacts/ux-design-specification.md) — ContextualNudge component
- [prd.md FR52a, NFR-S5](../planning-artifacts/prd.md)
- [Story 0.14 telemetry](./0-14-phase-a-telemetry-primitive.md)
- [Story 1.9 component API](./1-9-phase-1-component-api-design-system-library.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | ASP.NET Core RateLimiting | Built-in `AddRateLimiter` |
| Frontend | PrimeNG (Card, Button) | Per UX-DR11 layout |

### Testing requirements

- **Mock clock for 30-day cooldown.** Use `IClock` abstraction (FakeClock injected in tests). Without this, the 30-day expiration test takes 30 days.
- **Cross-kind isolation test** — dismiss `share-first-statement` 3x; assert `staleness-reminder` for same contact is still eligible (when its conditions are met by the test setup).
- **Rate-limit test** — verifies 60-per-minute boundary; without it, eligibility polling on aggressive route changes could DoS the database.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 0.14 — telemetry primitive
- Story 1.9 — component API library + Phase-1 components
- Story 3.1 — record debt (debt-count source)
- Story 4.4 — generate statement link (share path target)

**Stories 7.5 + 7.6 reuse:**
- The `nudge_dismissals` table (this story creates)
- The dismissal cooldown logic (this story implements; 7.5 + 7.6 just add new `nudge_kind` enum values)
- The `<ow-contextual-nudge>` component (this story scaffolds; 7.5 + 7.6 add kind-specific copy)

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.4
- [_bmad-output/planning-artifacts/ux-design-specification.md UX-DR11](../planning-artifacts/ux-design-specification.md)
- [_bmad-output/planning-artifacts/prd.md FR52a, NFR-S5](../planning-artifacts/prd.md)
- [Story 0.14 telemetry](./0-14-phase-a-telemetry-primitive.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
