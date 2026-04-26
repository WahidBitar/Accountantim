# Story 6.5: View historical exchange rate on past operations

Status: ready-for-dev

## Story

As **a user auditing past exchanges**,
I want **to see the exact rate that was locked for any historical exchange operation**,
so that **I can resolve disputes or verify calculations** (FR20).

**Covers requirements:** Epic 6 Story 6.5 — FR20 (view historical exchange rate); AuditBadge `exchange-prior`/`exchange-derived` consumption (Story 5.3 unchanged); historical rate query via Story 6.7's `locked_rate_id` FK [Source: epics.md Epic 6 Story 6.5; prd.md FR20].

**Why this story now:** Story 6.4 ships Exchange creation + writes the audit metadata. This story ships the read-side view that closes the audit loop on FR20. It is also the second consumer of Story 5.3's AuditBadge `exchange-*` kinds.

## Acceptance Criteria

### AC1 — "View exchange history" entry on debt detail

**Given** a debt that was created via Exchange (or has Exchange in its lineage)
**When** the user navigates to the debt detail page
**Then** an inline "View exchange history" link/button appears
**And** tapping it opens the Exchange History panel (drawer or modal — drawer per UX-DR pattern)
**And** if the debt has NO Exchange in its lineage, the link does NOT appear
**And** the link is detected via `journal_entries.locked_rate_id IS NOT NULL` for any leg of the debt's correlation chain [Source: epics.md Epic 6 Story 6.5 AC; Story 6.7 column].

### AC2 — Panel shows: original denom, target denom, locked rate, timestamp

**Given** the panel is open
**When** the panel renders
**Then** it shows:
- Original denomination + amount: e.g., "10g gold"
- Derived denomination + amount: e.g., "$950 USD"
- Locked rate: "$95/g"
- Locked-rate source: `'system'` or `'user-override'`
- If user-override, the system rate at that time: "$92.50/g (system rate at time of exchange)"
- Exchange timestamp (locale-formatted)
- Optional note from the original Exchange [Source: epics.md Epic 6 Story 6.5 AC; Story 6.4 audit metadata columns].

### AC3 — User-override is visually distinguished from system rate

**Given** the audit context
**When** `lockedRateSource = "user-override"`
**Then** the panel shows a `<p-tag severity="warn">` chip "Adjusted from system rate"
**And** for `lockedRateSource = "system"`, no special chip appears (just the rate value)
**And** the chip is keyboard-focusable + screen-reader announced ("Rate was adjusted by user from system value of [X]")

### AC4 — AuditBadge links the two sides of the Exchange

**Given** Story 5.3 ships `<du-audit-badge kind="exchange-prior" | "exchange-derived" />`
**When** the panel renders
**Then** TWO badges appear:
- `kind="exchange-prior"` linking to the SOURCE leg's debt detail (the one that was closed in the Exchange)
- `kind="exchange-derived"` linking to the TARGET leg's debt detail (the new one opened)
**And** the AuditBadge is consumed UNCHANGED from `libs/domain-ui` (Story 5.3 single-owner rule)
**And** when viewed from the source side, the "exchange-prior" badge is SELF (current debt) so it's omitted; only "exchange-derived" shown
**And** when viewed from the target side, the "exchange-derived" badge is SELF; only "exchange-prior" shown [Source: Story 5.3 + Story 6.4 AC15].

### AC5 — `GET /v1/journal-entries/{id}/exchange-history` endpoint

**Given** the panel needs the historical Exchange data
**When** the endpoint is called with the debt's journal entry ID
**Then** the response is `200 OK` with body:
```json
{
  "correlationId": "uuid",
  "executedAt": "2026-04-26T14:30:00Z",
  "originalDenomination": "XAU", "originalAmount": "10.0000",
  "derivedDenomination": "USD", "derivedAmount": "950.00",
  "lockedRate": "95.00000000",
  "lockedRateSource": "user-override",
  "systemRateAtTime": "92.50000000",
  "sourceJournalEntryId": "uuid",
  "targetJournalEntryId": "uuid",
  "sourceContactId": "uuid", "sourceContactName": "...",
  "targetContactId": "uuid", "targetContactName": "...",
  "note": "..."
}
```
**And** if the journal entry has no `locked_rate_id`, returns `404 Not Found` with code `faktuboh.exchanges.not_an_exchange`
**And** the query joins `journal_entries` + `rates` + `contacts` via the `correlation_id` and `locked_rate_id` FKs
**And** authorization filter: user sees only their own data [Source: Story 6.4 audit metadata + Story 6.7 FK].

### AC6 — Performance: < 200ms for typical query

**Given** indexed lookups via `correlation_id` and `locked_rate_id`
**When** the endpoint serves a request
**Then** p95 response time < 200ms with realistic data volume
**And** integration test asserts via timing measurement

### AC7 — Telemetry: `exchange_history_viewed` event

**Given** AR-047 telemetry primitive
**When** the panel opens
**Then** `TelemetryEmitter.Emit("exchange_history_viewed", { opaque_user_id, correlation_id_hash, was_user_overridden: bool })` fires
**And** PII guard validates [Source: Story 0.14].

### AC8 — A11y + RTL parity

**Given** the panel renders structured data
**When** axe-core scans
**Then** zero WCAG 2.1 AA violations
**And** Playwright RTL parity test passes
**And** screen-reader announces panel open + each labeled field

### AC9 — `audit-trail-formatter.ts` reused for inline rendering

**Given** Story 6.4 ships `formatExchangeAuditLine` for the audit trail surface
**When** the panel needs an inline summary line
**Then** the same helper is consumed unchanged
**And** the panel's expanded view shows the structured fields per AC2; the inline summary uses the helper for consistency

## Tasks / Subtasks

### Backend — Endpoint

- [ ] **T1 (AC: 5)** — Create `backend/src/Faktuboh.Api/Slices/JournalEntries/Exchange/History/`
  - [ ] `ExchangeHistory.Endpoints.cs` with `GET /v1/journal-entries/{id}/exchange-history`
  - [ ] `GetExchangeHistoryHandler`
  - [ ] `GetExchangeHistoryResponse` per AC5 shape
  - [ ] Authorization filter

### Backend — Query

- [ ] **T2 (AC: 5, 6)** — Implement the join query
  - [ ] `journal_entries` JOIN `rates` ON `locked_rate_id` JOIN `contacts` (twice for source + target)
  - [ ] Use existing indexes; assert via EXPLAIN ANALYZE
- [ ] **T3 (AC: 5)** — Register error code `faktuboh.exchanges.not_an_exchange` (404)

### Backend — Telemetry

- [ ] **T4 (AC: 7)** — Wire `exchange_history_viewed` event

### Frontend — Panel

- [ ] **T5 (AC: 1, 2, 3)** — Create `apps/owner-workspace/src/app/features/exchanges/exchange-history-panel/exchange-history-panel.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Selector `ow-exchange-history-panel`
  - [ ] PrimeNG `<p-drawer position="end">` (right-side drawer; flips for RTL)
  - [ ] Opens via input signal; emits close event
  - [ ] Renders structured fields per AC2
  - [ ] User-override chip per AC3
- [ ] **T6 (AC: 1)** — Wire entry-point on debt detail
  - [ ] Modify `debt-detail.component.html` to show "View exchange history" when applicable
  - [ ] Detection: query backend or rely on prefetched journal entry's `locked_rate_id`

### Frontend — AuditBadge integration

- [ ] **T7 (AC: 4)** — Mount AuditBadge in panel
  - [ ] Show only the OPPOSITE-side badge (omit self-link)
  - [ ] Consumed UNCHANGED from `libs/domain-ui`

### Frontend — Generated client

- [ ] **T8 (AC: 5)** — Extend `libs/core/api/exchanges.client.ts`
  - [ ] Add `getExchangeHistory(journalEntryId): Observable<ExchangeHistoryResponse>`
  - [ ] OpenAPI snapshot updated

### Tests — Backend

- [ ] **T9 (AC: 5, 6)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/JournalEntries/Exchange/History/`
  - [ ] Happy path: returns full structured payload
  - [ ] Non-Exchange entry returns 404
  - [ ] Auth filter: cross-tenant isolation
  - [ ] Performance: p95 < 200ms

### Tests — Frontend

- [ ] **T10 (AC: 1-4, 8)** — Vitest tests for panel rendering
  - [ ] System-rate path
  - [ ] User-override path with chip
  - [ ] AuditBadge mount
- [ ] **T11 (AC: 1, 4, 8)** — Playwright E2E
  - [ ] Open panel from debt detail
  - [ ] Verify all fields rendered
  - [ ] Tap AuditBadge → navigate to peer side
  - [ ] axe-core a11y assertion
  - [ ] RTL parity

## Dev Notes

### Critical guardrails (do not violate)

1. **AuditBadge consumed UNCHANGED.** Per Story 5.3 single-owner rule.
2. **Read-side only.** This story does NOT modify Exchange data; it just reads + displays.
3. **`locked_rate_id IS NOT NULL` is the Exchange detector.** Don't introduce a separate `is_exchange` boolean — use the FK presence.
4. **Self-link omitted.** When viewing the source side, don't show a badge that links to self. Only render the "other side" badge.
5. **User-override chip is informational, not interactive.** It's a `<p-tag>` with `aria-label`, not a button.
6. **Drawer position flips for RTL.** PrimeNG `<p-drawer>` with logical positioning (`position="end"` is locale-aware).
7. **404 distinguishes "not an exchange" from "not found."** Different error codes; the route returns 404 for both, but the body's `code` differs.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Api/Slices/JournalEntries/Exchange/History/
│   ├── ExchangeHistory.Endpoints.cs                        # NEW
│   ├── GetExchangeHistoryResponse.cs                       # NEW
│   └── GetExchangeHistoryHandler.cs                        # NEW
└── Faktuboh.Application/Errors/
    └── ErrorCatalog.cs                                     # MODIFY (+ not_an_exchange code)

apps/owner-workspace/
├── src/app/features/exchanges/exchange-history-panel/
│   ├── exchange-history-panel.component.ts                 # NEW
│   ├── exchange-history-panel.component.html               # NEW
│   ├── exchange-history-panel.component.scss               # NEW
│   └── exchange-history-panel.component.spec.ts            # NEW
├── src/app/features/debts/debt-detail/
│   └── debt-detail.component.html                          # MODIFY (entry-point button)
└── src/assets/i18n/{ar,en}.json                            # MODIFY (exchange-history.* keys)

libs/core/api/
└── exchanges.client.ts                                     # MODIFY (+ getExchangeHistory)

backend/tests/
└── Faktuboh.Api.Tests/Slices/JournalEntries/Exchange/History/
    └── ExchangeHistoryEndpointTests.cs                     # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Integration | Endpoint shape + auth + perf | Testcontainers + WebApplicationFactory |
| Component | Panel rendering + chip + badge | Vitest + Angular Testing Library |
| E2E | Open + navigate + a11y + RTL | Playwright |

### Architecture compliance

- **FR20** — view historical exchange rate.
- **§5.4** — frontend conventions.
- **NFR-A9** — RTL + a11y.
- **Story 5.3** — AuditBadge consumed unchanged.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Frontend | Angular 21 + PrimeNG 21 | `<p-drawer>`, `<p-tag>` |
| TS contracts | NSwag | extended client |

### Testing requirements

- Standard integration + component + E2E coverage.

### Previous Story Intelligence

**Prerequisites:**
- Story 5.3 — AuditBadge component
- Story 5.5 — audit-trail-formatter (extended in 6.4)
- Story 6.4 — Exchange writes audit metadata columns (this story READS them)
- Story 6.7 — `journal_entries.locked_rate_id` FK

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.5
- FR20 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- AuditBadge spec — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §11
- Story 6.4 Exchange — [_bmad-output/implementation-artifacts/6-4-exchange-operation-with-locked-rate-and-user-override.md](./6-4-exchange-operation-with-locked-rate-and-user-override.md)
- Story 6.7 historical storage — [_bmad-output/implementation-artifacts/6-7-historical-rate-storage-for-audit.md](./6-7-historical-rate-storage-for-audit.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Read-side closure on FR20.
- AuditBadge consumed unchanged.
- User-override chip provides clear visual distinction.

### File List

_Populated during implementation._
