# Story 4.7: Owner sees passive last-confirmation date per contact

Status: ready-for-dev

## Story

As an **owner who shared a statement**,
I want to **see "Last reconciled: [date]" on my contact list when the recipient confirms**,
so that **I know the counterparty agrees without needing real-time notifications** (FR37).

**Covers requirements:** Epic 4 — FR37, UX-DR12 ReconciliationMeta, delegated decision "passive next-session confirmation display" (SignalR deferred to Epic 7) [Source: epics.md Epic 4 Story 4.7].

**Why this story now:** Story 4.6 records the confirmation server-side. This story surfaces it on the owner workspace — the other half of the reconciliation loop. Passive display only; real-time comes in Epic 7.

## Acceptance Criteria

### AC1 — Contact list shows `ReconciliationMeta` state per contact

**Given** an owner loads `/contacts` or `/contacts/{id}`
**When** the view renders
**Then** for each contact with ≥ 1 confirmed statement: `ReconciliationMeta` in state `reconciled` + relative date ("3 days ago") appears inline on the contact row
**And** for each contact with shared-but-unconfirmed statements (`statements.confirmed_at IS NULL AND revoked_at IS NULL AND expires_at > now()`): `ReconciliationMeta` state `pending` with text "Shared, awaiting confirmation" (tertiary color)
**And** for contacts with no shared statements: no `ReconciliationMeta` renders (UX-DR12 `never` state)
**And** hovering/focusing `ReconciliationMeta` reveals absolute date via PrimeNG Tooltip
**And** the element uses `<time datetime="ISO">` for screen readers [Source: epics.md Story 4.7; ux-design-specification.md Component 8].

### AC2 — `GET /v1/contacts` + `GET /v1/contacts/{id}` return reconciliation meta

**Given** the list and detail endpoints exist from Epic 2
**When** the response shape is extended
**Then** each contact object includes a `reconciliationMeta` field:

```json
{
  "reconciliationMeta": {
    "state": "reconciled" | "pending" | "never",
    "lastConfirmedAt": "2026-04-10T12:00:00Z" | null,
    "lastSharedAt": "2026-04-01T09:00:00Z" | null
  }
}
```

**And** the field is computed server-side via a read-model join on `statements` + `statement_confirmations`
**And** the query path is optimized: a `contact_reconciliation_view` PostgreSQL view or a read-model projection table so the list endpoint stays O(N) contacts, not O(N*M) contacts-×-statements [Source: epics.md Story 4.7; architecture.md §4.2 D4.4 read-side projections].

### AC3 — Projection handler updates read-model on confirmation event

**Given** `StatementConfirmedEvent` integration event fires from Story 4.6
**When** the handler `UpdateReconciliationProjectionOnStatementConfirmed` runs
**Then** it upserts the contact's row in `contact_reconciliation_projection { contact_id, last_confirmed_at, last_shared_at, updated_at }`
**And** `last_confirmed_at` takes the MAX of existing + new timestamp
**And** `last_shared_at` is updated similarly on `StatementLinkIssuedEvent` (Story 4.4's event)
**And** the projection is eventually consistent — the list endpoint JOINs this projection with live `contacts` table
**And** the projection has `[Trait("Category", "Projection")]` tests asserting eventual consistency under burst input [Source: architecture.md §4.2 D4.4 projection pattern].

### AC4 — Frontend consumes `reconciliationMeta` without new API call

**Given** the Epic 2 contacts list client already fetches `/v1/contacts`
**When** the response shape now includes `reconciliationMeta`
**Then** the NSwag-generated TS client regenerates automatically on next build (OpenAPI-drift gate ensures)
**And** `ContactListComponent` reads `contact.reconciliationMeta` and passes to `<ow-reconciliation-meta [state]="c.reconciliationMeta.state" [timestamp]="c.reconciliationMeta.lastConfirmedAt">`
**And** no new HTTP call is added — the reconciliation display piggybacks the existing list fetch [Source: architecture.md §5.4; Epic 2 contacts list].

### AC5 — Relative date is locale-aware

**Given** AR and EN locales support different relative date formats
**When** `ReconciliationMeta` renders
**Then** it uses `Intl.RelativeTimeFormat(locale, { numeric: 'auto' })` to produce:
- EN: "3 days ago", "yesterday", "today"
- AR: "قبل 3 أيام", "أمس", "اليوم"
**And** the `<time datetime="ISO">` attribute always carries the absolute ISO timestamp for SR consumption [Source: architecture.md §5.4 i18n; ux-design-specification.md UX Consistency Patterns].

### AC6 — Single-owner component `ReconciliationMeta` unchanged

**Given** `ReconciliationMeta` is owned by `libs/domain-ui` (Phase 1 Story 1.9)
**When** this story consumes it in the contacts list
**Then** no modifications to the component API are needed
**And** no new props — the `timestamp` + `state` inputs from Phase 1 suffice (UX-DR12 `reconciled` | `pending` | `never`)
**And** NetArchTest-equivalent (ESLint) asserts `ReconciliationMeta` is imported ONLY from `libs/domain-ui`, never redefined [Source: ux-design-specification.md single-owner rule; Story 1.9].

### AC7 — Pending → Reconciled transition visible in tests

**Given** the reconciliation flow has two states visible to the owner
**When** an E2E test runs
**Then** the test:
1. Owner creates a contact + records a debt
2. Owner generates a statement link (Story 4.4)
3. Owner refreshes `/contacts` → sees `ReconciliationMeta` state `pending` "Shared, awaiting confirmation"
4. Simulates recipient confirmation (direct POST to `/confirm` in test context)
5. Owner refreshes `/contacts` → sees `ReconciliationMeta` state `reconciled` with current date
**And** the test runs in both AR + EN locales via Playwright locale override [Source: epics.md Story 4.7].

### AC8 — No SignalR wiring, no Epic 7 leakage

**Given** Epic 4 is passive display only per delegated decisions
**When** this story lands
**Then** no SignalR hub is started, no WebSocket connection, no `Microsoft.AspNetCore.SignalR` dependency
**And** the owner explicitly refreshes or loads a new page to see updates
**And** a `@defer (on idle)` optionally polls the list endpoint every 5 minutes while the tab is active (LOW priority — may defer to Epic 7 if unnecessary)
**And** NetArchTest asserts no SignalR reference in Epic 4 slices [Source: delegated decisions — SignalR deferred to Epic 7; Story 4.6 AC8].

### AC9 — Projection bootstraps from existing rows (migration-friendly)

**Given** the projection table is new but historic data may exist
**When** the migration runs
**Then** `contact_reconciliation_projection` is populated by a one-shot backfill query:

```sql
INSERT INTO contact_reconciliation_projection (contact_id, last_confirmed_at, last_shared_at)
SELECT
  s.contact_id,
  MAX(s.confirmed_at) AS last_confirmed_at,
  MAX(s.issued_at) AS last_shared_at
FROM statements s
GROUP BY s.contact_id
ON CONFLICT (contact_id) DO UPDATE SET
  last_confirmed_at = GREATEST(contact_reconciliation_projection.last_confirmed_at, EXCLUDED.last_confirmed_at),
  last_shared_at = GREATEST(contact_reconciliation_projection.last_shared_at, EXCLUDED.last_shared_at);
```

**And** integration test runs against Testcontainers with pre-seeded statements to verify the backfill [Source: architecture.md §4.2 D4.3 migration patterns].

### AC10 — Accessibility + RTL parity

**Given** `ReconciliationMeta` is already axe-clean (Story 1.9)
**When** it appears on the contacts list
**Then** zero WCAG AA violations in both locales
**And** RTL: icon appears inline-start; relative date inline-end; Tooltip positioning respects `dir='rtl'`
**And** keyboard focus ring visible per §2.2 AAA contrast on financial surfaces [Source: architecture.md §2.2; Story 1.10].

## Tasks / Subtasks

### Backend — Projection table + handler

- [ ] **T1 (AC: 3, 9)** — Migration `YYYYMMDDHHMMSS_AddContactReconciliationProjection.cs`
  - [ ] Table `contact_reconciliation_projection { contact_id PK, last_confirmed_at, last_shared_at, updated_at }`
  - [ ] Backfill SQL per AC9
- [ ] **T2 (AC: 3)** — Wolverine handler `UpdateReconciliationProjectionOnStatementConfirmed`
  - [ ] Consumes `StatementConfirmedEvent`
  - [ ] UPSERT with GREATEST semantics on `last_confirmed_at`
- [ ] **T3 (AC: 3)** — Wolverine handler `UpdateReconciliationProjectionOnStatementLinkIssued`
  - [ ] Consumes `StatementLinkIssuedEvent`
  - [ ] UPSERT with GREATEST on `last_shared_at`
- [ ] **T4 (AC: 3)** — Repository `IContactReconciliationProjectionRepository`
  - [ ] `UpsertOnConfirmAsync(Guid contactId, DateTimeOffset confirmedAt)`
  - [ ] `UpsertOnIssueAsync(Guid contactId, DateTimeOffset issuedAt)`

### Backend — Contacts endpoint extension

- [ ] **T5 (AC: 2)** — Extend `GetContactsListHandler` (from Epic 2) + `GetContactByIdHandler`
  - [ ] JOIN on `contact_reconciliation_projection`
  - [ ] Compute `ReconciliationMetaDto { state, lastConfirmedAt, lastSharedAt }`
  - [ ] State derivation:
    - `last_confirmed_at != null` → `reconciled`
    - `last_shared_at != null AND last_confirmed_at IS NULL` → `pending`
    - else → `never`
- [ ] **T6 (AC: 2)** — Update `ContactDto` to include `reconciliationMeta`
  - [ ] NSwag regenerates TS client on next build
- [ ] **T7 (AC: 4)** — Verify OpenAPI-drift gate catches the schema change

### Frontend — Contacts list

- [ ] **T8 (AC: 1, 4)** — Update `apps/owner-workspace/src/app/features/contacts/list/contact-list.component.ts`
  - [ ] Reads `reconciliationMeta` from each contact
  - [ ] Renders `<ow-reconciliation-meta [state]="c.reconciliationMeta.state" [timestamp]="c.reconciliationMeta.lastConfirmedAt ?? c.reconciliationMeta.lastSharedAt">`
- [ ] **T9 (AC: 1)** — Update contact detail page similarly

### Frontend — i18n + locale-aware dates

- [ ] **T10 (AC: 5)** — Extend `libs/domain-ui/reconciliation-meta/reconciliation-meta.component.ts`
  - [ ] Uses `Intl.RelativeTimeFormat(locale)` for relative date string
  - [ ] `locale` signal injected from `LocaleService`

### Tests — Backend

- [ ] **T11 (AC: 3)** — Projection eventual consistency test
  - [ ] Burst 100 `StatementConfirmedEvent`s → assert projection row has correct latest timestamp
- [ ] **T12 (AC: 9)** — Backfill migration test with seeded statements
- [ ] **T13 (AC: 2)** — Integration test: list endpoint returns correct `reconciliationMeta` per contact state

### Tests — Frontend

- [ ] **T14 (AC: 1)** — Vitest component test: `ContactListComponent` renders ReconciliationMeta per contact
- [ ] **T15 (AC: 7)** — Playwright E2E: full flow from AC7

### Tests — Architecture

- [ ] **T16 (AC: 8)** — NetArchTest: no SignalR references in Epic 4 slices

## Dev Notes

### Critical guardrails

1. **Projection table, not inline JOIN.** A JOIN across `contacts`, `statements`, `statement_confirmations` for every list render would slow the contacts page to a crawl at 50+ contacts. The projection makes the list read-side O(contacts).
2. **Eventually consistent is fine.** An owner refreshing 100ms after a recipient confirmed MAY see the pending state still. The next refresh (seconds later) shows reconciled. This is acceptable per delegated decisions; Epic 7 SignalR closes the gap.
3. **No real-time means no real-time.** No SignalR. No WebSocket. No push. A `@defer (on idle)` 5-minute poll is optional and can be skipped.
4. **ReconciliationMeta is single-owner.** Don't modify the component. If a new state is needed, it's a Phase 1 revisit, not a duplicate component.
5. **Backfill migration is one-shot.** Runs once on deploy. Idempotent via ON CONFLICT.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/Contacts/
│   ├── List/GetContactsListHandler.cs                      # MODIFY (JOIN projection)
│   └── GetById/GetContactByIdHandler.cs                    # MODIFY
├── src/Faktuboh.Api/Slices/Contacts/Projections/
│   ├── UpdateReconciliationProjectionOnStatementConfirmed.cs   # NEW
│   └── UpdateReconciliationProjectionOnStatementLinkIssued.cs  # NEW
├── src/Faktuboh.Domain/ContactReconciliation/
│   └── IContactReconciliationProjectionRepository.cs       # NEW
├── src/Faktuboh.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/ContactReconciliationProjectionConfiguration.cs  # NEW
│   │   └── Migrations/
│   │       └── YYYYMMDDHHMMSS_AddContactReconciliationProjection.cs        # NEW
│   └── Repositories/ContactReconciliationProjectionRepository.cs   # NEW
└── tests/Faktuboh.Api.Tests/Slices/Contacts/Projections/
    ├── ProjectionEventualConsistencyTests.cs               # NEW
    └── BackfillMigrationTests.cs                           # NEW

frontend/
├── apps/owner-workspace/src/app/features/contacts/list/
│   └── contact-list.component.ts                           # MODIFY
├── apps/owner-workspace/src/app/features/contacts/detail/
│   └── contact-detail.component.ts                         # MODIFY
└── libs/domain-ui/reconciliation-meta/
    └── reconciliation-meta.component.ts                    # MODIFY (locale-aware relative date)
```

### Architecture compliance

- **FR37** — passive display of reconciliation.
- **§4.2 D4.4** (read-side projections) — projection table implements this.
- **UX-DR12** (ReconciliationMeta single-owner) — consumed.
- **Delegated: SignalR deferred** — enforced.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Wolverine | latest | Projection handlers |
| Intl.RelativeTimeFormat | Browser-native | Locale-aware relative dates |

### Testing requirements

- **Unit:** Projection UPSERT with GREATEST logic.
- **Integration:** Eventual consistency under burst; backfill.
- **E2E:** Full share → confirm → owner-sees-reconciled flow.

### Previous Story Intelligence

**Prerequisites:**
- Story 1.9 — `ReconciliationMeta` component
- Story 2.1 — `Contact` aggregate + contacts list endpoint
- Story 2.4 — contacts list UI
- Story 4.4 — `Statement` issuance + `StatementLinkIssuedEvent`
- Story 4.6 — `StatementConfirmedEvent`

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.7
- Read-side projections — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.2 D4.4
- UX-DR12 ReconciliationMeta — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component 8
- FR37 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Projection table enables O(N) list queries.
- Epic 7 will add SignalR on top of this projection — no data-model change needed.

### File List

_Populated during implementation._
