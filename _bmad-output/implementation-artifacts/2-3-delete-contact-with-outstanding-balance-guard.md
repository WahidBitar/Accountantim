# Story 2.3: Delete contact with outstanding-balance guard

Status: ready-for-dev

## Story

As a **user retiring a relationship I no longer track**,
I want to **delete a contact with a clear warning when outstanding balances exist**,
so that I **don't accidentally lose the record of an unsettled debt** and, when I do delete, PII is tombstoned per AR-042 while historical transactions remain in the audit trail.

**Covers requirements:** FR10 (delete contact), AR-042 (PII tombstoning on deletion), FR49 (audit retention), UX-DR29 (ConfirmDialog pattern).

## Acceptance Criteria

### AC1 — Soft delete with PII tombstone

**Given** a contact with zero outstanding balances across all denominations (per-denomination balance sum = 0 for every denomination)
**When** the user confirms deletion via the `ConfirmDialog` per UX-DR29
**Then** `DELETE /v1/contacts/{id}` sets `ValidTo = now()` on the contact row (soft delete via bitemporal pattern)
**And** PII fields (name, phone, email, notes) are replaced with tombstone markers `"[redacted]"` per AR-042 in the current row
**And** `contacts_history` row preserves the pre-deletion state in encrypted form for the 7-year audit retention window
**And** `ContactDeleted` integration event emits via Wolverine
**And** the response returns `204 No Content`

### AC2 — Outstanding-balance warning state

**Given** a contact with one or more non-zero per-denomination balances
**When** the user taps "Delete"
**Then** the `ConfirmDialog` renders in warning state listing every non-zero balance as "لي 500 USD", "عليّ 10 g gold", etc., using `AmountDisplay` primary variant
**And** the dialog requires an explicit "I understand — delete anyway" checkbox before the confirm button activates
**And** tapping "Cancel" dismisses without side effects
**And** the warning copy is localized via Transloco (AR + EN)

### AC3 — Deleted contacts hidden from list; history preserved

**Given** a contact has been deleted
**When** the user loads `/contacts`
**Then** `GET /v1/contacts` excludes rows where `ValidTo IS NOT NULL`
**And** `GET /v1/contacts?includeDeleted=true` (admin/audit path — not in MVP UI) would return them
**And** historical transactions for this contact remain queryable via `AsOf(t)` for 7 years per AR-034

### AC4 — RFC 9457 on forbidden deletion

**Given** a contact referenced by a confirmed public statement (future Epic 4 invariant — stubbed for now as `IStatementRepository.HasAnyFor(contactId)` returning false)
**When** the user attempts delete
**Then** (future state) the response is `409 Conflict` with code `faktuboh.contacts.delete_blocked_by_confirmed_statement`
**And** MVP behavior: the stub always allows deletion; the check is wired but returns false; Epic 4 activates the block

### AC5 — Silent success

**Given** deletion succeeds
**When** the client receives 204
**Then** the contact row disappears from the list with 120ms fade-out
**And** **no success toast** per UX-DR27

### AC6 — ProcessingActivity + telemetry

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** deletion commits
**Then** telemetry event `contact_deleted` emits with PII-safe payload `{ opaque_user_id, contact_id_hash, had_outstanding_balance: bool }`

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 3)** — Create `Slices/Contacts/Delete/` folder
  - [ ] `DeleteContactHandler.cs` — Wolverine handler; calls `Contact.TombstoneAndSoftDelete()` domain method then repository save
  - [ ] Endpoint in `Contacts.Endpoints.cs`: `group.MapDelete("/{id:guid}", ...)`
  - [ ] `DeleteContactResponse.cs` — not strictly needed since 204, but add a sealed empty record for Verify snapshot
- [ ] **T2 (AC: 2)** — Create `Slices/Contacts/Delete/Preview/GetContactDeletionPreviewHandler.cs`
  - [ ] Endpoint `GET /v1/contacts/{id}/deletion-preview` returns `{ outstandingBalances: [{ denomination, direction, amount }] }` so the frontend dialog renders the warning
  - [ ] Query aggregates `journal_entries` for this contact grouping by denomination + direction
- [ ] **T3 (AC: 1)** — `Contact.TombstoneAndSoftDelete()` domain method replaces PII fields with `"[redacted]"` literal and sets `ValidTo`; raises `ContactDeletedEvent`
- [ ] **T4 (AC: 3)** — Update `IContactRepository.GetAllForOwnerAsync()` to filter `ValidTo IS NULL` by default; add `includeDeleted` overload
- [ ] **T5 (AC: 4)** — `IStatementRepository` interface in Shared Kernel with `Task<bool> HasAnyFor(Guid contactId)`; MVP stub returns false; Epic 4 replaces
- [ ] **T6 (AC: 6)** — Emit `contact_deleted` telemetry

### Frontend

- [ ] **T7 (AC: 1, 2, 5)** — Create `apps/owner-workspace/src/app/features/contacts/delete/delete-contact.dialog.ts`
  - [ ] Loads `GET /v1/contacts/{id}/deletion-preview` via `rxResource()`
  - [ ] If `outstandingBalances.length === 0`: simple ConfirmDialog with destructive `severity="danger"` button
  - [ ] If outstanding: warning state with list + "I understand" checkbox gating the confirm button
  - [ ] Uses PrimeNG `<p-confirmDialog>` per UX-DR29
  - [ ] Silent success: row fade-out; no toast
- [ ] **T8 (AC: 2)** — `AmountDisplay` (from `libs/domain-ui`, Epic 1 Story 1.9) consumed unchanged to render outstanding balances

### Tests

- [ ] **T9 (AC: 1, 3)** — `DeleteContactEndpointTests` (Integration)
  - [ ] Happy path: zero balances → 204; list omits contact; history row exists with encrypted pre-deletion PII
  - [ ] Tombstone assertion: current row has `name = '[redacted]'`
  - [ ] `AsOf(tDelete - 1ms)` returns original contact with real PII
- [ ] **T10 (AC: 2)** — `GetContactDeletionPreviewHandlerTests` — returns correct per-denomination aggregation
- [ ] **T11 (AC: 2, 5)** — Vitest: `delete-contact.dialog.spec.ts`
  - [ ] Warning state renders when balances > 0; confirm button disabled until checkbox
  - [ ] Zero-balance state auto-enables confirm
  - [ ] No success toast on 204
- [ ] **T12 (AC: 2)** — Playwright E2E: destructive flow in Arabic locale — axe a11y + visual regression

## Dev Notes

### Critical guardrails

1. **Soft delete only.** Physical DELETE would violate FR49 audit immutability. The domain method is `TombstoneAndSoftDelete()`, not `Delete()`.
2. **PII tombstoning** per AR-042 — current row's `name`/`phone`/`email`/`notes` replaced with literal `"[redacted]"` at delete time. History rows retain original PII in encrypted form (AR-018 encryption for PII-at-rest covers them).
3. **Balance check is server-authoritative.** Frontend shows the preview for UX confirmation but the server re-runs the aggregation in the delete transaction. A race where a debt is recorded between preview + delete is accepted (MVP trade-off) — the delete still succeeds but the audit shows the non-zero pre-deletion state.
4. **Statement block is stubbed, not absent.** The `IStatementRepository.HasAnyFor()` call is wired today; Epic 4 swaps the implementation. Architecture test asserts the call site exists.
5. **Dialog destructive styling** — `severity="danger"` on confirm button; red is RTL-correct (no flipping needed).

### Source tree — files to create or touch

```
backend/
├── src/Faktuboh.Api/Slices/Contacts/Delete/
│   ├── DeleteContactHandler.cs                # NEW
│   └── Preview/
│       ├── GetContactDeletionPreviewHandler.cs  # NEW
│       └── GetContactDeletionPreviewResponse.cs # NEW
├── src/Faktuboh.Domain/Contacts/Contact.cs    # MODIFY — add TombstoneAndSoftDelete()
├── src/Faktuboh.Domain/Statements/
│   └── IStatementRepository.cs                # NEW (stub interface, Shared-Kernel-allowed since it's a repo port)
├── src/Faktuboh.Infrastructure/Repositories/
│   └── StubStatementRepository.cs             # NEW (returns false until Epic 4)
└── src/Faktuboh.Application/Errors/ErrorCatalog.cs  # MODIFY — add delete_blocked_by_confirmed_statement

frontend/apps/owner-workspace/src/app/features/contacts/delete/
├── delete-contact.dialog.ts                    # NEW
├── delete-contact.dialog.html                  # NEW
├── delete-contact.dialog.scss                  # NEW
└── delete-contact.dialog.spec.ts               # NEW
```

### Balance-aggregation SQL fragment

```sql
SELECT
  denomination_code,
  direction,
  SUM(amount) FILTER (WHERE direction = 'receivable') AS total_receivable,
  SUM(amount) FILTER (WHERE direction = 'payable')    AS total_payable
FROM journal_entries
WHERE contact_id = @contactId AND valid_to IS NULL
GROUP BY denomination_code, direction;
```

### Testing standards

Per Story 2.1 conventions. Emphasis: **assert the tombstone replaces PII on the current row but NOT on the history row** — that's the compliance invariant.

### Project Structure Notes

Adds `Delete/` + `Delete/Preview/` subfolders. The Preview sub-action is intentionally a separate GET endpoint rather than composed into the DELETE — keeps each action's handler under 80 lines and tests isolated.

### Architecture compliance

- **AR-042** PII tombstoning on deletion
- **AR-034** 7-year retention for history rows
- **ADR-022** bitemporal soft delete via `ValidTo`
- **ADR-024** Minimal APIs DELETE + RFC 9457
- **UX-DR27** silent success
- **UX-DR29** ConfirmDialog for destructive actions
- **§5.2.3** camelCase JSON; **§5.2.4** timestamps as `timestamptz`

### Library/framework requirements

Per Story 2.1. Additional: PrimeNG `<p-confirmDialog>` + `ConfirmationService` — import via `@primeng/mcp` query for exact API.

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions |
| Integration | Testcontainers Postgres — tombstone + history-retention assertions |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — Arabic destructive flow + axe a11y |

### Previous Story Intelligence

- **Story 0.6** (`IBitemporal` interceptor) — soft delete via `ValidTo`; history row written
- **Story 0.7** (`ProcessingActivity`) — stamped on delete
- **Story 2.1** (Contacts/Create) — `Contact` aggregate + `IContactRepository`
- **Story 2.2** (Edit contact) — `Contact.Update()` method; this story adds sibling `Contact.TombstoneAndSoftDelete()`
- **Story 1.9** (Phase-1 components) — `AmountDisplay` renders outstanding balances in the warning dialog

## References

- [epics.md Story 2.3](../planning-artifacts/epics.md)
- [architecture.md §3.5, §4 D4.1](../planning-artifacts/architecture.md) bitemporal + AR-042
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [prd.md FR10, FR49](../planning-artifacts/prd.md)
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
