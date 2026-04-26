# Story 5.4: Transfer history view

Status: ready-for-dev

## Story

As **a user**,
I want **to see all transfers I've executed across all contacts in a chronological list**,
so that **I can audit my debt-reassignment activity** (FR26).

**Covers requirements:** Epic 5 Story 5.4 — FR26 (transfer history view); UX-DR30 factual empty-state copy; UX-DR15 (AuditBadge consumption); Dashboard exclusion rule (transfers reshape balances, not exposure) [Source: epics.md Epic 5 Story 5.4; prd.md FR26].

**Why this story now:** Stories 5.1–5.3 ship Transfer creation + audit linkage. This story closes Epic 5's read-side: the user can audit their full transfer activity. It also encodes the Dashboard exclusion rule (transfers excluded from contact bar chart + denomination pie chart) — that rule must be settled before Epic 6 ships Exchange (Exchange has a similar but distinct exclusion question).

## Acceptance Criteria

### AC1 — `/transfers` route renders chronological list

**Given** the user has ≥ 1 transfer
**When** the user navigates to `/transfers`
**Then** a chronological list (newest first) shows each transfer with:
- Source contact name
- Target contact name
- Amount + denomination
- Transfer date (ux-design-specification date format per locale)
- Optional note (truncated to 120 chars + tooltip on hover for full text)
**And** the list paginates with `<p-paginator>` (PrimeNG) at 25 rows per page
**And** sorting defaults to `executedAt DESC` (newest first); user can sort by `executedAt ASC` via column header [Source: epics.md Epic 5 Story 5.4 AC1].

### AC2 — Each row shows AuditBadge linking to source + target

**Given** Story 5.3 ships `<du-audit-badge>` with `transfer-from` and `transfer-to` kinds
**When** a transfer row renders
**Then** TWO badges appear per row:
- "Source: [contact name]" with `kind="transfer-from"` (badge meaning: "click to view the source debt that was transferred from")
- "Target: [contact name]" with `kind="transfer-to"` (badge meaning: "click to view the target debt this was transferred to")
**And** tapping either badge navigates to the corresponding debt detail page
**And** the AuditBadge component is consumed UNCHANGED from `libs/domain-ui` (no inputs/outputs added) [Source: epics.md Epic 5 Story 5.4 AC2; Story 5.3 single-owner rule].

### AC3 — Date-range filter

**Given** users may want to scope the view (last month, this quarter, etc.)
**When** the user opens the date-range filter
**Then** PrimeNG `<p-datepicker selectionMode="range">` shows a range picker
**And** the filter applies via query string (`?from=YYYY-MM-DD&to=YYYY-MM-DD`)
**And** clearing the filter shows all transfers
**And** the filter state persists across navigation (back button restores) [Source: epics.md Epic 5 Story 5.4 AC3].

### AC4 — Empty state per UX-DR30 factual copy

**Given** the user has 0 transfers
**When** `/transfers` renders
**Then** a centered empty-state shows:
- Icon: `pi-inbox` (neutral)
- Heading: "No transfers yet" (Transloco; AR equivalent: "لا توجد تحويلات بعد")
- Body: factual one-line — "Transfers appear here once you reassign a debt." (NOT apologetic — no "Sorry, nothing to show!")
**And** NO call-to-action button (transfers are contextual to debts, not initiated from this page)
**And** the empty-state copy is verified against UX-DR30 forbidden-words list (no "sorry", "oops", emojis) via ESLint rule [Source: epics.md Epic 5 Story 5.4 AC4; UX-DR30].

### AC5 — Transfers EXCLUDED from Dashboard charts

**Given** Stories 3.6 (contact bar chart) and 3.7 (denomination pie chart) aggregate journal entries
**When** transfers are present
**Then** the bar chart EXCLUDES journal entries with `correlation_id != id` (i.e., compound entries) — only "real exposure" entries count
**And** the pie chart EXCLUDES the same set
**And** a NetArchTest assertion + integration test verifies the exclusion logic
**And** the rationale is documented in `docs/development/dashboard-aggregation-rules.md`: "Transfers reshape existing balances, they don't add exposure. Including them would double-count."
**And** Epic 6 Story 6.4 will add Exchange to the SAME exclusion (same rule, different `correlation_id` source) [Source: epics.md Epic 5 Story 5.4 AC5].

### AC6 — `GET /v1/transfers` endpoint

**Given** the frontend needs paginated transfer data
**When** the endpoint is implemented
**Then** `GET /v1/transfers?page=1&pageSize=25&from=YYYY-MM-DD&to=YYYY-MM-DD&sort=executedAt:desc` returns:
```json
{
  "items": [
    {
      "correlationId": "uuid",
      "sourceContactId": "uuid", "sourceContactName": "...",
      "targetContactId": "uuid", "targetContactName": "...",
      "amount": { "amount": "200.00", "currency": "EGP" },
      "note": "...",
      "executedAt": "2026-04-26T14:30:00Z",
      "sourceDebtRoute": ["/contacts", "uuid", "debts", "uuid"],
      "targetDebtRoute": ["/contacts", "uuid", "debts", "uuid"]
    }
  ],
  "total": 42, "page": 1, "pageSize": 25
}
```
**And** the response envelope follows the API conventions per architecture (consistent pagination shape across endpoints)
**And** the query reads from `journal_entries` joined on `correlation_id` to compose the source+target view
**And** server-side query is indexed (uses `ix_journal_entries_correlation_id` from Story 0.15) [Source: epics.md Epic 5 Story 5.4 AC1, AC3].

### AC7 — Authorization: user sees only their own transfers

**Given** the endpoint is `.RequireAuthorization()`
**When** a user queries `/v1/transfers`
**Then** the query filters by `owner_subject_id = currentUser.SubjectId`
**And** an integration test asserts a second user's transfers are NOT visible
**And** RLS-style discipline is documented + tested (NetArchTest verifies the `owner_subject_id` filter clause is present in the query)

### AC8 — Performance: < 200ms for typical query

**Given** a typical query returns ≤ 25 rows
**When** measured against Testcontainers Postgres with 1000 transfer rows seeded
**Then** p95 response time < 200ms
**And** the index `ix_journal_entries_correlation_id` (Story 0.15) is used (assert via EF Core query log) [Source: prd.md NFR-P3 inferred].

### AC9 — Telemetry: `transfer_history_viewed` event

**Given** AR-047 telemetry primitive is wired
**When** the user opens `/transfers`
**Then** `TelemetryEmitter.Emit("transfer_history_viewed", { opaque_user_id, transfer_count_bucket, has_date_filter: bool })` fires
**And** PII guard validates [Source: Story 0.14 telemetry primitive].

### AC10 — A11y + RTL parity

**Given** the list is a `<p-table>` (PrimeNG) with PrimeNG built-in a11y
**When** rendered
**Then** column headers have `scope="col"`
**And** sort buttons announce direction via `aria-sort`
**And** Playwright RTL parity test passes (LTR + RTL screenshots match within tolerance)
**And** axe-core CI gate (Story 1.10) reports zero WCAG 2.1 AA violations

## Tasks / Subtasks

### Backend — Slice + endpoint

- [ ] **T1 (AC: 6, 7)** — Create `backend/src/Faktuboh.Api/Slices/JournalEntries/Transfers/Transfers.Endpoints.cs`
  - [ ] `MapGroup("/v1/transfers").RequireAuthorization()` + `ProcessingActivity.OwnerLedger`
  - [ ] `GET /v1/transfers` endpoint
- [ ] **T2 (AC: 6, 7)** — Create request/response DTOs
  - [ ] `ListTransfersRequest` (query params: page, pageSize, from?, to?, sort?)
  - [ ] `ListTransfersResponse` (paginated envelope)
  - [ ] `TransferListItem` (per-row shape)
- [ ] **T3 (AC: 6, 7, 8)** — Create `ListTransfersHandler`
  - [ ] Query `journal_entries` filtered by `owner_subject_id` + grouped by `correlation_id`
  - [ ] Joins to `contacts` for source + target names
  - [ ] Pagination + date filter + sort
  - [ ] Server-side filter; never client-side
- [ ] **T4 (AC: 8)** — Verify index usage
  - [ ] Integration test asserts query uses `ix_journal_entries_correlation_id`
  - [ ] If a missing supporting index is found (e.g., `(owner_subject_id, executed_at)`), add via migration

### Backend — Dashboard exclusion

- [ ] **T5 (AC: 5)** — Modify Dashboard aggregation queries
  - [ ] Edit Stories 3.6 + 3.7 query handlers (or shared query helpers)
  - [ ] Add `WHERE correlation_id = id` filter (excludes compound entries)
  - [ ] Add NetArchTest assertion: any "Dashboard" or "Aggregation" query type MUST contain this filter
- [ ] **T6 (AC: 5)** — Document rule in `docs/development/dashboard-aggregation-rules.md`
  - [ ] Rationale + future extension to Exchange (Epic 6)

### Frontend — List + filter

- [ ] **T7 (AC: 1, 2, 3)** — Create `apps/owner-workspace/src/app/features/transfers/transfer-history/transfer-history-page.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Selector `ow-transfer-history-page`
  - [ ] Lazy route `/transfers`
  - [ ] Consumes `transfersClient.list(query): Observable<ListTransfersResponse>` (NSwag-generated)
  - [ ] PrimeNG `<p-table>` + `<p-paginator>` + `<p-datepicker selectionMode="range">`
- [ ] **T8 (AC: 2)** — Mount AuditBadge per row (×2)
  - [ ] Source badge: `<du-audit-badge [linkedRoute]="row.sourceDebtRoute" kind="transfer-from" [label]="row.sourceContactName" />`
  - [ ] Target badge: `<du-audit-badge [linkedRoute]="row.targetDebtRoute" kind="transfer-to" [label]="row.targetContactName" />`
  - [ ] Component consumed UNCHANGED from `libs/domain-ui`

### Frontend — Empty state

- [ ] **T9 (AC: 4)** — Empty-state component (shared per UX-DR30)
  - [ ] If a generic `<ow-empty-state>` exists from Story 1.8, reuse it
  - [ ] Else inline empty-state with `pi-inbox` icon + factual copy
  - [ ] Transloco keys `transfers.empty.heading` + `transfers.empty.body`

### Frontend — Filter persistence

- [ ] **T10 (AC: 3)** — Wire date-range filter to query string
  - [ ] Use Angular Router `queryParams` for from/to + page
  - [ ] Filter state restored on back-button navigation

### Telemetry

- [ ] **T11 (AC: 9)** — Emit `transfer_history_viewed` event

### Tests — Backend

- [ ] **T12 (AC: 6, 7, 8)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/JournalEntries/Transfers/`
  - [ ] `ListTransfersEndpointTests` — pagination, date filter, sort
  - [ ] Auth filter: second user's transfers not visible
  - [ ] Performance: p95 < 200ms with 1000 seeded rows
  - [ ] Index usage assertion via EF Core query log
- [ ] **T13 (AC: 5)** — Dashboard exclusion integration tests
  - [ ] Seed transfers + non-transfers; assert dashboard queries return only non-transfers
  - [ ] NetArchTest: dashboard query types contain the exclusion clause

### Tests — Frontend

- [ ] **T14 (AC: 1-4, 10)** — Vitest tests in `transfer-history-page.component.spec.ts`
  - [ ] List renders rows
  - [ ] Both AuditBadges per row present + correctly configured
  - [ ] Empty-state shows when 0 transfers
  - [ ] Date filter applies via query string
- [ ] **T15 (AC: 1-3, 10)** — Playwright E2E in `apps/owner-workspace/e2e/transfers/transfer-history.spec.ts`
  - [ ] Navigate to /transfers; see list
  - [ ] Apply date filter; see filtered results
  - [ ] Tap AuditBadge → navigate to debt detail
  - [ ] axe-core a11y assertion (LTR + RTL)
  - [ ] RTL screenshot diff

### CI gates

- [ ] **T16 (AC: 4)** — Add UX-DR30 forbidden-words ESLint rule for empty-state strings
  - [ ] Forbidden in i18n: "sorry", "oops", emoji code points, "unfortunately"
  - [ ] If rule already exists from Story 1.8, extend allowlist coverage to `transfers.empty.*` keys

## Dev Notes

### Critical guardrails (do not violate)

1. **AuditBadge consumed UNCHANGED.** Per Story 5.3 single-owner rule. No inputs/outputs added; component used as-is.
2. **Dashboard exclusion of compound entries.** Transfers + future Exchanges reshape existing balances, they don't add exposure. The `WHERE correlation_id = id` filter is the canonical exclusion. NetArchTest enforces.
3. **Empty-state copy is FACTUAL, not apologetic.** Per UX-DR30. ESLint rule on i18n keys.
4. **Server-side filter only.** Date-range filter applies in the query, never in the client. A 10K-transfer user must not download 10K rows for client-side filtering.
5. **Owner-only data.** `owner_subject_id` filter on every query; integration test verifies cross-tenant isolation.
6. **No CTA button on empty state.** Transfers are initiated from debt detail pages, not from this view. Adding a CTA here would mislead users about the entry point.
7. **Transloco for ALL user-facing strings.** Even the AuditBadge label is computed Transloco-keyed by the caller.
8. **`<p-table>` not custom HTML table.** PrimeNG table provides built-in a11y (`scope`, `aria-sort`) — re-implementing risks regressions.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Api/Slices/JournalEntries/Transfers/
│   ├── Transfers.Endpoints.cs                             # NEW
│   ├── ListTransfersRequest.cs                            # NEW
│   ├── ListTransfersResponse.cs                           # NEW
│   └── ListTransfersHandler.cs                            # NEW
├── Faktuboh.Api/Slices/Dashboard/  (or wherever 3.6/3.7 live)
│   ├── ContactBarChartHandler.cs                          # MODIFY (exclusion filter)
│   └── DenominationPieChartHandler.cs                     # MODIFY (exclusion filter)
└── docs/development/
    └── dashboard-aggregation-rules.md                     # NEW

apps/owner-workspace/
├── src/app/features/transfers/transfer-history/
│   ├── transfer-history-page.component.ts                 # NEW
│   ├── transfer-history-page.component.html               # NEW
│   ├── transfer-history-page.component.scss               # NEW
│   └── transfer-history-page.component.spec.ts            # NEW
├── src/app/features/transfers/transfers.routes.ts         # MODIFY (add /transfers route)
├── src/assets/i18n/{ar,en}.json                           # MODIFY (transfers.* keys)
└── e2e/transfers/transfer-history.spec.ts                 # NEW

backend/tests/Faktuboh.Api.Tests/Slices/
├── JournalEntries/Transfers/ListTransfersEndpointTests.cs # NEW
└── Dashboard/DashboardExclusionTests.cs                   # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Integration | List query, pagination, date filter, auth, performance | `WebApplicationFactory` + Testcontainers Postgres |
| Architectural | Dashboard exclusion clause + AuditBadge unchanged | NetArchTest + Vitest snapshot |
| Unit/Component | List rendering + filter + empty-state | Vitest + Angular Testing Library |
| E2E | Navigate + filter + badge navigation + a11y + RTL | Playwright |

### Architecture compliance

- **FR26** — transfer history view.
- **UX-DR15** — AuditBadge consumption.
- **UX-DR30** — factual empty-state copy.
- **§5.3.2** — slice-internal organization.
- **§5.4** — frontend conventions.
- **NFR-A9** — RTL + a11y.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | .NET 10, EF Core 10, Wolverine | Same stack |
| Frontend | Angular 21 + PrimeNG 21 + Transloco | `<p-table>`, `<p-paginator>`, `<p-datepicker selectionMode="range">` |
| TS contracts | NSwag | `transfers.client.ts` extended with `list()` |

### Testing requirements

- **Integration:** auth isolation (tenant test), pagination correctness, date filter correctness, p95 < 200ms.
- **Architectural:** dashboard exclusion clause check via NetArchTest.
- **E2E:** filter persistence + badge navigation + a11y + RTL parity.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.15 — `correlation_id` column + index
- Story 1.8 — empty-state pattern (if `<ow-empty-state>` exists, reuse)
- Story 1.10 — axe-core CI gate
- Story 3.6 — contact bar chart (modified for exclusion)
- Story 3.7 — denomination pie chart (modified for exclusion)
- Story 5.2 — Transfer backend (produces `correlation_id`-linked entries)
- Story 5.3 — AuditBadge component (consumed unchanged)

**Coordination notes:**
- The dashboard exclusion rule generalizes to Epic 6 (Exchange's compound entries excluded by the same `correlation_id != id` filter). Story 6.4 inherits this rule unchanged.
- The `<du-audit-badge>` consumption pattern here is the template for Stories 6.4 / 6.5.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 5 Story 5.4
- FR26 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- UX-DR30 empty-state — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md)
- AuditBadge spec — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §11
- Story 5.2 backend — [_bmad-output/implementation-artifacts/5-2-atomic-compound-journal-entry-transfer.md](./5-2-atomic-compound-journal-entry-transfer.md)
- Story 5.3 AuditBadge — [_bmad-output/implementation-artifacts/5-3-auditbadge-component-with-bidirectional-navigation.md](./5-3-auditbadge-component-with-bidirectional-navigation.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Dashboard exclusion rule documented + enforced by NetArchTest; Epic 6 inherits.
- AuditBadge consumed unchanged.
- Server-side filter only; auth-isolated query.

### File List

_Populated during implementation._
