# Story 3.2: Edit debt with audit preservation

Status: ready-for-dev

## Story

As a **user who needs to correct a recorded debt**,
I want to **edit a debt's amount, date, denomination, or story via `PATCH /v1/journal-entries/{id}` with every change tracked in the audit trail**,
so that my **records stay accurate and disputes can be resolved by history** (FR14), the bitemporal interceptor writes pre-mutation state to `journal_entries_history`, and the audit log captures `{ actor, action, before/after, source_ip, ProcessingActivity }` per FR49.

**Covers requirements:** FR14 (edit debt with audit), FR49 (audit immutability), UX-DR32 (error recovery with Reload action).

## Acceptance Criteria

### AC1 — PATCH persists + writes bitemporal history

**Given** an existing debt owned by the authenticated user (via contact → owner chain)
**When** the user submits `PATCH /v1/journal-entries/{id}` with updated fields + `Idempotency-Key` + `If-Match` etag
**Then** the `journal_entries` row updates in a single transaction
**And** the bitemporal interceptor writes a `journal_entries_history` row with pre-update state (`valid_to` stamped to mutation wall-clock)
**And** `JournalEntryUpdated` integration event emits via Wolverine same-transaction outbox with payload `{ journalEntryId, contactId, fieldsChanged: string[], amountDelta?: Money[] }` — the Glance projection uses `amountDelta` to update sums
**And** the response is `200 OK` with `UpdateJournalEntryResponse` containing current state + server-computed balance deltas
**And** end-to-end response < 500ms; updated balance surfaces in the Glance within 500ms per NFR-P4

### AC2 — Full audit log entry

**Given** the audit-log primitive from Story 0.6
**When** a PATCH commits
**Then** a row lands in `audit_log` with `{ id, actor_subject_id, action: "debt.updated", entity_type: "journal_entry", entity_id, before: <json snapshot of pre-mutation state>, after: <json snapshot of post-mutation state>, source_ip, processing_activity, recorded_at }`
**And** the row is append-only (Story 0.6 DB trigger blocks UPDATE/DELETE)
**And** the before/after JSON snapshots are stored in full — not diffs — so the audit viewer (Story 3.8) can reconstruct without joining history tables
**And** if only the `note` field changed, the audit row still records full before/after state

### AC3 — Concurrent-edit conflict

**Given** a second user session (or second tab) updated the same debt after the user's form loaded
**When** the user submits PATCH with a stale `If-Match` etag
**Then** response is `409 Conflict` RFC 9457 with code `faktuboh.journal-entries.concurrent_edit_conflict` + `actions: [{id:"reload", label:"Reload", preserveDraft: true}]` per UX-DR32
**And** the frontend renders the inline Reload button that refetches + merges the user's draft (draft wins on touched fields)

### AC4 — RFC 9457 on validation failure

**Given** invalid patch input (amount ≤ 0, unsupported denomination, invalid date in future >1 day, note >500 chars)
**When** FluentValidation runs
**Then** response is 4xx RFC 9457 with JSON Pointers walked onto Signal Forms
**And** all error codes live in `ErrorCatalog`

### AC5 — Silent success; Glance re-renders

**Given** the PATCH succeeds
**When** the response arrives
**Then** the drawer/dialog closes; the row in the Glance updates in place with 120ms fade
**And** NO success toast per UX-DR27
**And** HeroTile sums re-compute via the projection handler processing `JournalEntryUpdated`

### AC6 — Telemetry

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** the PATCH commits
**Then** telemetry `debt_updated` emits with `{ opaque_user_id, journal_entry_id_hash, fields_changed: string[], had_amount_change: bool }` (field names only — no before/after values)

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 4, 6)** — Create `Slices/JournalEntries/Edit/`
  - [ ] `UpdateJournalEntryRequest.cs` — all fields nullable for PATCH semantics
  - [ ] `UpdateJournalEntryResponse.cs`
  - [ ] `UpdateJournalEntryRequestValidator.cs`
  - [ ] `UpdateJournalEntryHandler.cs` — handler with ownership check + domain `JournalEntry.Update(...)` + repository save
  - [ ] Endpoint: `group.MapPatch("/{id:guid}", ...)` with `If-Match` etag binding
- [ ] **T2 (AC: 1)** — Configure `RowVersion` on `JournalEntry` EF entity (`xmin` mapping); migration to expose the concurrency token
- [ ] **T3 (AC: 1)** — Update `JournalEntry.Update(decimal? amount, string? denominationCode, Direction? direction, DateOnly? occurredOn, string? note)` domain method — enforces invariants + raises `JournalEntryUpdatedEvent` with computed delta
- [ ] **T4 (AC: 2)** — Audit-log writer called in the handler pipeline: before SaveChangesAsync, build `AuditLogEntry` with before/after snapshots serialized via `System.Text.Json`; writer uses the Story 0.6 append-only writer primitive
- [ ] **T5 (AC: 3)** — `JournalEntriesExceptionHandler` maps `DbUpdateConcurrencyException` → RFC 9457 409 with `actions: [{id:"reload"}]`
- [ ] **T6 (AC: 4)** — Register new codes: `concurrent_edit_conflict`, `occurred_on_invalid` in `ErrorCatalog`
- [ ] **T7 (AC: 1)** — Glance projection handler extended to process `JournalEntryUpdated` event (Phase-A: log-only; no secondary table)

### Frontend

- [ ] **T8 (AC: 1, 3, 5)** — Create `apps/owner-workspace/src/app/features/debts/edit/edit-debt.component.ts`
  - [ ] Signal Form loads via `rxResource` (`GET /v1/journal-entries/{id}`)
  - [ ] Etag captured from response header → attached on PATCH as `If-Match`
  - [ ] On 409: inline `<p-message severity="warn">` with Reload button that refetches + merges draft per UX-DR32
  - [ ] Silent success: row updates in place; drawer closes; no toast
- [ ] **T9 (AC: 1)** — Route `/dashboard/debts/:id/edit` (drawer/dialog overlay, same responsive pattern as Story 3.1)

### Tests

- [ ] **T10 (AC: 1, 2, 4)** — `UpdateJournalEntryHandlerTests` — happy + negatives; audit row assertion
- [ ] **T11 (AC: 1-3)** — `UpdateJournalEntryEndpointTests` (Integration)
  - [ ] Happy: PATCH returns 200; `journal_entries_history` has pre-update row; `audit_log` row with full before/after snapshots
  - [ ] Concurrent PATCH with stale etag → 409 with actions array
  - [ ] Event consumption: `JournalEntryUpdated` delivered with correct delta
  - [ ] Idempotency: double-submit returns cached response
- [ ] **T12 (AC: 3, 5)** — Vitest: draft merging on 409 reload; silent success
- [ ] **T13 (AC: 1, 5)** — Playwright E2E: edit flow mobile + desktop; axe a11y

## Dev Notes

### Critical guardrails

1. **Audit log is append-only.** The Story 0.6 DB trigger blocks UPDATE/DELETE on `audit_log`. Never attempt to modify an audit row in this handler.
2. **Full before/after snapshots**, not diffs. Storage cost is negligible at MVP scale; the Story 3.8 viewer depends on snapshot completeness.
3. **Ownership check first**, validation second. An unauthorized edit attempt must not reveal validation errors about the target entity (data-existence leak). Pattern: 404 with same-timing response per AR-016.
4. **Draft merge on 409.** Never naive-overwrite the user's in-flight changes. See UX-DR32.
5. **No value-leaking telemetry.** Before/after values go only to the audit log (AR-018 encryption later). Telemetry carries field names + boolean hints.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/JournalEntries/Edit/
│   ├── UpdateJournalEntryRequest.cs                  # NEW
│   ├── UpdateJournalEntryResponse.cs                 # NEW
│   ├── UpdateJournalEntryRequestValidator.cs         # NEW
│   └── UpdateJournalEntryHandler.cs                  # NEW
├── src/Faktuboh.Domain/JournalEntries/JournalEntry.cs  # MODIFY — add Update() + raises UpdatedEvent
├── src/Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDD_AddJournalEntryRowVersion.cs         # NEW
└── tests/Faktuboh.Api.Tests/Slices/JournalEntries/Edit/
    ├── UpdateJournalEntryHandlerTests.cs             # NEW
    └── UpdateJournalEntryEndpointTests.cs            # NEW (Integration)

frontend/apps/owner-workspace/src/app/features/debts/edit/
├── edit-debt.component.ts                             # NEW
├── edit-debt.component.html                           # NEW
└── edit-debt.component.spec.ts                        # NEW
```

### Testing standards

Per Story 2.1 + Story 3.1. **Audit-row assertion is the new invariant** — every PATCH integration test MUST assert the `audit_log` row shape, not just the `journal_entries_history` row.

### Project Structure Notes

Sibling folder `Slices/JournalEntries/Edit/` alongside `Create/`. The `JournalEntry.Update()` domain method is the single place that raises `JournalEntryUpdatedEvent` — no direct event publishing from the handler.

### Architecture compliance

- **ADR-022** bitemporal history on PATCH
- **ADR-024** Minimal APIs + RFC 9457
- **FR49** audit immutability (append-only trigger from Story 0.6)
- **UX-DR27** silent success; **UX-DR32** reload action with draft preservation
- **§5.2.5** Money decimal-string wire format
- **NFR-P4** 500ms balance surface time

### Library/framework requirements

Per Story 3.1.

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions + NSubstitute |
| Integration | Testcontainers Postgres + real Wolverine + audit-log shape assertion |
| Component | Vitest + Angular Testing Library (draft merge logic) |
| E2E | Playwright — edit flow mobile + desktop |

### Previous Story Intelligence

- **Story 0.6** (bitemporal + audit-log append-only primitive) — consumed here
- **Story 2.2** (edit contact) — the concurrency token + reload-action pattern established; this story mirrors it
- **Story 3.1** (record debt) — `JournalEntry` aggregate + `JournalEntriesExceptionHandler` + `JournalEntries.Endpoints.cs` all exist; this story extends them
- **Story 3.8** (audit surface) — consumes the `audit_log` rows this story writes; before/after snapshots must be complete for that reader

## References

- [epics.md Story 3.2](../planning-artifacts/epics.md)
- [architecture.md §3.5, §4 D4.9, §4 D4.11](../planning-artifacts/architecture.md)
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [prd.md FR14, FR49, NFR-P4](../planning-artifacts/prd.md)
- [ux-design-specification.md UX-DR27, UX-DR32](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
