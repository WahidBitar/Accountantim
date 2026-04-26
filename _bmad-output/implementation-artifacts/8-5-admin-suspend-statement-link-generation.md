# Story 8.5: Admin suspend statement link generation

Status: ready-for-dev

## Story

As **Wahid**,
I want to **temporarily suspend statement link generation for a flagged account**,
so that **abuse investigations don't require disabling the entire account** (FR47) [Source: epics.md Epic 8 Story 8.5; prd.md FR47].

**Covers requirements:** Epic 8 — FR47 (account suspension scoped to link generation), NFR-S10 admin elevated auth + audit logging, link-generation gate (enforced in Story 4.4 + 4.10 endpoints) [Source: epics.md Story 8.5].

**Why this story now:** Story 8.4 publishes `ReportFlaggedForActionEvent`; this story owns the `account_suspensions` table + the consumer that creates rows + the gate that enforces them. Without this story, Story 8.4's flagging is a no-op.

## Acceptance Criteria

### AC1 — `account_suspensions` Postgres table

**Given** the suspension is forward-looking (existing links remain active)
**When** migration `YYYYMMDDHHMMSS_AddAccountSuspensionsTable.cs` lands
**Then** the table:
```sql
CREATE TABLE account_suspensions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_subject_id uuid NOT NULL,
    statement_links_suspended boolean NOT NULL DEFAULT true,
    reason text NOT NULL,                    -- e.g., "fraud-report", "manual"
    suspended_by_subject_id uuid NULL,       -- admin's subject_id; NULL for auto-flag
    suspended_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,         -- default suspended_at + 30d
    lifted_at timestamptz NULL,              -- non-null when admin lifts manually
    lifted_by_subject_id uuid NULL,
    lift_reason text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_account_suspensions_owner_active
    ON account_suspensions(owner_subject_id)
    WHERE lifted_at IS NULL AND statement_links_suspended = true;
CREATE INDEX ix_account_suspensions_expires
    ON account_suspensions(expires_at)
    WHERE lifted_at IS NULL AND statement_links_suspended = true;
```
**And** the table is bitemporal (implements `IBitemporal` per ADR-022) — `account_suspensions_history` created in same migration
**And** an integration test asserts insert + bitemporal history-row creation [Source: epics.md Story 8.5 AC; ADR-022].

### AC2 — Wolverine handler `OnReportFlaggedForAction` creates suspension

**Given** Story 8.4 publishes `ReportFlaggedForActionEvent`
**When** the event arrives
**Then** the handler `OnReportFlaggedForAction` (already scaffolded in Story 8.4 T8 — this story owns its body):
1. Checks for existing active suspension for `owner_subject_id` — if exists, no-op (idempotent)
2. Otherwise inserts `account_suspensions` row with:
   - `owner_subject_id = evt.OwnerSubjectId`
   - `reason = evt.Reason` ("fraud-report" if from Story 8.4 flag)
   - `suspended_by_subject_id = NULL` if `evt.AutoFlagged`, else the admin's subject_id
   - `expires_at = evt.DefaultExpiresAt` (30 days from `suspended_at` per Story 8.4 AC4)
3. Publishes `AccountSuspensionCreatedEvent` integration event
4. Writes audit-log row [Source: Story 8.4 AC4; epics.md Story 8.5].

### AC3 — Link-generation endpoint gate

**Given** `POST /v1/contacts/{id}/statements` (Story 4.4 link generation) must enforce the suspension
**When** the endpoint is called
**Then** before generating the link, the handler queries:
```sql
SELECT 1 FROM account_suspensions
WHERE owner_subject_id = :ownerSubjectId
  AND statement_links_suspended = true
  AND lifted_at IS NULL
  AND expires_at > now()
LIMIT 1
```
**And** if a row matches, the endpoint returns 403 RFC 9457 with code `faktuboh.admin.account_suspended` + body containing:
- `title`: "Statement link generation suspended"
- `detail`: "Your account is under review. New statement links cannot be generated until the review concludes."
- `expiresAt`: ISO 8601 timestamp (gives the user a sense of when they can retry)
**And** existing links remain active — recipients viewing prior shares are unaffected
**And** Story 4.10's link-generation handler imports a `IAccountSuspensionGate` interface from `Faktuboh.Application/Suspensions/` (defined in this story) [Source: epics.md Story 8.5 AC; Story 4.4, Story 4.10].

### AC4 — Admin UI to manage suspensions

**Given** the admin needs visibility + control
**When** the admin navigates to `/admin/suspensions`
**Then** the page renders a `<p-table>` listing all active suspensions with columns:
- Owner subject_id (opaque short)
- Reason
- Suspended at
- Expires at
- Suspended by (admin name OR "Auto-flag system")
- Action button: Lift suspension
**And** filter by status (active / lifted / expired) via pills
**And** the data source is `GET /v1/admin/suspensions?status=active`
**And** all admin auth + audit + minimum-k inheritance from Story 8.1 applies [Source: epics.md Story 8.5 AC admin UI].

### AC5 — "Lift suspension" action

**Given** the admin wants to lift a suspension early
**When** the admin taps "Lift suspension" on a row
**Then** `<p-confirmDialog>` opens with header "Lift this suspension?" + body explaining the owner can immediately resume link generation
**And** confirm triggers `POST /v1/admin/suspensions/{id}/lift` with body `{ reason: string }` (admin must provide a reason — required, max 500 chars)
**And** the row updates `lifted_at = now()`, `lifted_by_subject_id = admin's id`, `lift_reason = body.reason`
**And** `AccountSuspensionLiftedEvent` integration event publishes
**And** an audit-log entry is written
**And** the next call to `POST /v1/contacts/{id}/statements` from the owner succeeds [Source: epics.md Story 8.5 AC lift].

### AC6 — Auto-expiration handling

**Given** suspensions have `expires_at` (default 30 days)
**When** `expires_at < now()` AND `lifted_at IS NULL`
**Then** the link-generation gate (AC3) does NOT match → owner can resume generation naturally
**And** no scheduled job is needed at MVP — the gate query already excludes expired suspensions
**And** an integration test seeds an expired suspension; asserts link generation succeeds [Source: epics.md Story 8.5 AC implicit].

### AC7 — Telemetry — `admin_suspension_created` + `admin_suspension_lifted`

**Given** AR-047 telemetry
**When** suspension created/lifted
**Then** events emit:
- `admin_suspension_created` — `{ opaque_owner_id, reason, was_auto_flagged, expires_in_days_bucket }`
- `admin_suspension_lifted` — `{ opaque_owner_id, days_active_bucket, lifted_by_admin: bool, was_auto_expiration: bool }`
**And** PiiGuard validates [Source: Story 0.14].

### AC8 — Suspension does NOT affect viewing/exporting/sharing existing links

**Given** the suspension is scoped to NEW link generation only
**When** a suspended owner attempts to view existing data, export CSVs, or recipients view prior links
**Then** all those paths succeed normally
**And** the suspension is enforced ONLY at `POST /v1/contacts/{id}/statements` (link creation)
**And** an integration test asserts: suspended owner can still view contacts, list debts, export CSV, and prior recipients can still view shared statements [Source: epics.md Story 8.5 AC suspension scope].

## Tasks / Subtasks

### Backend — Migration + table

- [ ] **T1 (AC: 1)** — Create migration `YYYYMMDDHHMMSS_AddAccountSuspensionsTable.cs` per AC1
  - [ ] Table + bitemporal history table per ADR-022
  - [ ] Indexes per AC1
- [ ] **T2 (AC: 1)** — Define `AccountSuspension` aggregate in `Faktuboh.Domain/AccountSuspensions/`
  - [ ] Implements `IBitemporal`
  - [ ] `Lift(Guid liftedBySubjectId, string reason)` instance method
  - [ ] Repository interface `IAccountSuspensionRepository`

### Backend — Wolverine handler

- [ ] **T3 (AC: 2)** — Implement body of `OnReportFlaggedForAction` (scaffolded in Story 8.4 T8)
  - [ ] Idempotency check + insert + integration-event publish + audit-log write

### Backend — Link-generation gate

- [ ] **T4 (AC: 3)** — Define `IAccountSuspensionGate` in `Faktuboh.Application/Suspensions/`
  - [ ] Method `Task<SuspensionStatus> CheckAsync(Guid ownerSubjectId, CancellationToken ct)`
  - [ ] Returns either `SuspensionStatus.Active(expiresAt)` or `SuspensionStatus.None`
- [ ] **T5 (AC: 3)** — Implement `AccountSuspensionGate` in `Faktuboh.Infrastructure/Suspensions/`
  - [ ] Executes the query from AC3
- [ ] **T6 (AC: 3)** — Wire gate into Story 4.4 link-generation handler
  - [ ] Inject `IAccountSuspensionGate`
  - [ ] Call before any link-generation work
  - [ ] Return RFC 9457 `faktuboh.admin.account_suspended` (403) on suspension-active
- [ ] **T7 (AC: 3)** — Register `faktuboh.admin.account_suspended` in `ErrorCatalog`

### Backend — Admin list endpoint

- [ ] **T8 (AC: 4)** — Create slice action `Slices/Admin/Suspensions/List/`
  - [ ] `ListSuspensionsRequest.cs`, `SuspensionListItemDto.cs`, `ListSuspensionsHandler.cs`
  - [ ] Endpoint `MapGet("/v1/admin/suspensions", ...)`

### Backend — Lift endpoint

- [ ] **T9 (AC: 5)** — Create slice action `Slices/Admin/Suspensions/Lift/`
  - [ ] `LiftSuspensionRequest.cs` — `(string Reason)` (required, max 500)
  - [ ] `LiftSuspensionHandler.cs`
  - [ ] Endpoint `MapPost("/v1/admin/suspensions/{id}/lift", ...)`
  - [ ] FluentValidation: reason non-empty + length

### Backend — Integration events

- [ ] **T10 (AC: 2, 5)** — Define `AccountSuspensionCreatedEvent` + `AccountSuspensionLiftedEvent` in `Faktuboh.Domain.Events.IntegrationEvents/`

### Backend — Telemetry

- [ ] **T11 (AC: 7)** — Define `AdminSuspensionCreatedTelemetry` + `AdminSuspensionLiftedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T12 (AC: 7)** — Wire emission in handler + lift endpoint

### Frontend — Suspensions page

- [ ] **T13 (AC: 4)** — Create `apps/owner-workspace/src/app/features/admin/suspensions/`
  - [ ] `suspensions-list.component.ts` + `.html` + `.scss`
  - [ ] PrimeNG `<p-table>` with status filter + lift button per row

### Frontend — Lift dialog

- [ ] **T14 (AC: 5)** — Wire `<p-confirmDialog>` for lift action with mandatory reason textarea

### Frontend — API client

- [ ] **T15 (AC: 4, 5)** — Extend `admin.client.ts`
  - [ ] `getSuspensions(status?): rxResource<SuspensionsListResponse>`
  - [ ] `liftSuspension(id, reason): Observable<void>`

### Frontend — Owner-side suspended-account banner

- [ ] **T16 (AC: 3)** — Owner-workspace error handler interprets `faktuboh.admin.account_suspended` 403
  - [ ] Shows a non-blocking banner at top of contacts list: "Statement link generation is temporarily suspended. New links will be available [date]."
  - [ ] All other functionality continues
  - [ ] Banner uses `<p-message severity="warn">` with `aria-live="polite"`
  - [ ] Bilingual via Transloco

### Tests — Backend

- [ ] **T17 (AC: 1)** — Integration test for table + bitemporal history
- [ ] **T18 (AC: 2)** — Integration test for `OnReportFlaggedForAction` handler
  - [ ] Receives event → row inserted
  - [ ] Receives duplicate event for same owner → idempotent (no second row)
  - [ ] Auto-flagged event → `suspended_by_subject_id = NULL`
- [ ] **T19 (AC: 3)** — Integration test for link-generation gate
  - [ ] No suspension → link generation succeeds
  - [ ] Active suspension → 403 `faktuboh.admin.account_suspended` with `expiresAt`
  - [ ] Lifted suspension → succeeds
  - [ ] Expired suspension → succeeds
- [ ] **T20 (AC: 4)** — Integration test for list endpoint with filter
- [ ] **T21 (AC: 5)** — Integration test for lift endpoint
  - [ ] Lift updates row + publishes event + writes audit
  - [ ] Reason required (empty → 400)
  - [ ] Lift then retry link generation → succeeds
- [ ] **T22 (AC: 8)** — Integration test for suspension scope
  - [ ] Suspended owner can still: GET contacts, GET debts, POST/GET CSV exports
  - [ ] Recipients can still view existing shared statements
  - [ ] Only POST link generation is blocked

### Tests — Frontend

- [ ] **T23 (AC: 4, 5)** — Vitest tests for components
- [ ] **T24 (AC: 3)** — Vitest test for owner-side banner on 403 response
- [ ] **T25 (AC: 4, 5)** — Playwright E2E for admin suspension management flow

## Dev Notes

### Critical guardrails (do not violate)

1. **Suspension scope is link-generation ONLY.** Per FR47 + UX-DR philosophy: paywall gates creation, not access. Suspended users can still view, export, and recipients can still view existing shared content. Test AC8 enforces this.
2. **Lift requires a reason.** Without it, the audit trail loses context. Reason is mandatory + length-capped (500 chars).
3. **Auto-expiration via query, not job.** The gate query excludes expired suspensions naturally. No scheduled job needed at MVP.
4. **Idempotent handler.** Wolverine at-least-once delivery requires the handler to skip duplicate-event-for-same-owner. The active-suspension check is the dedup key.
5. **Owner-side banner is informational + non-blocking.** Per UX-DR: don't yell at users about server-side state. The banner explains what's happening + when it ends. Other paths still work.
6. **Bitemporal history.** Lift writes to `account_suspensions_history` via the `IBitemporal` interceptor — audit trail of every state change.
7. **`<p-confirmDialog>` for lift action** — destructive in the sense that it removes the safety guardrail from the suspended account. Per UX-DR29.
8. **The 30-day default expiration is a starting point.** Admin can override at lift-time (future enhancement; not in this MVP — admin can only lift, not extend; document as Phase-2).

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/Admin/Suspensions/
│   │       ├── List/
│   │       │   ├── ListSuspensionsRequest.cs                   # NEW
│   │       │   ├── SuspensionListItemDto.cs                    # NEW
│   │       │   └── ListSuspensionsHandler.cs                   # NEW
│   │       ├── Lift/
│   │       │   ├── LiftSuspensionRequest.cs                    # NEW
│   │       │   └── LiftSuspensionHandler.cs                    # NEW
│   │       └── Projections/
│   │           └── OnReportFlaggedForAction.cs                 # MODIFY (body added by this story)
│   ├── Faktuboh.Domain/
│   │   ├── AccountSuspensions/
│   │   │   ├── AccountSuspension.cs                            # NEW
│   │   │   └── IAccountSuspensionRepository.cs                 # NEW
│   │   └── Events/IntegrationEvents/
│   │       ├── AccountSuspensionCreatedEvent.cs                # NEW
│   │       └── AccountSuspensionLiftedEvent.cs                 # NEW
│   ├── Faktuboh.Application/
│   │   ├── Suspensions/
│   │   │   ├── IAccountSuspensionGate.cs                       # NEW
│   │   │   └── SuspensionStatus.cs                             # NEW
│   │   ├── Telemetry/Events/
│   │   │   ├── AdminSuspensionCreatedTelemetry.cs              # NEW
│   │   │   └── AdminSuspensionLiftedTelemetry.cs               # NEW
│   │   └── Errors/ErrorCatalog.cs                              # MODIFY (+1 code)
│   └── Faktuboh.Infrastructure/
│       ├── Suspensions/
│       │   └── AccountSuspensionGate.cs                        # NEW
│       ├── Persistence/
│       │   ├── Configurations/AccountSuspensionConfiguration.cs # NEW
│       │   └── Migrations/YYYYMMDDHHMMSS_AddAccountSuspensionsTable.cs  # NEW
│       └── Repositories/AccountSuspensionRepository.cs         # NEW
├── tests/Faktuboh.Api.Tests/Slices/Admin/Suspensions/
│   ├── ListSuspensionsTests.cs                                 # NEW
│   ├── LiftSuspensionTests.cs                                  # NEW
│   ├── OnReportFlaggedForActionTests.cs                        # NEW
│   └── AccountSuspensionGateTests.cs                           # NEW
└── (modify Story 4.4 link-generation handler to inject gate)

frontend/
├── apps/owner-workspace/src/app/features/admin/suspensions/
│   ├── suspensions-list.component.ts                           # NEW
│   ├── suspensions-list.component.html                         # NEW
│   └── suspensions-list.component.spec.ts                      # NEW
└── apps/owner-workspace/src/app/core/
    ├── api/admin.client.ts                                     # EDIT
    └── error-handling/suspended-account-banner.component.ts    # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + NSubstitute |
| Integration (BE) | Testcontainers Postgres + bitemporal interceptor real-run + Wolverine in-process |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story extends `Slices/Admin/` with the `Suspensions/` action group. The suspension gate (`IAccountSuspensionGate`) is injected into Story 4.4's link-generation handler — this is the cross-slice coupling pattern: `Faktuboh.Application/Suspensions/` defines the abstraction, the Statements slice consumes it via DI without importing `AccountSuspension` aggregate types. NetArchTest enforces no domain-type leakage across the boundary.

### Architecture compliance

- [epics.md Epic 8 Story 8.5](../planning-artifacts/epics.md)
- [Story 4.4 link generation](./4-4-generate-statement-html-shareable-link.md) — gate consumer
- [Story 4.10 token generation](./4-10-token-cryptographic-generation-with-property-tests.md) — gate also applies
- [Story 8.1 admin auth pattern](./8-1-admin-dashboard-with-platform-metrics.md)
- [Story 8.4 publishes ReportFlaggedForActionEvent](./8-4-admin-investigation-and-flagging.md)
- [ADR-022 bitemporal](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [prd.md FR47, NFR-S10](../planning-artifacts/prd.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | EF Core 10 + bitemporal interceptor | Per ADR-022 |
| Frontend | PrimeNG (Table, ConfirmDialog, Message) | Standard |

### Testing requirements

- **Suspension-scope test (AC8)** — without it, the suspension could regress to over-block (e.g., blocking GET endpoints). Critical for FR47 compliance.
- **Idempotent-handler test** — Wolverine retries are silent; without the dedup test, accounts could accumulate multiple suspension rows.
- **Auto-expiration test** — without it, an expired suspension could remain enforced if the gate query is wrong.
- **Owner-side banner test** — confirms the 403 response renders sensibly (not a generic error).

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 4.4 — link-generation endpoint (gate consumer)
- Story 4.10 — token generation (also gated)
- Story 8.1 — admin slice + auth + audit
- Story 8.4 — `ReportFlaggedForActionEvent` publisher
- Story 3.8 — audit log
- Story 0.14 — telemetry

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.5
- [_bmad-output/planning-artifacts/prd.md FR47, NFR-S10](../planning-artifacts/prd.md)
- [Story 4.4](./4-4-generate-statement-html-shareable-link.md)
- [Story 8.4](./8-4-admin-investigation-and-flagging.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
