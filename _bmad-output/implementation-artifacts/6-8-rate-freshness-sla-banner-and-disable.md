# Story 6.8: Rate freshness SLA — banner and disable

Status: ready-for-dev

## Story

As **a user with stale exchange rates**,
I want **a non-dismissible banner when rates are > 4h old and exchange operations disabled when > 24h old**,
so that **I never execute an exchange at a rate that's obviously stale** (NFR-I3, UX-DR32).

**Covers requirements:** Epic 6 Story 6.8 — NFR-I3 (4h banner / 24h disable freshness SLA); UX-DR32 (error/degraded-state recovery patterns); clock-skew handling via DB-authoritative timestamps; viewing remains permitted (only writes blocked) [Source: epics.md Epic 6 Story 6.8; prd.md NFR-I3].

**Why this story now:** Story 6.4 ships Exchange. Without the freshness SLA, a user can execute an exchange against a 3-day-old rate (e.g., during a multi-day primary+secondary outage) without warning. NFR-I3 mandates this as a Day-1 commitment. Story 6.8 closes the freshness loop.

## Acceptance Criteria

### AC1 — Backend `GET /v1/rates/freshness` endpoint

**Given** the frontend needs an authoritative freshness assessment (NOT computed client-side, to avoid clock skew)
**When** the endpoint is called
**Then** the response is `200 OK` with body:
```json
{
  "latestRecordedAt": "2026-04-26T10:00:00Z",
  "nowFromDbServer": "2026-04-26T14:30:00Z",
  "ageHours": 4.5,
  "status": "stale",
  "writesBlocked": false,
  "blockReason": null
}
```
**And** `status` is one of:
- `"fresh"` — age < 4h
- `"stale"` — 4h ≤ age < 24h (banner shown, writes allowed)
- `"degraded"` — age ≥ 24h (banner shown, writes BLOCKED)
**And** `nowFromDbServer` is `now() AT TIME ZONE 'UTC'` from Postgres — NEVER `DateTime.UtcNow` from app server (defends against clock skew per AC8)
**And** `latestRecordedAt` is `MAX(recorded_at) FROM rates` (the freshest rate ingestion across ALL pairs)
**And** the endpoint is unauthenticated-friendly (or scoped to `.RequireAuthorization()` — auth required at MVP for telemetry hygiene; revisit if needed for public-statement) [Source: epics.md Epic 6 Story 6.8 AC; prd.md NFR-I3].

### AC2 — Frontend banner: stale state (4h ≤ age < 24h)

**Given** the response indicates `status: "stale"`
**When** any owner-workspace screen that uses or displays a rate renders
**Then** a non-dismissible `<ow-contextual-nudge severity="warn">` banner shows at the top of the screen:
- Heading: "Rates last updated [N] hours ago" (Transloco; `{hours}` interpolated)
- Body: "Exchange operations may use slightly outdated rates."
- No close button (banner is non-dismissible per NFR-I3)
**And** the banner is positioned ABOVE the page content (sticky-top) but does not overlap content (pushes content down)
**And** the banner is rendered ONCE per page load (not per component) — managed by a top-level shell component
**And** the banner respects the design tokens (warning palette per Story 1.1) [Source: epics.md Epic 6 Story 6.8 AC; UX-DR32].

### AC3 — Frontend banner + disable: degraded state (age ≥ 24h)

**Given** the response indicates `status: "degraded"`
**When** the user opens the Exchange form
**Then** the banner shows with severity `error`:
- Heading: "Rates outdated more than 24 hours"
- Body: "Exchange operations are paused until rates refresh. Viewing existing balances and history remains available."
**And** the Exchange form's Confirm button is DISABLED with tooltip "Exchange operations disabled — rates stale >24h"
**And** the form fields remain editable (user can fill the form, just can't submit)
**And** the disable state lifts automatically when the next `GET /v1/rates/freshness` poll returns `fresh` or `stale` [Source: epics.md Epic 6 Story 6.8 AC; UX-DR32].

### AC4 — Reading remains permitted; only writes blocked

**Given** the degraded state
**When** the user navigates the app
**Then** the following remain accessible:
- Dashboard (Stories 3.5/3.6/3.7) — shows existing balances
- Contact details + history
- Transfer history (Story 5.4)
- Exchange history (Story 6.5)
- Statement viewing (Epic 4)
**And** the following are BLOCKED with the banner:
- Exchange form submit (AC3)
- Any other rate-dependent WRITE operation (none in current scope, but the rule extends)
**And** Transfer (Story 5.2) is NOT blocked — Transfer is single-denomination, doesn't use rates
**And** Record-Debt (Story 3.1) is NOT blocked — also no rate dependency [Source: epics.md Epic 6 Story 6.8 AC].

### AC5 — Banner cannot be suppressed by primary-provider outage alone

**Given** a primary-provider outage triggers Story 6.2 failover to secondary
**When** the secondary continues to deliver rates within freshness window
**Then** the banner does NOT show (because rates ARE fresh, just from a different provider)
**And** if BOTH providers fail and rates age past 4h, the banner shows
**And** the banner reads ONLY `latestRecordedAt` (from any provider) — the failover state is NOT a freshness signal [Source: epics.md Epic 6 Story 6.8 AC4].

### AC6 — DB-authoritative timestamp (clock-skew defense)

**Given** the user's local clock may drift from the server
**When** the endpoint computes age
**Then** the calculation uses `EXTRACT(EPOCH FROM (now() - max(recorded_at))) / 3600` (Postgres-side)
**And** the response returns BOTH `nowFromDbServer` (for client display reference) AND `ageHours` (server-computed)
**And** the frontend uses `ageHours` for the threshold logic, NOT a client-side `Date.now()` calculation
**And** if a client-side display needs "X hours ago" rendering, it uses `nowFromDbServer` as the reference (not `Date.now()`)
**And** integration test asserts: clock-skewed clients still get correct status [Source: epics.md Epic 6 Story 6.8 AC5].

### AC7 — Polling: every 60 seconds while app is active

**Given** the freshness state can change (new rate fetched at hourly tick)
**When** the owner-workspace is active (tab focused)
**Then** the freshness endpoint is polled every 60 seconds
**And** polling pauses when tab is hidden (`document.visibilityState === 'hidden'`)
**And** polling resumes immediately on tab refocus
**And** the polling helper lives in a dedicated service `libs/core/rate-freshness.service.ts` (single source)

### AC8 — Telemetry: `rate_freshness_check`, `rate_freshness_state_changed`

**Given** AR-047 telemetry primitive
**When** the freshness endpoint serves
**Then** `rate_freshness_check` event fires per request: `{ status, age_hours_bucket }`
**And** when the status changes between two consecutive polls, `rate_freshness_state_changed` fires: `{ from_status, to_status, age_hours_bucket }`
**And** PII guard validates [Source: Story 0.14].

### AC9 — `<ow-contextual-nudge>` component reused

**Given** Story 1.8 ships `<ow-contextual-nudge>` per UX patterns
**When** this story renders the banner
**Then** the existing component is consumed UNCHANGED
**And** if the component lacks a "non-dismissible" prop (per NFR-I3), this story adds it as a single-owner-rule amendment to that component (with sign-off)
**And** the alternative is to inline a `<p-message>` PrimeNG component if the contextual-nudge cannot be made non-dismissible

### AC10 — Backend: writes blocked at handler level (defense-in-depth)

**Given** UI disable is the user-facing guard, but backend must also enforce
**When** Story 6.4's `ExchangeDebtHandler` executes
**Then** before invoking the compound persister, the handler checks freshness via `IRateFreshnessService.GetCurrentStatusAsync()`
**And** if `status == "degraded"`, throws `RatesStaleException` → RFC 9457 `503 Service Unavailable` with code `faktuboh.exchanges.rates_stale`
**And** the integration test asserts: backend rejects Exchange when rates > 24h, even if UI is bypassed
**And** the same check applies to any future write that uses rates [Source: defense-in-depth principle].

## Tasks / Subtasks

### Backend — Service + endpoint

- [ ] **T1 (AC: 1, 6)** — Create `Faktuboh.Application/Rates/IRateFreshnessService.cs` + `RateFreshnessService.cs`
  - [ ] `GetCurrentStatusAsync()` returns `(LatestRecordedAt, NowFromDbServer, AgeHours, Status)`
  - [ ] DB query uses Postgres-side `now() - max(recorded_at)` calc
- [ ] **T2 (AC: 1)** — Create `backend/src/Faktuboh.Api/Slices/Rates/Freshness/`
  - [ ] `Freshness.Endpoints.cs` with `GET /v1/rates/freshness`
  - [ ] `FreshnessResponse` per AC1 shape
  - [ ] `GetFreshnessHandler`

### Backend — Defense-in-depth on writes

- [ ] **T3 (AC: 10)** — Modify Story 6.4's `ExchangeDebtHandler`
  - [ ] Inject `IRateFreshnessService`
  - [ ] Pre-check before persister; throw `RatesStaleException` on degraded
  - [ ] Register code `faktuboh.exchanges.rates_stale` (503)
- [ ] **T4 (AC: 10)** — Integration test: backend rejects Exchange when rates degraded

### Backend — Telemetry

- [ ] **T5 (AC: 8)** — Wire `rate_freshness_check` + `rate_freshness_state_changed` events
  - [ ] State-change detection requires comparison; either store last-status in user session or detect on the client side
  - [ ] Default: client-side state-change detection (since polling is client-driven), backend emits per-request

### Frontend — Polling service

- [ ] **T6 (AC: 7)** — Create `libs/core/rate-freshness.service.ts`
  - [ ] Polls every 60s via `rxResource()` or `setInterval` + signal
  - [ ] Pauses on `document.visibilityState === 'hidden'`
  - [ ] Resumes on focus
  - [ ] Exposes `currentStatus = signal<FreshnessStatus>('fresh')`

### Frontend — Banner

- [ ] **T7 (AC: 2, 3)** — Mount banner in app shell
  - [ ] Modify `apps/owner-workspace/src/app/app.component.ts` to read `currentStatus` signal
  - [ ] Render `<ow-contextual-nudge severity="warn|error" [dismissible]="false">` based on status
  - [ ] Position sticky-top; pushes content
- [ ] **T8 (AC: 9)** — Verify `<ow-contextual-nudge>` supports non-dismissible
  - [ ] If not, amend the component (single-owner rule applies — touch its API + sign-off)

### Frontend — Exchange form disable

- [ ] **T9 (AC: 3, 4)** — Modify Story 6.4's `exchange-form.component.ts`
  - [ ] Read `currentStatus` signal
  - [ ] Disable Confirm button when `degraded`
  - [ ] Tooltip text per AC3
  - [ ] Form fields remain editable

### Frontend — Generated client

- [ ] **T10 (AC: 1)** — Extend `libs/core/api/rates.client.ts`
  - [ ] Add `getFreshness(): Observable<FreshnessResponse>`

### Tests — Backend

- [ ] **T11 (AC: 1, 6)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/Rates/Freshness/`
  - [ ] Fresh state: returns `fresh`
  - [ ] Stale state (seed rate at 5h ago): returns `stale`
  - [ ] Degraded state (seed rate at 25h ago): returns `degraded`
  - [ ] DB-authoritative timestamp test (clock-skewed env)
- [ ] **T12 (AC: 10)** — Exchange backend rejection test
  - [ ] Seed degraded rates; submit Exchange; assert 503

### Tests — Frontend

- [ ] **T13 (AC: 2, 3, 7)** — Vitest tests for service + banner rendering
  - [ ] Polling lifecycle (active/hidden/refocus)
  - [ ] Banner state transitions
- [ ] **T14 (AC: 3)** — Component test: Exchange form Confirm disabled when degraded
- [ ] **T15 (AC: 2-4)** — Playwright E2E
  - [ ] Mock API to return stale → assert banner appears
  - [ ] Mock API to return degraded → assert Exchange disable
  - [ ] Read paths (dashboard, history) remain accessible
  - [ ] axe-core a11y assertion

## Dev Notes

### Critical guardrails (do not violate)

1. **DB-authoritative timestamp.** `now() - max(recorded_at)` runs in Postgres. Never use `DateTime.UtcNow` on the app server (clock skew between app pods + DB is a real issue).
2. **Banner is non-dismissible.** Per NFR-I3. The user cannot click X to hide it. The only way it goes away is when rates refresh.
3. **Reading is always permitted.** Only WRITES that depend on rates are blocked. Dashboard/history/statements all keep working.
4. **Defense-in-depth.** UI disable is the friendly guard; backend rejection is the hard guard. Both layers required.
5. **Banner reflects rates from ANY provider.** Failover state is irrelevant — only `latestRecordedAt` matters.
6. **Polling pauses when tab hidden.** Avoids battery drain on background tabs.
7. **60-second poll is calibrated.** More frequent = wasted requests; less frequent = stale UI state.
8. **Don't extend the freshness check to non-rate writes.** Transfer + Record-Debt don't depend on rates; blocking them would be wrong.
9. **`<ow-contextual-nudge>` single-owner rule.** If you need non-dismissible support, amend the component's API (ESLint allowlist) — don't fork it inline.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Application/Rates/
│   ├── IRateFreshnessService.cs                            # NEW
│   └── RateFreshnessService.cs                             # NEW
├── Faktuboh.Api/Slices/Rates/Freshness/
│   ├── Freshness.Endpoints.cs                              # NEW
│   ├── FreshnessResponse.cs                                # NEW
│   └── GetFreshnessHandler.cs                              # NEW
├── Faktuboh.Api/Slices/JournalEntries/Exchange/
│   └── ExchangeDebtHandler.cs                              # MODIFY (rates-stale check)
├── Faktuboh.Domain/Rates/
│   └── RatesStaleException.cs                              # NEW
└── Faktuboh.Application/Errors/
    └── ErrorCatalog.cs                                     # MODIFY (+ rates_stale code)

apps/owner-workspace/
├── src/app/app.component.ts                                # MODIFY (mount banner)
├── src/app/features/exchanges/exchange-form/
│   └── exchange-form.component.ts                          # MODIFY (disable on degraded)
└── src/assets/i18n/{ar,en}.json                            # MODIFY (rate-freshness.* keys)

libs/core/
├── rate-freshness.service.ts                               # NEW
└── api/rates.client.ts                                     # MODIFY (+ getFreshness)

backend/tests/
├── Faktuboh.Api.Tests/Slices/Rates/Freshness/
│   └── FreshnessEndpointTests.cs                           # NEW
└── Faktuboh.Api.Tests/Slices/JournalEntries/Exchange/
    └── ExchangeRatesStaleRejectionTests.cs                 # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Integration | Endpoint states + DB timestamp + handler rejection | Testcontainers |
| Unit/Component | Service polling + banner + form disable | Vitest |
| E2E | Banner appears + Exchange disabled + reads work | Playwright |

### Architecture compliance

- **NFR-I3** — 4h banner / 24h disable freshness SLA.
- **UX-DR32** — degraded-state patterns.
- **§5.4** — frontend conventions.
- **Defense-in-depth** — UI + backend both enforce.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Postgres `now()` | DB-authoritative time |
| Frontend | Angular signals + visibilitychange API | Polling lifecycle |
| UI | `<ow-contextual-nudge>` | Single-owner; possibly extended for non-dismissible |

### Testing requirements

- **Integration:** all 3 freshness states + clock-skew defense + handler rejection.
- **Component:** banner rendering + form disable.
- **E2E:** end-to-end flow with mocked API states.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.14 — telemetry primitive
- Story 1.8 — `<ow-contextual-nudge>` component
- Story 6.1 — `rates` table + `recorded_at` column
- Story 6.2 — failover (banner does NOT depend on this)
- Story 6.4 — `ExchangeDebtHandler` (modified here for backend rejection)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.8
- NFR-I3 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- UX-DR32 — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md)
- Story 6.1 rate-feed — [_bmad-output/implementation-artifacts/6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md](./6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md)
- Story 6.4 Exchange — [_bmad-output/implementation-artifacts/6-4-exchange-operation-with-locked-rate-and-user-override.md](./6-4-exchange-operation-with-locked-rate-and-user-override.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- DB-authoritative timestamps defend against clock skew.
- Banner non-dismissible per NFR-I3; reading always permitted; only Exchange writes blocked.
- Defense-in-depth: UI disable + backend `RatesStaleException`.

### File List

_Populated during implementation._
