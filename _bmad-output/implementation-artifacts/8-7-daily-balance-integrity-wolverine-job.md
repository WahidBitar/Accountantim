# Story 8.7: Daily balance-integrity Wolverine job (AR-051 / NFR-R6)

Status: ready-for-dev

## Story

As **Wahid**,
I want **a daily scheduled Wolverine job that checks `sum(transactions) == stored_balance` for every contact-denomination pair and alerts within 1 hour on mismatch**,
so that **silent ledger divergence is detected fast** (NFR-R6, AR-051) [Source: epics.md Epic 8 Story 8.7; prd.md NFR-R6].

**Covers requirements:** Epic 8 — AR-051 daily balance-integrity job, NFR-R6 (1h alert window on mismatch), reuse of Story 0.14 telemetry primitive + Story 0.11 App Insights alerts [Source: epics.md Story 8.7].

**Why this story now:** Stories 8.1, 8.4, 8.5 establish the admin surface. This story adds the operational-safety background job — the first Wolverine scheduled job in the codebase. It establishes the scheduled-job pattern that Stories 8.4's auto-flag job (just shipped) extends.

## Acceptance Criteria

### AC1 — Wolverine scheduled job at 03:00 UTC daily

**Given** Wolverine supports cron-scheduled messages
**When** the application starts
**Then** the job is registered in `Program.cs`:
```csharp
opts.Schedule<RunBalanceIntegrityCheckCommand>().Cron("0 3 * * *");  // daily at 03:00 UTC
```
**And** 03:00 UTC is chosen because it's low-traffic (≈ 06:00 in Cairo, 06:00 in Riyadh, 03:00 in London/Frankfurt — quiet hours across all primary geographies)
**And** the message handler `RunBalanceIntegrityCheckHandler` consumes the message + executes the integrity check
**And** an integration test (using Wolverine's test mode) triggers the message manually + asserts handler executes [Source: epics.md Story 8.7 AC; Wolverine scheduled-jobs documentation].

### AC2 — Per-pair balance computation

**Given** the integrity check
**When** the job runs
**Then** for every distinct `(contact_id, denomination)` pair across all owners:
1. Compute `sum_actual = SUM(amount * direction_signed)` from `journal_entries` where `direction_signed = +1` for receivable, `-1` for payable
2. Read `sum_stored` from the per-contact balance read model (from Story 2.4)
3. Compare: `discrepancy = abs(sum_actual - sum_stored)`
4. Tolerance check: `discrepancy > smallest_unit(denomination)`
- USD: `0.01`
- EUR/GBP/AED/SAR/EGP/JOD/KWD/BHD/TND: minor-unit per `CurrencyRegistry`
- Gold: `0.001` grams
- Silver: `0.001` grams
**And** mismatches are inserted into a new `balance_integrity_alerts` table per AC3
**And** matches are NOT logged (silent on success — only divergence is news) [Source: epics.md Story 8.7 AC; prd.md NFR-R6].

### AC3 — `balance_integrity_alerts` table

**Given** alerts must persist for admin review
**When** migration `YYYYMMDDHHMMSS_AddBalanceIntegrityAlertsTable.cs` lands
**Then** the table:
```sql
CREATE TABLE balance_integrity_alerts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_subject_id uuid NOT NULL,
    contact_id uuid NOT NULL,
    denomination text NOT NULL,
    sum_actual numeric(28, 8) NOT NULL,
    sum_stored numeric(28, 8) NOT NULL,
    discrepancy numeric(28, 8) NOT NULL,
    detected_at timestamptz NOT NULL DEFAULT now(),
    resolved_at timestamptz NULL,
    resolved_by_subject_id uuid NULL,
    resolution_notes text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_balance_integrity_alerts_unresolved
    ON balance_integrity_alerts(detected_at DESC) WHERE resolved_at IS NULL;
CREATE UNIQUE INDEX ux_balance_integrity_alerts_unresolved_per_pair
    ON balance_integrity_alerts(owner_subject_id, contact_id, denomination)
    WHERE resolved_at IS NULL;
```
**And** the unique partial index ensures: at most ONE unresolved alert per `(owner, contact, denomination)` triple — re-detection on subsequent days is a no-op until previous is resolved
**And** uses `numeric(28, 8)` for amounts (FX-pivot precision per AR-035 since this is internal accounting metadata, not user-facing) [Source: epics.md Story 8.7; architecture.md AR-035].

### AC4 — App Insights alert fires within 1 hour per NFR-R6

**Given** mismatches must trigger an alert to Wahid's email within 1 hour of detection
**When** a row is inserted into `balance_integrity_alerts`
**Then** an App Insights custom event `balance_integrity_mismatch_detected` emits with payload:
- `opaque_owner_id`
- `contact_id_hash`
- `denomination`
- `discrepancy` (decimal, capped to 2 decimal places for display in the alert email)
- `detected_at`
**And** an App Insights alert rule `alert-balance-integrity` is configured in `infra/azure/modules/alerts.bicep`:
- Trigger: `customEvents | where name == "balance_integrity_mismatch_detected" | summarize count() by bin(timestamp, 5m)` returns count > 0
- Frequency: every 5 minutes
- Action group: `ag-faktuboh-email` (Wahid)
**And** the alert email subject includes "FAKTUBOH BALANCE INTEGRITY ALERT" prefix
**And** the alert body includes the contact_id_hash (for admin correlation) but NOT the contact name (PII)
**And** alert latency from event emission to email delivery: < 1 hour per NFR-R6 (App Insights alert rules typically fire within 5-10 minutes — 1h is a generous SLA) [Source: epics.md Story 8.7 AC; prd.md NFR-R6; Story 0.11 App Insights alerts].

### AC5 — Admin dashboard `/admin/integrity` lists unresolved alerts

**Given** Wahid needs to triage detected divergences
**When** the admin navigates to `/admin/integrity`
**Then** the page renders a `<p-table>` listing all unresolved alerts with columns:
- Detected at
- Owner subject_id (opaque short)
- Contact id hash (short)
- Denomination
- Sum actual / Sum stored / Discrepancy
- Action button: Mark resolved (with notes)
**And** filter by denomination + sort by `discrepancy DESC` (largest first)
**And** the data source is `GET /v1/admin/integrity/alerts`
**And** all admin auth + audit + minimum-k inheritance from Story 8.1 applies [Source: epics.md Story 8.7 AC admin dashboard].

### AC6 — "Mark resolved" action

**Given** the admin investigates + applies a fix
**When** the admin taps "Mark resolved"
**Then** `<p-confirmDialog>` opens requiring resolution notes (max 1000 chars, required)
**And** confirm triggers `POST /v1/admin/integrity/alerts/{id}/resolve` with `{ notes: string }`
**And** the row updates `resolved_at = now()`, `resolved_by_subject_id = admin's id`, `resolution_notes = body.notes`
**And** an audit-log entry is written
**And** `BalanceIntegrityAlertResolvedEvent` integration event publishes (for downstream observability) [Source: epics.md Story 8.7].

### AC7 — Runbook `docs/runbooks/balance-integrity-alert.md`

**Given** Wahid receives an alert email
**When** the runbook is followed
**Then** `docs/runbooks/balance-integrity-alert.md` documents:

```markdown
# Balance Integrity Alert — Triage Runbook

## Trigger

Email subject: "FAKTUBOH BALANCE INTEGRITY ALERT — [denomination] discrepancy"

Source: App Insights alert `alert-balance-integrity` firing on `balance_integrity_mismatch_detected` event.

## Triage steps

### Step 1: Pull alert detail
Navigate to `/admin/integrity`, find the alert row. Note the discrepancy magnitude.

### Step 2: Categorize by magnitude
- **Discrepancy < 0.10 [denomination]**: Likely a rounding accumulation bug. Investigate Money VO + bitemporal history for the (contact, denomination) pair.
- **Discrepancy 0.10–10.00**: Likely a missed handler or out-of-order event processing. Inspect Wolverine DLQ + recent integration-event log.
- **Discrepancy > 10.00**: Potential data corruption or migration-applied transformation error. Stop further writes for the affected (owner, contact) pair pending investigation.

### Step 3: Pull bitemporal history
Run:
```sql
SELECT * FROM journal_entries_history
WHERE contact_id = :contactId
  AND denomination = :denomination
ORDER BY recorded_at DESC
LIMIT 50;
```
Look for: out-of-order writes, NULL handler updates, divergent _As_Of computations.

### Step 4: Recompute + reconcile
If bitemporal history is consistent: the read-model `per_contact_balances` row is wrong. Manually replay:
```sql
UPDATE per_contact_balances
SET balance = (SELECT SUM(amount * direction_signed) FROM journal_entries WHERE contact_id = :contactId AND denomination = :denomination)
WHERE contact_id = :contactId AND denomination = :denomination;
```
Verify the alert resolves on next job run (next 03:00 UTC).

### Step 5: Mark resolved
In `/admin/integrity`, mark resolved with notes describing root cause.

### Step 6: Post-incident
- Open a GitHub issue for any underlying bug discovered.
- Add a regression test for the specific scenario.
- Update this runbook with new findings if novel.

## Escalation

If discrepancy > 100 [denomination] OR pattern across multiple (contact, denomination) pairs same day → consider taking the system into emergency-shutdown mode (Story 0.17 `FAKTUBOH_EMERGENCY_SHUTDOWN=true`) until investigation completes.
```

**And** the runbook is committed to the repo + linked from the alert email body [Source: epics.md Story 8.7 AC runbook].

### AC8 — Synthetic-divergence integration test

**Given** the alert path must be verifiable end-to-end
**When** the integration test `BalanceIntegrityJobTests.SyntheticDivergence` runs
**Then** the test:
1. Seeds a contact + 3 debts totaling 100.00 USD (sum_actual = 100.00)
2. Manually corrupts the read-model balance: `UPDATE per_contact_balances SET balance = 99.50 WHERE contact_id = X AND denomination = 'USD'` (discrepancy = 0.50)
3. Triggers `RunBalanceIntegrityCheckHandler` directly (not via cron)
4. Asserts:
   - A row inserted into `balance_integrity_alerts` with `discrepancy = 0.50`
   - The custom event `balance_integrity_mismatch_detected` is captured by App Insights test sink
   - No alert row inserted for any other (contact, denomination) pair
5. Cleanup: the test resets the corrupted row + deletes the alert row to leave a clean state
**And** a second variant `SyntheticDivergence_BelowTolerance` corrupts by 0.005 (below USD's 0.01 tolerance) and asserts NO alert is created
**And** a third variant `SyntheticDivergence_ExistingAlert` ensures duplicate-detection on the same pair the next day is a no-op (the unique partial index from AC3) [Source: epics.md Story 8.7 AC synthetic-divergence test].

### AC9 — Job execution telemetry

**Given** AR-047 telemetry primitive
**When** the job completes
**Then** `TelemetryEmitter.Emit("balance_integrity_job_completed", new BalanceIntegrityJobCompletedTelemetry(pairs_checked, mismatches_found, duration_ms_bucket))` fires
**And** `duration_ms_bucket` is one of `"<10s" | "<60s" | "<5m" | ">5m"` — operations visibility into job runtime
**And** if the job throws, `balance_integrity_job_failed` emits with `error_kind` (no exception message — PII risk) [Source: Story 0.14].

### AC10 — Job is killable / pause-able for emergencies

**Given** Wahid may need to disable the job during an investigation
**When** the env var `FAKTUBOH_BALANCE_INTEGRITY_JOB_DISABLED=true` is set
**Then** the handler `RunBalanceIntegrityCheckHandler` checks the env var as the FIRST step
**And** if disabled, it logs INFO + emits `balance_integrity_job_skipped_disabled` telemetry + returns immediately
**And** an integration test asserts the disabled path skips all DB queries [Source: design decision aligned with Story 0.17 emergency-shutdown pattern].

## Tasks / Subtasks

### Backend — Migration + table

- [ ] **T1 (AC: 3)** — Create migration `YYYYMMDDHHMMSS_AddBalanceIntegrityAlertsTable.cs` per AC3
- [ ] **T2 (AC: 3)** — Define `BalanceIntegrityAlert` entity in `Faktuboh.Domain/BalanceIntegrity/`
  - [ ] Plain entity (NOT `IBitemporal` — alerts are operational metadata, immutable except for resolution)
  - [ ] `Resolve(Guid resolvedBy, string notes)` method
  - [ ] Repository interface

### Backend — Wolverine scheduled job

- [ ] **T3 (AC: 1, 2)** — Create command + handler in `Slices/Operations/BalanceIntegrity/`
  - [ ] `RunBalanceIntegrityCheckCommand.cs` — empty record (just a trigger)
  - [ ] `RunBalanceIntegrityCheckHandler.cs` — executes the integrity check
- [ ] **T4 (AC: 1)** — Register schedule in `Program.cs`
  - [ ] `opts.Schedule<RunBalanceIntegrityCheckCommand>().Cron("0 3 * * *")`

### Backend — Integrity check logic

- [ ] **T5 (AC: 2)** — Implement check in handler
  - [ ] Iterate `(contact_id, denomination)` pairs via SQL (avoid N+1 — batch query)
  - [ ] For each, compute `sum_actual` + read `sum_stored` + compute discrepancy
  - [ ] Lookup tolerance from `CurrencyRegistry.GetSmallestUnit(denomination)`
  - [ ] Insert into `balance_integrity_alerts` only on `discrepancy > tolerance`
  - [ ] Skip insert if unique partial index would conflict (existing unresolved alert) — `INSERT ... ON CONFLICT DO NOTHING`

### Backend — Disabled-flag check

- [ ] **T6 (AC: 10)** — Implement env-var check at handler start
  - [ ] If `FAKTUBOH_BALANCE_INTEGRITY_JOB_DISABLED=true`, log + emit telemetry + return
  - [ ] Document the env var in `docs/runbooks/balance-integrity-alert.md`

### Backend — Telemetry

- [ ] **T7 (AC: 4, 9, 10)** — Define telemetry events:
  - [ ] `BalanceIntegrityMismatchDetectedTelemetry` (per detected row)
  - [ ] `BalanceIntegrityJobCompletedTelemetry`
  - [ ] `BalanceIntegrityJobFailedTelemetry`
  - [ ] `BalanceIntegrityJobSkippedDisabledTelemetry`
- [ ] **T8 (AC: 4, 9)** — Wire emission in handler

### Backend — App Insights alert rule

- [ ] **T9 (AC: 4)** — Add Bicep alert rule `alert-balance-integrity` to `infra/azure/modules/alerts.bicep`
  - [ ] Query: `customEvents | where name == "balance_integrity_mismatch_detected" | summarize count() by bin(timestamp, 5m)`
  - [ ] Threshold: count > 0
  - [ ] Frequency: every 5 minutes
  - [ ] Action group: `ag-faktuboh-email`
  - [ ] Email subject prefix: "FAKTUBOH BALANCE INTEGRITY ALERT"

### Backend — Admin endpoints

- [ ] **T10 (AC: 5)** — Create slice action `Slices/Admin/Integrity/List/`
  - [ ] `ListAlertsHandler.cs`
  - [ ] Endpoint `MapGet("/v1/admin/integrity/alerts", ...)`
- [ ] **T11 (AC: 6)** — Create slice action `Slices/Admin/Integrity/Resolve/`
  - [ ] `ResolveAlertRequest.cs` — `(string Notes)` (required, max 1000)
  - [ ] `ResolveAlertHandler.cs`
  - [ ] Endpoint `MapPost("/v1/admin/integrity/alerts/{id}/resolve", ...)`
- [ ] **T12 (AC: 6)** — Define `BalanceIntegrityAlertResolvedEvent` integration event

### Backend — Currency tolerance lookup

- [ ] **T13 (AC: 2)** — Extend `CurrencyRegistry` with `GetSmallestUnit(string currency): decimal`
  - [ ] Returns `0.01` for fiat 2-decimal currencies, `0.001` for metals, etc.
  - [ ] Centralized so the integrity check doesn't hardcode tolerances

### Frontend — Admin integrity page

- [ ] **T14 (AC: 5, 6)** — Create `apps/owner-workspace/src/app/features/admin/integrity/`
  - [ ] `integrity-alerts.component.ts` + `.html` + `.scss`
  - [ ] PrimeNG `<p-table>` with sort + filter + resolve action
- [ ] **T15 (AC: 6)** — Wire `<p-confirmDialog>` for resolve action with mandatory notes textarea

### Frontend — API client

- [ ] **T16 (AC: 5, 6)** — Extend `admin.client.ts`
  - [ ] `getIntegrityAlerts(): rxResource<IntegrityAlertsResponse>`
  - [ ] `resolveIntegrityAlert(id, notes): Observable<void>`

### Runbook

- [ ] **T17 (AC: 7)** — Create `docs/runbooks/balance-integrity-alert.md` per AC7

### Tests — Backend

- [ ] **T18 (AC: 8)** — Synthetic-divergence integration test in `tests/Faktuboh.Api.Tests/Slices/Operations/BalanceIntegrity/BalanceIntegrityJobTests.cs`
  - [ ] **`SyntheticDivergence`** — corrupts read-model + asserts alert created
  - [ ] **`SyntheticDivergence_BelowTolerance`** — corrupts by 0.005 USD; asserts NO alert
  - [ ] **`SyntheticDivergence_ExistingAlert`** — runs job twice; asserts only one alert row (unique partial index)
  - [ ] **`HappyPath_NoMismatches`** — no corruption; asserts no alerts created + `balance_integrity_job_completed` telemetry fires
  - [ ] **`MultipleDenominations`** — corrupts USD AND gold for same contact; asserts 2 alert rows
  - [ ] **`DisabledFlag`** — sets env var + asserts no DB writes + `balance_integrity_job_skipped_disabled` telemetry
- [ ] **T19 (AC: 1)** — Wolverine scheduled-job registration test
  - [ ] Asserts `RunBalanceIntegrityCheckCommand` is registered with cron `0 3 * * *`
- [ ] **T20 (AC: 5, 6)** — Integration tests for admin endpoints
  - [ ] List unresolved alerts
  - [ ] Resolve alert + audit row written + integration event published
  - [ ] Empty notes → 400
- [ ] **T21 (AC: 4)** — App Insights telemetry assertion via `AssertEventEmitted("balance_integrity_mismatch_detected")`

### Tests — Frontend

- [ ] **T22 (AC: 5, 6)** — Vitest tests for integrity-alerts component
- [ ] **T23 (AC: 5, 6)** — Playwright E2E for admin triage flow

## Dev Notes

### Critical guardrails (do not violate)

1. **Synthetic-divergence test is the gate.** Without it, the alert path could regress silently. Manually corrupting a row + verifying alert fires end-to-end is the only way to know the wiring works.
2. **Tolerance per denomination — pinned in `CurrencyRegistry`.** Hardcoding `0.01` everywhere is a fintech bug — gold's `0.001` granularity matters. Use the central lookup.
3. **Unique partial index on `(owner, contact, denomination) WHERE resolved_at IS NULL`.** Prevents duplicate alerts for the same persistent divergence — without it, daily runs flood the queue.
4. **App Insights alert latency 1h is generous.** Reality is usually < 10 min. The 1h SLA accommodates Azure alert-rule processing variability.
5. **No PII in alert email or telemetry.** The alert email contains `contact_id_hash` not contact name. The admin correlates via the dashboard.
6. **Disabled flag via env var.** During an active investigation, Wahid can disable the job without a deploy (`az containerapp update --set-env-vars ...`). Mirrors Story 0.17 emergency-shutdown pattern.
7. **Job runs at 03:00 UTC.** Quiet hours across primary geographies. If usage geography shifts post-launch, revisit the schedule.
8. **Resolution notes required.** Without forced-notes, the audit trail loses context for post-incident review.
9. **Bitemporal history of `journal_entries` is the authoritative source.** The integrity check trusts `journal_entries` over `per_contact_balances` (the read model) — divergence implies the read-model rebuild is needed, not the source.
10. **Internal precision uses `numeric(28, 8)`.** AR-035 dual-precision rule — internal accounting uses higher precision; user-facing display stays at `numeric(19, 4)`.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/
│   │       ├── Operations/BalanceIntegrity/
│   │       │   ├── RunBalanceIntegrityCheckCommand.cs          # NEW
│   │       │   └── RunBalanceIntegrityCheckHandler.cs          # NEW
│   │       └── Admin/Integrity/
│   │           ├── List/
│   │           │   └── ListAlertsHandler.cs                    # NEW
│   │           └── Resolve/
│   │               ├── ResolveAlertRequest.cs                  # NEW
│   │               └── ResolveAlertHandler.cs                  # NEW
│   ├── Faktuboh.Domain/
│   │   ├── BalanceIntegrity/
│   │   │   ├── BalanceIntegrityAlert.cs                        # NEW
│   │   │   └── IBalanceIntegrityAlertRepository.cs             # NEW
│   │   ├── Events/IntegrationEvents/
│   │   │   └── BalanceIntegrityAlertResolvedEvent.cs           # NEW
│   │   └── Primitives/CurrencyRegistry.cs                      # MODIFY (+GetSmallestUnit)
│   ├── Faktuboh.Application/Telemetry/Events/
│   │   ├── BalanceIntegrityMismatchDetectedTelemetry.cs        # NEW
│   │   ├── BalanceIntegrityJobCompletedTelemetry.cs            # NEW
│   │   ├── BalanceIntegrityJobFailedTelemetry.cs               # NEW
│   │   └── BalanceIntegrityJobSkippedDisabledTelemetry.cs      # NEW
│   └── Faktuboh.Infrastructure/
│       ├── Persistence/
│       │   ├── Configurations/BalanceIntegrityAlertConfiguration.cs # NEW
│       │   └── Migrations/YYYYMMDDHHMMSS_AddBalanceIntegrityAlertsTable.cs # NEW
│       └── Repositories/BalanceIntegrityAlertRepository.cs     # NEW
├── tests/Faktuboh.Api.Tests/Slices/
│   ├── Operations/BalanceIntegrity/
│   │   └── BalanceIntegrityJobTests.cs                         # NEW (synthetic-divergence)
│   └── Admin/Integrity/
│       ├── ListAlertsTests.cs                                  # NEW
│       └── ResolveAlertTests.cs                                # NEW
├── infra/azure/modules/
│   └── alerts.bicep                                            # EDIT (+alert-balance-integrity)
└── docs/runbooks/
    └── balance-integrity-alert.md                              # NEW

frontend/
└── apps/owner-workspace/src/app/features/admin/integrity/
    ├── integrity-alerts.component.ts                           # NEW
    ├── integrity-alerts.component.html                         # NEW
    └── integrity-alerts.component.spec.ts                      # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + FluentAssertions (CurrencyRegistry tolerance lookup) |
| Integration (BE) | Testcontainers Postgres + Wolverine in-process (manual command trigger, NOT cron-driven) |
| Unit (FE) | Vitest |
| E2E | Playwright admin triage flow |

### Project Structure Notes

This story introduces `Slices/Operations/` as a new top-level slice category for system-initiated, scheduled-job-driven operations. The Operations slice has no HTTP endpoints (jobs are message-handled) but is still a vertical slice per architecture §5.3.2 — owns its domain types, telemetry events, and tests.

The admin-facing surfaces (`Admin/Integrity/`) live under the existing `Slices/Admin/` slice from Story 8.1, sharing the auth + audit infrastructure.

### Architecture compliance

- [epics.md Epic 8 Story 8.7](../planning-artifacts/epics.md)
- [architecture.md AR-035, AR-051](../planning-artifacts/architecture.md) — money precision + integrity job
- [architecture.md §3.5](../planning-artifacts/architecture.md) — bitemporal as source-of-truth
- [Story 0.11 App Insights alerts](./0-11-app-insights-otel-traceid-correlation.md)
- [Story 0.14 telemetry](./0-14-phase-a-telemetry-primitive.md)
- [Story 0.17 emergency-shutdown env-var pattern](./0-17-cost-circuit-breaker-baseline.md)
- [Story 2.4 per-contact balance read model](./2-4-list-contacts-with-per-denomination-balance-summary.md)
- [Story 8.1 admin slice](./8-1-admin-dashboard-with-platform-metrics.md)
- [prd.md NFR-R6](../planning-artifacts/prd.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Wolverine scheduled jobs | `opts.Schedule<T>().Cron()` |
| Backend | Npgsql | `numeric(28, 8)` columns |
| Frontend | PrimeNG (Table, ConfirmDialog, Textarea) | Standard |

### Testing requirements

- **Synthetic-divergence test (AC8) is the linchpin.** Six variants — happy path, divergence-detected, below-tolerance, existing-alert dedup, multi-denomination, disabled-flag. Without all six, the integrity job ships with unverified branches.
- **Integration test seeds the read-model corruption directly via SQL** to simulate a real divergence; this exercises the entire alert path end-to-end including the App Insights event sink.
- **Admin endpoint tests** verify the resolution flow; without them, the "Mark resolved" path could regress.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 0.11 — App Insights + alert rule patterns
- Story 0.14 — telemetry primitive
- Story 0.17 — env-var disable pattern
- Story 2.4 — per-contact balance read model (the "stored" side of the comparison)
- Story 3.1 — record debt (the "actual" side via journal_entries)
- Story 8.1 — admin slice

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.7
- [_bmad-output/planning-artifacts/architecture.md AR-051, AR-035](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/prd.md NFR-R6](../planning-artifacts/prd.md)
- [Story 0.11](./0-11-app-insights-otel-traceid-correlation.md)
- [Story 0.14](./0-14-phase-a-telemetry-primitive.md)
- [Story 8.1](./8-1-admin-dashboard-with-platform-metrics.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
