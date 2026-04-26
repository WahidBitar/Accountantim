# Story 3.3: Delete debt with audit preservation

Status: ready-for-dev

## Story

As a **user who recorded a debt entry in error**,
I want to **delete it via `DELETE /v1/journal-entries/{id}` with the deletion tracked in audit**,
so that **mistakes can be corrected without compromising history** (FR15), the row is soft-deleted (ValidTo stamped; history row written), the audit log captures `action: "debt.deleted"`, and the row disappears from the Glance while remaining visible in the audit surface (Story 3.8) with strikethrough.

**Covers requirements:** FR15 (delete debt with audit), FR49 (audit retention), NFR-P7 (200ms balance recalc), UX-DR29 (ConfirmDialog).

## Acceptance Criteria

### AC1 — Soft delete with audit row

**Given** an existing debt owned by the authenticated user
**When** the user confirms deletion via `<p-confirmDialog>` per UX-DR29
**Then** `DELETE /v1/journal-entries/{id}` sets `valid_to = now()` on the `journal_entries` row
**And** the bitemporal interceptor writes a `journal_entries_history` row with the pre-delete state
**And** a new audit row lands with `action: "debt.deleted"`, full `before` snapshot, `after: null`
**And** `JournalEntryDeleted` integration event emits via Wolverine
**And** response returns `204 No Content`
**And** dashboard sums recalculate in < 200ms per NFR-P7

### AC2 — Row disappears from Glance; visible in audit surface

**Given** the deletion commits
**When** the Glance re-renders (via projection handler consuming the event)
**Then** the row is absent from both DebtRow columns and the settled accordion
**And** HeroTile sums update with tabular-numeral transition
**And** the audit surface (Story 3.8) displays the deleted row with strikethrough style + "Deleted on {date}" metadata

### AC3 — ConfirmDialog destructive state

**Given** the user taps "Delete" on a debt row's kebab menu
**When** the `<p-confirmDialog>` opens
**Then** the dialog shows the debt's summary (`AmountDisplay` + contact name + date) and requires explicit "Delete" confirmation with `severity="danger"` button
**And** tapping "Cancel" dismisses without side effects
**And** dialog copy is localized (AR + EN)

### AC4 — Idempotency + RFC 9457

**Given** a double-tap on Delete (common on mobile)
**When** two `DELETE` requests carry the same `Idempotency-Key`
**Then** only one handler execution commits; the second returns the cached 204
**And** attempting to delete a nonexistent or forbidden debt returns 404 RFC 9457 with same-timing response per AR-016

### AC5 — Silent success + telemetry

**Given** the deletion succeeds
**When** the 204 arrives
**Then** the row fades out over 120ms; NO success toast per UX-DR27
**And** telemetry `debt_deleted` emits with `{ opaque_user_id, journal_entry_id_hash, days_since_creation }`

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 4)** — Create `Slices/JournalEntries/Delete/DeleteJournalEntryHandler.cs`
  - [ ] Ownership check (404 constant-time on miss)
  - [ ] `JournalEntry.SoftDelete()` domain method sets `ValidTo` + raises `JournalEntryDeletedEvent`
  - [ ] Endpoint: `group.MapDelete("/{id:guid}", ...)`
- [ ] **T2 (AC: 1)** — Audit-log writer pipeline called with `action: "debt.deleted"`, `before: <full snapshot>`, `after: null`
- [ ] **T3 (AC: 1, 2)** — Glance projection handler extended to consume `JournalEntryDeleted` — Phase-A: log + invalidate the dashboard resource on the next request
- [ ] **T4 (AC: 5)** — Emit `debt_deleted` telemetry

### Frontend

- [ ] **T5 (AC: 3, 5)** — Create `apps/owner-workspace/src/app/features/debts/delete/delete-debt.dialog.ts`
  - [ ] PrimeNG `<p-confirmDialog>` with destructive styling
  - [ ] Shows the debt summary via `AmountDisplay` + contact name + date
  - [ ] On confirm: DELETE request with Idempotency-Key; on 204, row fade-out + drawer close
  - [ ] No success toast
- [ ] **T6 (AC: 2)** — Glance row component adds a kebab menu with "Edit" + "Delete" actions → Delete routes to the confirm dialog

### Tests

- [ ] **T7 (AC: 1, 4)** — `DeleteJournalEntryHandlerTests` — happy + ownership 404 + idempotency
- [ ] **T8 (AC: 1, 2)** — `DeleteJournalEntryEndpointTests` (Integration)
  - [ ] DELETE returns 204; `journal_entries.valid_to` set; `journal_entries_history` row present; `audit_log` row with action `debt.deleted`
  - [ ] List endpoint (`GET /v1/journal-entries`) excludes deleted
  - [ ] `GET /v1/journal-entries/{id}/history` (Story 3.8's surface) returns the deleted row with deletion timestamp
  - [ ] `AsOf(tDelete - 1ms)` returns the pre-delete row
- [ ] **T9 (AC: 3, 5)** — Vitest: confirm dialog + destructive styling + silent success
- [ ] **T10 (AC: 3)** — Playwright E2E: delete flow in Arabic + English; axe a11y

## Dev Notes

### Critical guardrails

1. **Soft delete only** — physical DELETE violates FR49 audit immutability. The `SoftDelete()` domain method is the ONLY way.
2. **Audit row `before` is a full JSON snapshot** — the Story 3.8 viewer depends on it.
3. **404 constant-time** on ownership miss — AR-016 enumeration-oracle defense.
4. **Silent success** — row fade-out IS the feedback.
5. **Projection handler is log-only** at MVP — the primary store drives the Glance re-render via resource invalidation on the client.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/JournalEntries/Delete/
│   └── DeleteJournalEntryHandler.cs                  # NEW
├── src/Faktuboh.Domain/JournalEntries/JournalEntry.cs  # MODIFY — add SoftDelete() + DeletedEvent
└── tests/Faktuboh.Api.Tests/Slices/JournalEntries/Delete/
    ├── DeleteJournalEntryHandlerTests.cs              # NEW
    └── DeleteJournalEntryEndpointTests.cs             # NEW (Integration)

frontend/apps/owner-workspace/src/app/features/debts/delete/
├── delete-debt.dialog.ts                              # NEW
├── delete-debt.dialog.html                            # NEW
└── delete-debt.dialog.spec.ts                         # NEW
```

### Testing standards

Per Story 3.1/3.2. **Assertion: `AsOf(t)` returns pre-delete row** is the bitemporal round-trip test unique to this story.

### Project Structure Notes

`Slices/JournalEntries/Delete/` — thin slice; shares handler/validator/DTO patterns with Create + Edit. The `SoftDelete()` method is intentionally the only mutation in this slice (no request DTO needed beyond the route ID).

### Architecture compliance

- **ADR-022** soft delete via `ValidTo`
- **FR49** audit immutability; append-only `audit_log`
- **AR-016** constant-time response on ownership miss
- **UX-DR27** silent success; **UX-DR29** ConfirmDialog for destructive actions
- **NFR-P7** 200ms balance recalc

### Library/framework requirements

Per Story 3.1.

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions |
| Integration | Testcontainers Postgres — full audit + history + list-exclusion assertions |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — delete flow AR + EN, axe a11y |

### Previous Story Intelligence

- **Story 0.6** (bitemporal + append-only audit_log) — primitives consumed
- **Story 2.3** (delete contact with guard) — established the destructive-dialog pattern this story reuses
- **Story 3.1** (create debt) — `JournalEntry` aggregate + slice scaffold
- **Story 3.2** (edit debt) — audit-log writer pipeline established; this story reuses it with `action: "debt.deleted"`
- **Story 3.8** (audit surface) — consumes the deleted-row markers this story writes

## References

- [epics.md Story 3.3](../planning-artifacts/epics.md)
- [architecture.md §3.5, §4 D4.9](../planning-artifacts/architecture.md)
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [prd.md FR15, FR49, NFR-P7](../planning-artifacts/prd.md)
- [ux-design-specification.md UX-DR27, UX-DR29](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
