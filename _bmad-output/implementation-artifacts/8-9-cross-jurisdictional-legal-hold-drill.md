# Story 8.9: Cross-jurisdictional legal-hold drill

Status: ready-for-dev

## Story

As **Wahid**,
I want **a simulation of a Saudi court order arriving during an in-flight GDPR erasure request, with deterministic precedence resolution**,
so that **AR-044 #2 is validated before real production collisions** (LegalHold precedence) [Source: epics.md Epic 8 Story 8.9; architecture.md AR-044, §3.5].

**Covers requirements:** Epic 8 — AR-044 #2 (cross-jurisdictional rehearsal), §3.5 LegalHold aggregate + state machine, §7.5.7 ops-readiness deferral, ADR-004 GDPR erasure flow [Source: epics.md Story 8.9; architecture.md §3.5].

**Why this story now:** Story 8.8 just shipped restore drill. This story adds the second of three operational rehearsals — the cross-jurisdictional precedence rehearsal. Sequenced before 8.10 (rotation drill) because rotation rehearsal can be done annually; legal-hold collision is more urgent given the multi-jurisdiction MENA + EU user base from Day 1.

## Acceptance Criteria

### AC1 — `LegalHold` aggregate + state machine

**Given** the architecture §3.5 specifies `LegalHold` aggregate with state machine
**When** `Faktuboh.Domain/LegalHolds/LegalHold.cs` is implemented (this story is the first to land it)
**Then** the aggregate has:
- `Id: Guid`
- `OwnerSubjectId: Guid` — the affected user
- `Jurisdiction: string` — ISO 3166-1 alpha-2 (e.g., `"SA"` for Saudi Arabia, `"DE"` for Germany)
- `IssuingAuthority: string` — court name + reference
- `ReceivedAt: DateTimeOffset`
- `State: LegalHoldState` — enum: `Issued | Active | Released | Expired`
- `EffectiveAt: DateTimeOffset` — when the hold becomes Active (often == ReceivedAt; may be future)
- `ExpiresAt: DateTimeOffset?` — null for indefinite holds
- `ReleasedAt: DateTimeOffset?`
- `ReleaseReason: string?`
- `CourtOrderDocumentReference: string` — e.g., a SHA256 hash of the PDF stored separately (NOT the PDF itself in DB)
**And** state transitions:
- `Issued → Active` (on `EffectiveAt` reached, or manual activation by admin)
- `Active → Released` (admin marks released after court rescinds)
- `Active → Expired` (when `ExpiresAt` reached, automatic)
**And** invalid transitions throw `LegalHoldStateTransitionException` (e.g., `Released → Active`)
**And** the aggregate implements `IBitemporal` per ADR-022 — every state change has history [Source: architecture.md §3.5].

### AC2 — Migration for `legal_holds` table

**Given** the aggregate needs persistence
**When** migration `YYYYMMDDHHMMSS_AddLegalHoldsTable.cs` lands
**Then** the table:
```sql
CREATE TABLE legal_holds (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_subject_id uuid NOT NULL,
    jurisdiction text NOT NULL CHECK (length(jurisdiction) = 2),
    issuing_authority text NOT NULL,
    received_at timestamptz NOT NULL,
    state text NOT NULL CHECK (state IN ('Issued', 'Active', 'Released', 'Expired')),
    effective_at timestamptz NOT NULL,
    expires_at timestamptz NULL,
    released_at timestamptz NULL,
    release_reason text NULL,
    court_order_document_reference text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_legal_holds_owner_active
    ON legal_holds(owner_subject_id) WHERE state = 'Active';
CREATE INDEX ix_legal_holds_state_effective
    ON legal_holds(state, effective_at);
```
**And** bitemporal history table created per ADR-022 [Source: architecture.md §3.5].

### AC3 — `DeferredErasure` aggregate + table

**Given** GDPR erasure must be deferred during an active legal hold
**When** `Faktuboh.Domain/DeferredErasures/DeferredErasure.cs` lands
**Then** the aggregate has:
- `Id: Guid`
- `OwnerSubjectId: Guid`
- `OriginalRequestAt: DateTimeOffset`
- `BlockingLegalHoldId: Guid` — FK to `legal_holds`
- `State: DeferredErasureState` — `Pending | Executed | Cancelled`
- `ExecutedAt: DateTimeOffset?`
**And** migration `YYYYMMDDHHMMSS_AddDeferredErasuresTable.cs` adds:
```sql
CREATE TABLE deferred_erasures (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_subject_id uuid NOT NULL,
    original_request_at timestamptz NOT NULL,
    blocking_legal_hold_id uuid NOT NULL REFERENCES legal_holds(id),
    state text NOT NULL CHECK (state IN ('Pending', 'Executed', 'Cancelled')),
    executed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_deferred_erasures_pending
    ON deferred_erasures(blocking_legal_hold_id) WHERE state = 'Pending';
```
[Source: epics.md Story 8.9 AC; architecture.md §3.5].

### AC4 — Erasure flow checks for active legal hold

**Given** Story 1.7's GDPR erasure flow exists
**When** the user submits an erasure request
**Then** the erasure handler `ExecuteUserErasureHandler`:
1. Queries `legal_holds WHERE owner_subject_id = ? AND state = 'Active' AND (expires_at IS NULL OR expires_at > now())`
2. If a row exists, the erasure is DEFERRED — insert `deferred_erasures` row with `state = Pending` + `blocking_legal_hold_id`
3. Returns 202 Accepted with body `{ status: 'deferred', reason: 'legal_hold_active', estimated_resolution: 'unknown' }`
4. If no active hold, proceeds with erasure normally
**And** Story 1.7's handler is modified to call this check FIRST
**And** an integration test asserts: erasure request with active hold → deferred row created + erasure NOT executed [Source: epics.md Story 8.9 AC; Story 1.7].

### AC5 — Hold release auto-executes deferred erasures

**Given** a Saudi court rescinds the hold
**When** the admin marks the `LegalHold` as `Released` (via admin endpoint OR direct domain event)
**Then** a Wolverine handler `OnLegalHoldReleased` consumes the `LegalHoldReleasedEvent`:
1. Queries `deferred_erasures WHERE blocking_legal_hold_id = ? AND state = 'Pending'`
2. For each, triggers `ExecuteUserErasureCommand` (re-runs the erasure)
3. Updates `deferred_erasures.state = Executed` + `executed_at = now()`
**And** the auto-execute is logged in the audit trail with `action = 'admin.legal_hold.released_auto_executed_erasure'`
**And** an integration test asserts: hold released → all pending deferred erasures execute within 1s [Source: epics.md Story 8.9 AC auto-execute].

### AC6 — Admin endpoints for legal-hold management

**Given** the admin must create + transition + release holds
**When** the admin endpoints are exposed
**Then** the endpoints under `Slices/Admin/LegalHolds/`:
- `POST /v1/admin/legal-holds` — create new hold (state=Issued)
- `POST /v1/admin/legal-holds/{id}/activate` — transition Issued → Active
- `POST /v1/admin/legal-holds/{id}/release` — transition Active → Released (requires reason)
- `GET /v1/admin/legal-holds?state={state}` — list holds
- `GET /v1/admin/legal-holds/{id}` — detail
**And** all endpoints inherit Story 8.1 admin auth + audit
**And** all destructive transitions (release) use `<p-confirmDialog>` per UX-DR29 [Source: epics.md Story 8.9 implicit admin UI; Story 8.1].

### AC7 — Drill execution: simulated Saudi court order during in-flight erasure

**Given** the drill validates the precedence rule end-to-end
**When** `tools/LegalHoldDrill/RunDrill.cs` executes against staging:
1. Create test user `subject_id = X` + seed contacts/debts
2. User X submits GDPR erasure → erasure starts
3. **Simultaneously** (within 500ms), POST a `LegalHold` with `Jurisdiction=SA` + `State=Issued` + `EffectiveAt=now()`
4. Activate the hold immediately
5. Verify: erasure for X is DEFERRED (deferred_erasures row created); erasure NOT executed
6. Wait 5 seconds (simulating court rescission)
7. POST hold release with reason "drill - court rescinded"
8. Verify: deferred erasure auto-executes within 1s; user X's PII is now NULL/key-shredded
**And** the drill produces a log file `docs/runbooks/legal-hold-drill-YYYY-MM-DD.md`
**And** assertions are made via integration test framework (not just CLI grep) — the drill IS an integration test, just gated to staging [Source: epics.md Story 8.9 AC drill].

### AC8 — Audit trail for the entire flow

**Given** every legal-hold state change + every deferred erasure is auditable
**When** the drill executes
**Then** audit-log rows are written for:
- `admin.legal_hold.created`
- `admin.legal_hold.activated`
- `system.erasure.deferred` (with `payload.blocking_legal_hold_id`)
- `admin.legal_hold.released`
- `system.erasure.auto_executed_after_hold_release`
**And** every row has correct `processing_activity` (admin or system)
**And** the audit trail can be reconstructed in chronological order via `audit_log` query [Source: epics.md Story 8.9 AC audit trail; prd.md NFR-S10].

### AC9 — Telemetry — `legal_hold_collision_detected`

**Given** AR-047 telemetry primitive
**When** an erasure is deferred due to a hold
**Then** `TelemetryEmitter.Emit("legal_hold_collision_detected", new LegalHoldCollisionTelemetry(opaque_user_id, jurisdiction, days_since_hold_received_bucket))` fires
**And** when a deferred erasure auto-executes, `legal_hold_deferred_erasure_executed` fires [Source: Story 0.14].

### AC10 — Annual cadence + flag

**Given** AR-044 #2 specifies annual rehearsal
**When** the drill completes successfully
**Then** a marker file `docs/runbooks/legal-hold-drill-completed-YYYY-MM-DD.flag` is committed
**And** GitHub Action `.github/workflows/annual-legal-hold-drill-reminder.yml` creates an issue 365 days later [Source: epics.md Story 8.9 AC annual].

## Tasks / Subtasks

### Backend — Domain types

- [ ] **T1 (AC: 1)** — Create `LegalHold` aggregate + `LegalHoldState` enum + `LegalHoldStateTransitionException` in `Faktuboh.Domain/LegalHolds/`
  - [ ] State-transition methods: `Activate()`, `Release(reason)`, `MarkExpired()`
  - [ ] Implements `IBitemporal`
- [ ] **T2 (AC: 3)** — Create `DeferredErasure` aggregate + `DeferredErasureState` enum in `Faktuboh.Domain/DeferredErasures/`
  - [ ] Methods: `MarkExecuted()`, `Cancel()`

### Backend — Migrations

- [ ] **T3 (AC: 2)** — Migration `YYYYMMDDHHMMSS_AddLegalHoldsTable.cs` per AC2
- [ ] **T4 (AC: 3)** — Migration `YYYYMMDDHHMMSS_AddDeferredErasuresTable.cs` per AC3

### Backend — Erasure flow integration

- [ ] **T5 (AC: 4)** — Modify Story 1.7's `ExecuteUserErasureHandler` to call legal-hold check first
  - [ ] Inject `ILegalHoldRepository`
  - [ ] Branch on active-hold result

### Backend — Hold-release event handler

- [ ] **T6 (AC: 5)** — Define `LegalHoldReleasedEvent` integration event in `Faktuboh.Domain.Events.IntegrationEvents/`
- [ ] **T7 (AC: 5)** — Create Wolverine handler `OnLegalHoldReleased` in `Slices/LegalHolds/Projections/`
  - [ ] Queries deferred erasures + triggers re-execution

### Backend — Admin endpoints

- [ ] **T8 (AC: 6)** — Create slice action group `Slices/Admin/LegalHolds/` with 5 actions:
  - [ ] `Create/CreateLegalHoldHandler.cs`
  - [ ] `Activate/ActivateLegalHoldHandler.cs`
  - [ ] `Release/ReleaseLegalHoldHandler.cs` (with reason validation)
  - [ ] `List/ListLegalHoldsHandler.cs`
  - [ ] `Get/GetLegalHoldHandler.cs`

### Backend — Telemetry

- [ ] **T9 (AC: 9)** — Define telemetry events + wire emission

### Drill tool

- [ ] **T10 (AC: 7)** — Create `tools/LegalHoldDrill/`
  - [ ] `Program.cs` orchestrates the 8-step drill per AC7
  - [ ] Outputs results to a dated runbook file

### Frontend — Admin UI

- [ ] **T11 (AC: 6)** — Create `apps/owner-workspace/src/app/features/admin/legal-holds/`
  - [ ] List + detail + create + activate + release components
  - [ ] `<p-confirmDialog>` on release (destructive)

### Frontend — API client

- [ ] **T12 (AC: 6)** — Extend `admin.client.ts` with legal-hold methods

### Annual reminder

- [ ] **T13 (AC: 10)** — Create `.github/workflows/annual-legal-hold-drill-reminder.yml`

### Tests — Backend

- [ ] **T14 (AC: 1)** — Unit tests for `LegalHold` state machine
  - [ ] Each valid transition + invalid transitions throw
- [ ] **T15 (AC: 4)** — Integration test for erasure-with-active-hold deferral
- [ ] **T16 (AC: 5)** — Integration test for hold-release auto-execute
- [ ] **T17 (AC: 7)** — Drill integration test (runnable as `dotnet test --filter "Category=Drill"`)
- [ ] **T18 (AC: 8)** — Audit-trail reconstruction test
- [ ] **T19 (AC: 9)** — Telemetry assertions

### Tests — Frontend

- [ ] **T20 (AC: 6)** — Vitest tests for legal-holds components
- [ ] **T21 (AC: 6)** — Playwright E2E for admin flow

## Dev Notes

### Critical guardrails (do not violate)

1. **Saudi court order takes precedence over GDPR erasure.** The legal-hold check happens BEFORE erasure executes. Without this, GDPR compliance for EU users could conflict with Saudi data-residency obligations + erase data the court has subpoenaed.
2. **`DeferredErasure` is the seam.** When the hold releases, the deferred erasure auto-executes — the GDPR right is honored eventually.
3. **State transitions are append-only.** `IBitemporal` history captures every change. Court-traceable audit trail.
4. **Annual cadence per AR-044 #2.** GitHub Action creates the next-year reminder; institutional memory.
5. **The drill is an INTEGRATION TEST, not just a CLI script.** Assertions framed in xUnit; failures break a build.
6. **Court order document reference is a HASH, not the PDF.** The PDF lives elsewhere (legal storage); the DB stores the hash for verifiable correlation.
7. **Indefinite holds (`expires_at = NULL`) are common.** Don't assume all holds expire.
8. **`<p-confirmDialog>` on release.** Releasing a hold could trigger downstream erasures — destructive in effect.
9. **Audit every state change.** Without it, the chain of custody is broken.
10. **The drill validates the entire chain end-to-end.** Mocking the legal-hold check in unit tests doesn't suffice — the full path including Wolverine event delivery + auto-execute must be exercised.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Domain/
│   │   ├── LegalHolds/
│   │   │   ├── LegalHold.cs                                    # NEW
│   │   │   ├── LegalHoldState.cs                               # NEW
│   │   │   ├── LegalHoldStateTransitionException.cs            # NEW
│   │   │   └── ILegalHoldRepository.cs                         # NEW
│   │   ├── DeferredErasures/
│   │   │   ├── DeferredErasure.cs                              # NEW
│   │   │   ├── DeferredErasureState.cs                         # NEW
│   │   │   └── IDeferredErasureRepository.cs                   # NEW
│   │   └── Events/IntegrationEvents/
│   │       └── LegalHoldReleasedEvent.cs                       # NEW
│   ├── Faktuboh.Api/Slices/
│   │   ├── Admin/LegalHolds/
│   │   │   ├── Create/                                         # NEW
│   │   │   ├── Activate/                                       # NEW
│   │   │   ├── Release/                                        # NEW
│   │   │   ├── List/                                           # NEW
│   │   │   └── Get/                                            # NEW
│   │   ├── LegalHolds/Projections/
│   │   │   └── OnLegalHoldReleased.cs                          # NEW
│   │   └── Users/DeleteAccount/
│   │       └── ExecuteUserErasureHandler.cs                    # MODIFY (add hold check)
│   ├── Faktuboh.Application/Telemetry/Events/
│   │   ├── LegalHoldCollisionTelemetry.cs                      # NEW
│   │   └── LegalHoldDeferredErasureExecutedTelemetry.cs        # NEW
│   └── Faktuboh.Infrastructure/Persistence/Migrations/
│       ├── YYYYMMDDHHMMSS_AddLegalHoldsTable.cs                # NEW
│       └── YYYYMMDDHHMMSS_AddDeferredErasuresTable.cs          # NEW
└── tests/Faktuboh.Api.Tests/Slices/
    ├── LegalHolds/
    │   ├── LegalHoldStateMachineTests.cs                       # NEW (Unit)
    │   ├── ErasureWithActiveHoldTests.cs                       # NEW (Integration)
    │   └── HoldReleaseAutoExecuteTests.cs                      # NEW (Integration)
    └── Drill/
        └── LegalHoldDrillTests.cs                              # NEW (Drill category)

tools/
└── LegalHoldDrill/
    ├── LegalHoldDrill.csproj                                   # NEW
    └── Program.cs                                              # NEW

frontend/
└── apps/owner-workspace/src/app/features/admin/legal-holds/
    ├── legal-holds-list.component.ts                           # NEW
    ├── legal-hold-detail.component.ts                          # NEW
    ├── legal-hold-create.component.ts                          # NEW
    └── *.spec.ts files

docs/runbooks/
├── legal-hold-drill-template.md                                # NEW
├── legal-hold-drill-YYYY-MM-DD.md                              # NEW (per execution)
└── legal-hold-drill-completed-YYYY-MM-DD.flag                  # NEW

.github/workflows/
└── annual-legal-hold-drill-reminder.yml                        # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 (state machine) |
| Integration (BE) | Testcontainers Postgres + real Wolverine |
| Drill | xUnit v3 with `[Trait("Category", "Drill")]` — runs against staging |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story scaffolds two new domain types (`LegalHold`, `DeferredErasure`) + a new admin slice action group + modifies Story 1.7's erasure handler. The `LegalHolds/Projections/` folder lives under a new `Slices/LegalHolds/` slice (not `Admin/`) because the projection consumes domain events asynchronously — it is operationally separate from the admin UI surface even though both share the LegalHold aggregate.

### Architecture compliance

- [epics.md Epic 8 Story 8.9](../planning-artifacts/epics.md)
- [architecture.md §3.5 LegalHold + AR-044 #2](../planning-artifacts/architecture.md)
- [adr-004 GDPR erasure](../planning-artifacts/adrs/adr-004-day-one-compliance-posture.md)
- [adr-022 bitemporal](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [Story 1.7 erasure flow](./1-7-account-deletion-gdpr-erasure.md)
- [Story 8.1 admin auth](./8-1-admin-dashboard-with-platform-metrics.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | EF Core 10 + bitemporal | Per ADR-022 |
| Frontend | PrimeNG | Standard |

### Testing requirements

- **State-machine unit tests** — covers all transitions + all invalid attempts.
- **Erasure-deferral integration test** — without it, the precedence rule could regress.
- **Auto-execute on release** — without it, deferred erasures could go un-honored.
- **Drill end-to-end** — proves the rehearsal works before any real collision arrives.

### Previous Story Intelligence

**Prerequisites:**
- Story 1.7 — GDPR erasure flow (modified by this story)
- Story 8.1 — admin slice + auth
- Story 0.14 — telemetry
- Story 3.8 — audit log

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.9
- [_bmad-output/planning-artifacts/architecture.md §3.5, AR-044](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/adrs/adr-004-day-one-compliance-posture.md](../planning-artifacts/adrs/adr-004-day-one-compliance-posture.md)
- [Story 1.7](./1-7-account-deletion-gdpr-erasure.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
