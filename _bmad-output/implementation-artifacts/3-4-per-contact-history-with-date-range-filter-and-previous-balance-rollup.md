# Story 3.4: Per-contact history with date-range filter and previous-balance rollup

Status: ready-for-dev

## Story

As a **user with months of transactions against one contact**,
I want to **view the full transaction history at `/contacts/{id}` and filter by date range — with transactions before the filter-start-date rolling up into a single "Previous balance: X USD, Y grams gold" synthesized row per denomination**,
so that I can **focus on recent activity without scrolling through months of history** (FR16, FR28), the balance calculation completes in under 200ms even at 1000+ row volumes (NFR-P7), and the date picker is right-aligned with a read-only Hijri toggle (NFR-A9f).

**Covers requirements:** FR16 (per-contact history with filter), FR28 (previous-balance rollup), NFR-P7 (200ms balance), NFR-A9f (Hijri calendar read-only), UX-DR12 (ReconciliationMeta).

## Acceptance Criteria

### AC1 — GET returns chronological history with per-denomination subtotals

**Given** a contact with ≥ 30 transactions across multiple denominations and dates
**When** the frontend issues `GET /v1/contacts/{id}/history?from={isoDate}&to={isoDate}`
**Then** the response is `ContactHistoryResponse` with:
  - `transactions: HistoryEntry[]` — chronologically ordered (date asc, then `recordedAt` asc), each row `{ id, occurredOn, direction, denominationCode, amount, note?, actionHistory: AuditMarker[] }` where `actionHistory` lists prior edits from the audit log
  - `previousBalance: BalanceEntry[]` — per-denomination `{ denominationCode, direction, amount }` computed from all transactions `WHERE occurred_on < from` (the synthesized rollup row) — empty array if no `from` filter is supplied
  - `runningBalance: BalanceEntry[]` — per-denomination totals as of `to` (or as of now if no `to`)
**And** p95 response time < 200ms at 1000-transaction baseline per NFR-P7

### AC2 — "Previous balance" synthesized row shape

**Given** the response contains a non-empty `previousBalance` array
**When** the frontend renders the history list
**Then** a **single synthesized row** appears ABOVE the chronological transactions, visually distinct with subtle neutral background `--surface-100`, rendering each `previousBalance` entry as an `AmountDisplay variant="compact"` pill
**And** the row's leading icon is a `<p-icon name="history">` marker
**And** the row is keyboard-navigable and screen-reader-announced as "Previous balance, {date-range-start}: {amount per denomination}"
**And** if `previousBalance` is empty, the synthesized row is omitted

### AC3 — Date-range picker with Hijri toggle

**Given** the user interacts with the date-range filter
**When** the picker is rendered
**Then** it uses `<p-datePicker selectionMode="range">` anchored right-aligned on the page (logical `inset-inline-end`)
**And** a toggle pill "Gregorian / Hijri" sets display mode (Hijri read-only at MVP per NFR-A9f — Arabic month names render but the underlying model is still Gregorian ISO dates)
**And** presets "This week / This month / This quarter / This year / All time" render as chip buttons
**And** applying a filter updates the URL query params (`?from=...&to=...`) for shareable links

### AC4 — RunningBalance display matches stored totals

**Given** the property-based test asserting `SUM(transactions in range) + previousBalance == runningBalance` for every denomination
**When** the response is consumed
**Then** the frontend displays a sticky footer with `runningBalance` per denomination
**And** an FsCheck property test verifies the invariant across 10k generated transaction sequences

### AC5 — ReconciliationMeta surfaces (graceful-never state)

**Given** the contact has NO confirmed statement at MVP (Epic 4 activates confirmation — not yet)
**When** the history page renders
**Then** `ReconciliationMeta` (UX-DR12) renders in a dismissible info band at the top: "Last reconciled: never"
**And** the component accepts an optional `lastReconciledAt: DateTimeOffset | null` prop; null renders "never" state
**And** Epic 4 activates the non-null path; this story's implementation must support both

### AC6 — Telemetry + ProcessingActivity

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** the query runs
**Then** OTel span captures aggregation latency
**And** telemetry `contact_history_viewed` emits with `{ opaque_user_id, contact_id_hash, has_date_filter: bool, transaction_count_bucket: "<10|10-100|100-1000|1k+" }`

### AC7 — RTL + a11y

**Given** the user's locale is Arabic
**When** the page renders in `dir="rtl"`
**Then** column order reverses (date right, amount left)
**And** date-range picker anchors to `inset-inline-end` which resolves to left edge in RTL
**And** axe-core zero WCAG 2.1 AA violations
**And** manual TalkBack Arabic walkthrough passes (NFR-A9)

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1)** — Create `Slices/Contacts/History/GetContactHistoryHandler.cs`
  - [ ] Endpoint `group.MapGet("/{id:guid}/history", ...)` under Contacts slice (not JournalEntries — the surface is contact-scoped)
  - [ ] `GetContactHistoryRequest` binds `from: DateOnly?, to: DateOnly?, limit: int = 1000` query parameters
  - [ ] Single SQL query using CTE + `FILTER` clauses:

```sql
WITH previous_rollup AS (
  SELECT denomination_code, direction, SUM(amount) AS amount
  FROM journal_entries
  WHERE contact_id = @contactId
    AND owner_subject_id = @ownerSubjectId
    AND valid_to IS NULL
    AND occurred_on < @from
  GROUP BY denomination_code, direction
),
windowed AS (
  SELECT *
  FROM journal_entries
  WHERE contact_id = @contactId
    AND owner_subject_id = @ownerSubjectId
    AND valid_to IS NULL
    AND (@from IS NULL OR occurred_on >= @from)
    AND (@to   IS NULL OR occurred_on <= @to)
  ORDER BY occurred_on ASC, recorded_at ASC
  LIMIT @limit
),
running AS (
  SELECT denomination_code, direction, SUM(amount) AS amount
  FROM journal_entries
  WHERE contact_id = @contactId
    AND owner_subject_id = @ownerSubjectId
    AND valid_to IS NULL
    AND (@to IS NULL OR occurred_on <= @to)
  GROUP BY denomination_code, direction
)
SELECT
  (SELECT json_agg(...) FROM previous_rollup) AS previous_balance,
  (SELECT json_agg(...) FROM windowed)        AS transactions,
  (SELECT json_agg(...) FROM running)         AS running_balance;
```

- [ ] **T2 (AC: 1, 4)** — `ContactHistoryResponse` sealed record with `transactions`, `previousBalance`, `runningBalance`, plus `actionHistory` merged per transaction via LEFT JOIN on `audit_log`
- [ ] **T3 (AC: 1)** — Composite index `ix_journal_entries_contact_occurred_on` on `(contact_id, occurred_on) WHERE valid_to IS NULL`
- [ ] **T4 (AC: 4)** — FsCheck property test: generate random transaction sequences; assert `sum(transactions in window) + previousBalance == runningBalance` per denomination/direction tuple
- [ ] **T5 (AC: 5)** — `ReconciliationMetaFields` extension on response: `{ lastReconciledAt: DateTimeOffset? }` — MVP returns null; Epic 4 populates

### Frontend

- [ ] **T6 (AC: 2, 3, 5, 7)** — Create `apps/owner-workspace/src/app/features/contacts/history/contact-history.component.ts`
  - [ ] Standalone + signals + OnPush; zoneless
  - [ ] `rxResource()` triggered by date-range signal; URL query params synchronized bidirectionally
  - [ ] Synthesized previous-balance row as the first element of the rendered list (not a separate section — keeps virtual-scroller math clean)
  - [ ] Running balance sticky footer via CSS `position: sticky; inset-block-end: 0`
  - [ ] `<p-datePicker selectionMode="range">` + Gregorian/Hijri toggle
  - [ ] Preset chips "This week/month/quarter/year/All time"
- [ ] **T7 (AC: 5)** — Create `ReconciliationMeta` component in `libs/domain-ui/reconciliation-meta/` (Phase 2 UX-DR12 component, new primitive)
  - [ ] `@Input() lastReconciledAt: InputSignal<DateTimeOffset | null>`
  - [ ] Renders "Last reconciled: {date}" or "Last reconciled: never"
  - [ ] Uses `<p-message severity="info">` with dismissible flag stored in `localStorage`
- [ ] **T8 (AC: 2)** — History row consumes `AmountDisplay` + audit-marker icons (for edited/deleted rows — strikethrough comes from Story 3.3 + Story 3.2's audit trail)

### Tests

- [ ] **T9 (AC: 1, 4)** — `GetContactHistoryHandlerTests` — table-driven date ranges: no-filter, from-only, to-only, range, empty-range
- [ ] **T10 (AC: 1)** — Integration test with Testcontainers + 1000-transaction seed; p95 < 200ms; `EXPLAIN` captured in Verify snapshot
- [ ] **T11 (AC: 4)** — FsCheck property test (see T4) as part of unit suite
- [ ] **T12 (AC: 2, 3, 7)** — Vitest: synthesized row rendering; preset chips apply correct date ranges; Hijri toggle re-renders labels
- [ ] **T13 (AC: 3, 7)** — Playwright E2E: Arabic flow with Hijri toggle + axe a11y + RTL visual regression

## Dev Notes

### Critical guardrails

1. **`previousBalance` is a SYNTHESIZED row, not a stored entity.** It exists only in the response payload; do not attempt to insert it into `journal_entries`.
2. **Per-denomination rigor.** `previousBalance` + `runningBalance` are always arrays, one entry per denomination-direction pair. Never `"total"` fields. Omit denominations with zero balance.
3. **Date comparisons use `occurred_on`** (`DateOnly`), not `recorded_at`. A transaction recorded today but dated last month belongs to last month's window.
4. **Single-query aggregation** via CTE. Three separate round-trips (previous + windowed + running) would blow NFR-P7 at 1000-row baseline. The composite index is mandatory.
5. **Hijri is read-only at MVP.** The toggle renders Arabic month names using `Intl.DateTimeFormat` with `calendar: 'islamic-umalqura'`, but the model/wire stays ISO Gregorian `DateOnly`. Writing Hijri dates comes post-MVP.
6. **ReconciliationMeta renders gracefully with null.** Do not omit the component when `lastReconciledAt` is null — the "never" state is intentional signaling that Epic 4 hasn't kicked in yet.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/Contacts/History/
│   ├── GetContactHistoryRequest.cs                   # NEW
│   ├── GetContactHistoryResponse.cs                  # NEW
│   ├── HistoryEntry.cs                               # NEW
│   ├── BalanceEntry.cs                               # REUSE from Story 2.4
│   └── GetContactHistoryHandler.cs                   # NEW
├── src/Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDD_AddJournalEntryContactOccurredOnIdx.cs  # NEW
└── tests/Faktuboh.Api.Tests/Slices/Contacts/History/
    ├── GetContactHistoryHandlerTests.cs              # NEW
    └── GetContactHistoryEndpointTests.cs             # NEW (Integration + perf)

frontend/
├── apps/owner-workspace/src/app/features/contacts/history/
│   ├── contact-history.component.ts                  # NEW
│   ├── contact-history.component.html                # NEW
│   ├── contact-history.component.scss                # NEW (logical props)
│   ├── contact-history.component.spec.ts             # NEW
│   └── previous-balance-row.component.ts             # NEW (small sub-component)
└── libs/domain-ui/reconciliation-meta/
    ├── reconciliation-meta.component.ts              # NEW (UX-DR12)
    ├── reconciliation-meta.component.html            # NEW
    └── reconciliation-meta.component.spec.ts         # NEW
```

### Previous-balance row shape (reference)

```typescript
type PreviousBalanceRow = {
  denominationCode: string;     // "USD"
  direction: 'receivable' | 'payable';
  amount: string;               // decimal string "300.00" per §5.2.5
};

// Rendered as:
// [icon] Previous balance (before 2026-01-01): لي 300.00 USD • عليّ 10.00 g_gold
```

### Testing standards

Per Story 2.1/3.1. **FsCheck property test is the correctness linchpin** — generate 10k random transaction sequences with varied occurred_on, denominations, directions; assert the invariant.

### Project Structure Notes

History is a Contact-scoped surface (`/contacts/{id}/history` — logically), so the slice lives under `Slices/Contacts/History/` rather than `Slices/JournalEntries/`. Rationale: the primary response shape is "a contact's story" not "a list of journal entries" — the contact is the aggregate owner.

### Architecture compliance

- **§4 D4.1** Postgres primary
- **§4 D4.4** no CQRS — single query into primary store
- **§5.2.4** DateOnly for `occurred_on`
- **§5.2.5** Money decimal-string wire
- **FR16, FR28** per-contact history + previous-balance rollup
- **NFR-P7** 200ms
- **NFR-A9f** Hijri read-only
- **UX-DR12** ReconciliationMeta with graceful-never state

### Library/framework requirements

Per Story 3.1. Additional:
- `Intl.DateTimeFormat` with `calendar: 'islamic-umalqura'` for Hijri month names
- PrimeNG `<p-datePicker selectionMode="range">` — confirm API via `@primeng/mcp`
- FsCheck property-based tests in xUnit v3

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FsCheck + FluentAssertions |
| Integration | Testcontainers Postgres + 1000-transaction Bogus seed + p95 < 200ms + EXPLAIN snapshot |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — Arabic + Hijri toggle + axe a11y + RTL visual regression |

### Previous Story Intelligence

- **Story 0.6** (bitemporal) — `valid_to IS NULL` filter hides deleted rows
- **Story 0.14** (AR-047 telemetry)
- **Story 1.9** (Phase-1 components) — `AmountDisplay` consumed unchanged; `ReconciliationMeta` is a new Phase-2 component owned here per UX Spec Phase-2 allocation (UX-DR12)
- **Story 2.4** (List contacts with balances) — `BalanceEntry` DTO reused; aggregation index pattern established
- **Story 3.1** (record debt) — the `journal_entries` schema this query consumes
- **Story 3.2, 3.3** (edit/delete debt) — the `audit_log` rows joined via `actionHistory` field

## References

- [epics.md Story 3.4](../planning-artifacts/epics.md)
- [architecture.md §4 D4.1, D4.4](../planning-artifacts/architecture.md)
- [architecture.md §5.2.4, §5.2.5](../planning-artifacts/architecture.md)
- [prd.md FR16, FR28, NFR-P7, NFR-A9](../planning-artifacts/prd.md)
- [ux-design-specification.md UX-DR12 ReconciliationMeta](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md UX Consistency Patterns → Data Density, Responsive Design](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
