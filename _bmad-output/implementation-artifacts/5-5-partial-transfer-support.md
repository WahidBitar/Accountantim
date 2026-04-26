# Story 5.5: Partial-transfer support

Status: ready-for-dev

## Story

As **Karim (PRD Journey 2)**,
I want **to transfer $200 of a $500 debt while leaving $300 on the source contact**,
so that **I can handle the common real-world case where someone pays off part of their debt by assigning it elsewhere** (FR24 partial variant).

**Covers requirements:** Epic 5 Story 5.5 — FR24 partial-transfer variant; PRD Journey 2 (Karim transfers $200 of Ali's $500 debt to Faisal); same `ICompoundJournalEntry` infrastructure as Story 5.2 [Source: epics.md Epic 5 Story 5.5; prd.md FR24 + Journey 2].

**Why this story now:** Story 5.2 shipped the `amount` parameter (optional — null = full transfer). This story exercises the **partial path explicitly**: writes integration tests for partial-balance arithmetic, validates partial-equals-remaining handles correctly, and adds the UI affordance for entering a partial amount. Without an explicit story, the partial path could ship under-tested and silently produce off-by-cent rounding errors on subsequent transfers.

## Acceptance Criteria

### AC1 — Partial transfer reduces source remaining balance

**Given** Ali owes Karim $500 (single debt)
**When** Karim executes a partial transfer of $200 to Faisal
**Then** Ali's remaining balance becomes $300 (asserted via `IDebtRepository.GetRemainingBalanceAsync`)
**And** the source-close leg writes a journal entry with `amount = 200, direction = opposite-of-source` (closes $200 of the balance)
**And** the source debt is NOT marked as closed (still has remaining $300)
**And** Faisal has a NEW debt of $200 with `correlation_id` matching the transfer
**And** bitemporal history records the source-close leg as a fresh insert (not a mutation of the original $500 entry) [Source: epics.md Epic 5 Story 5.5 AC1; PRD Journey 2].

### AC2 — Subsequent partial transfers further reduce remaining balance

**Given** Ali has a $300 remaining balance after the first $200 transfer
**When** Karim executes a second partial transfer of $100 to a third contact
**Then** Ali's remaining balance becomes $200
**And** all three legs (original $500 opener, first $200 close, second $100 close) share the original debt's chain via `IDebtRepository` query
**And** an integration test asserts the remaining-balance arithmetic across N=5 sequential partial transfers maintains exact precision (no cent-drift) [Source: epics.md Epic 5 Story 5.5 AC4].

### AC3 — Partial-equals-remaining closes the source debt entirely

**Given** Ali has a $300 remaining balance
**When** Karim executes a partial transfer of $300 (equals the remaining balance)
**Then** the operation succeeds (treated equivalently to a full transfer)
**And** Ali's remaining balance becomes $0
**And** the source debt's `is_closed` flag flips to true (or equivalent semantic — closure is a derived property of `remaining = 0`)
**And** subsequent attempts to transfer further from this source return RFC 9457 `400` with code `faktuboh.transfers.source_debt_closed` [Source: epics.md Epic 5 Story 5.5 AC5].

### AC4 — Partial amount > remaining is rejected

**Given** Ali has a $300 remaining balance
**When** Karim attempts a partial transfer of $400
**Then** the backend (Story 5.2 AC9) returns RFC 9457 `400` with code `faktuboh.transfers.amount_exceeds_balance` and JSON Pointer `/amount/amount`
**And** the frontend (Story 5.1 AC4) catches this client-side via inline validation BEFORE the request submits
**And** an integration test asserts both layers reject the case [Source: Story 5.2 AC9; Story 5.1 AC4].

### AC5 — UI: partial-amount input visible only when "partial" radio selected

**Given** Story 5.1 ships the radio: full | partial
**When** the user selects "partial"
**Then** the amount input becomes visible (default value = source's remaining balance — most common case is "all of what's left")
**And** when the user selects "full", the amount input hides (and any value clears)
**And** the radio selection is preserved if the user toggles back-and-forth (no field-state loss)
**And** Story 5.1's existing inline validation (AC4) catches `amount > remaining` cases [Source: Story 5.1 AC2 + AC4].

### AC6 — Money-helper precision under partial arithmetic

**Given** the dual-precision rule (architecture §5.2.5) keeps user-facing amounts at `numeric(19,4)` and Money operations use banker's rounding
**When** sequential partial transfers reduce a balance
**Then** every intermediate balance is the exact decimal arithmetic result (no IEEE-754 drift)
**And** an FsCheck property test asserts: for any starting balance B and any sequence of partial transfers t₁, t₂, ..., tₙ where Σtᵢ ≤ B, the final remaining balance equals B − Σtᵢ exactly
**And** the test runs across all currencies in `CurrencyRegistry` (EUR, USD, GBP, AED, SAR, EGP, JOD, KWD, BHD, TND) including 3-decimal currencies (JOD, KWD, BHD, TND) [Source: architecture.md §5.2.5; Story 2.1 Money round-trip test pattern].

### AC7 — Idempotency-Key holds across partial submissions

**Given** Story 5.2 AC6 enforces idempotency via the `Idempotency-Key` middleware
**When** Karim double-taps Confirm on a partial transfer
**Then** only one transfer executes; the second blocks on the row-level lock and returns the cached response
**And** the cached response shows the SAME `correlationId`, `sourceJournalEntryId`, `targetJournalEntryId` (no second transfer accidentally created) [Source: Story 5.2 AC6].

### AC8 — Audit trail line on debt detail shows partial amount

**Given** Story 3.4 ships the per-contact history view + Story 3.8 ships the user-visible audit trail
**When** the audit trail renders a partial transfer event
**Then** the line reads (Transloco-keyed): "Transferred $200 to Faisal — remaining $300" (AR equivalent)
**And** the line links to the target debt via the same AuditBadge mechanism (Story 5.3)
**And** for full transfers, the line reads: "Transferred $500 to Faisal — fully closed"
**And** the helper that distinguishes the two cases lives in `libs/core/audit-trail-formatter.ts` (single-source) [Source: Story 3.8 audit-trail surface].

### AC9 — Partial transfers shown in Story 5.4 history view with correct amount

**Given** Story 5.4 ships `/transfers` history list
**When** partial transfers appear
**Then** the row shows the **transferred amount** (e.g., "$200"), NOT the source's original total ($500)
**And** an additional column or inline annotation shows "(partial)" badge per UX-DR15-style convention
**And** an integration test asserts the row data shape matches AC9 [Source: Story 5.4 AC1].

### AC10 — Telemetry differentiates partial from full

**Given** Story 5.2 AC10 emits `transfer_executed` with `is_partial: bool`
**When** a partial transfer fires
**Then** the event includes `is_partial: true`
**And** for full transfers, `is_partial: false`
**And** the dashboard panel "Partial vs Full Transfer Ratio" can be added to App Insights (out-of-scope deliverable; field is exposed for future analytics) [Source: Story 5.2 AC10].

## Tasks / Subtasks

### Backend — Verification + extension

- [ ] **T1 (AC: 1, 3)** — Verify Story 5.2's handler correctly routes partial vs full
  - [ ] Re-read `TransferDebtHandler.Handle(...)` for the `request.amount ?? remaining` branch
  - [ ] Add explicit unit test: `partial-equals-remaining` fixture confirms source closure
- [ ] **T2 (AC: 3)** — Add `faktuboh.transfers.source_debt_closed` error code to `ErrorCatalog`
  - [ ] Status 400, transloco_key, externally visible
  - [ ] Extend `TransferDebtRequestValidator` to check the source debt's open/closed state at validation time
- [ ] **T3 (AC: 1, 2)** — Verify `IDebtRepository.GetRemainingBalanceAsync` correctly sums all `journal_entries` rows by debt's contact + denomination
  - [ ] Add integration test with N=5 sequential partial transfers
  - [ ] Assert exact-precision arithmetic across all currencies in `CurrencyRegistry`

### Backend — Tests (the meat of this story)

- [ ] **T4 (AC: 1, 2, 6)** — Property-based test in `tests/Faktuboh.Domain.Tests/JournalEntries/Transfer/PartialTransferPropertyTests.cs`
  - [ ] FsCheck generators for `(startingBalance, partialAmounts[])`
  - [ ] Property: `B − Σtᵢ = remainingAfter` exactly for any valid partial sequence
  - [ ] Across all currencies (2-decimal + 3-decimal denominations)
  - [ ] 1000 generated cases per currency
- [ ] **T5 (AC: 1, 2)** — Integration test `PartialTransferSequenceTests`
  - [ ] Seed source debt of $500
  - [ ] Execute partial $200 → assert remaining = $300
  - [ ] Execute partial $100 → assert remaining = $200
  - [ ] Execute partial $200 → assert remaining = $0 + source closed
  - [ ] Attempt partial $50 on closed source → assert 400 `source_debt_closed`
- [ ] **T6 (AC: 4)** — Integration test for partial > remaining rejection
  - [ ] Already covered by Story 5.2 AC9; add explicit case here for partial-on-already-reduced-balance
- [ ] **T7 (AC: 7)** — Integration test for Idempotency-Key with partial transfer (concurrent double-submit)

### Frontend — Form refinement

- [ ] **T8 (AC: 5)** — Verify Story 5.1's transfer-form correctly shows/hides amount input
  - [ ] Add Vitest test asserting amount input visibility tied to radio
  - [ ] Default value = source remaining when partial selected
  - [ ] Radio toggle preserves field state
- [ ] **T9 (AC: 8)** — Audit-trail formatter helper
  - [ ] Create `libs/core/audit-trail-formatter.ts` with function `formatTransferAuditLine(transfer, locale): string`
  - [ ] Branches on `is_partial` for the two copy variants
  - [ ] Transloco keys: `audit.transfer.partial`, `audit.transfer.full`
  - [ ] Used in Story 3.8 audit trail surface + Story 3.4 per-contact history

### Frontend — History view extension

- [ ] **T10 (AC: 9)** — Add "(partial)" annotation to Story 5.4 history rows
  - [ ] Modify `transfer-history-page.component.html` to show inline badge when `is_partial === true`
  - [ ] Transloco key `transfers.history.partial-badge`
  - [ ] Add Playwright test asserting partial rows show the badge

### Documentation

- [ ] **T11 (AC: 6)** — Add note to `docs/development/dual-precision-rule.md` (or money helper readme)
  - [ ] Example: "Partial transfers across N=5 sequence preserve exact precision under banker's rounding"

## Dev Notes

### Critical guardrails (do not violate)

1. **Partial transfer is NOT a separate code path.** It's the same `TransferDebtHandler` from Story 5.2 with `request.amount` set. This story's value is primarily in TESTING the partial path explicitly.
2. **Partial-equals-remaining triggers source closure.** The handler treats this case identically to a full transfer (`amount = remaining`). The integration test asserts.
3. **No raw arithmetic on Money.** Sequential partial transfers exercise `addMoney`/`subtractMoney` helpers; raw `decimal -` operations are forbidden by analyzer.
4. **Banker's rounding (`MidpointRounding.ToEven`) explicit.** The FsCheck property test catches drift at scale.
5. **Source closure is a DERIVED property.** `is_closed = (remaining == 0)`. There's no separate "close debt" command; closure happens organically when balance hits zero. This keeps the bitemporal model clean (no separate state-mutation event).
6. **All currencies tested.** 2-decimal (USD, EUR) AND 3-decimal (JOD, KWD, BHD, TND). Cent vs fil/dirham precision differs.
7. **Frontend default partial amount = source remaining.** Most common case is "transfer all of what's left to a new party." Default reflects user intent.
8. **`(partial)` badge is non-interactive copy.** It's not the AuditBadge component (which is for navigation). It's a `<p-tag>` annotation per UX convention.

### Source tree — files to create or touch

```
backend/src/
└── Faktuboh.Application/Errors/
    └── ErrorCatalog.cs                                    # MODIFY (+ source_debt_closed code)

backend/tests/
├── Faktuboh.Domain.Tests/JournalEntries/Transfer/
│   └── PartialTransferPropertyTests.cs                    # NEW (FsCheck)
└── Faktuboh.Api.Tests/Slices/JournalEntries/Transfer/
    └── PartialTransferSequenceTests.cs                    # NEW (Integration)

apps/owner-workspace/
├── src/app/features/transfers/transfer-history/
│   └── transfer-history-page.component.html               # MODIFY (partial badge)
├── src/app/features/transfers/transfer-form/
│   └── transfer-form.component.spec.ts                    # MODIFY (radio toggle tests)
└── src/assets/i18n/{ar,en}.json                           # MODIFY (transfers.history.partial-badge, audit.transfer.{partial,full})

libs/core/
└── audit-trail-formatter.ts                               # NEW (helper for Stories 3.4/3.8 + 5.4)

docs/development/
└── dual-precision-rule.md                                 # MODIFY (partial transfer note)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Property-based | Sequential partial precision (1000 × N currencies) | FsCheck |
| Integration | Partial sequence + closure + idempotency | Testcontainers Postgres |
| Unit/Component | Form radio toggle + amount visibility | Vitest |
| E2E | Partial badge visible in history | Playwright |

### Architecture compliance

- **FR24** — partial-transfer variant.
- **§5.2.5** — Money + dual-precision (no precision loss across N partials).
- **Banker's rounding** — `MidpointRounding.ToEven` explicit.
- **NFR-P4** — handler still meets p95 < 500ms for partial path.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Test | FsCheck | Property-based for precision |
| Test | xUnit v3 + Testcontainers | Integration |
| Frontend | Vitest + Angular Testing Library + Playwright | |

### Testing requirements

- **Property:** N=5 partial sequence preserves exact precision across all currencies.
- **Integration:** partial → partial-equals-remaining → closure → reject-on-closed sequence.
- **Unit:** form radio + amount visibility.
- **E2E:** partial badge in history view.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.15 — `ICompoundJournalEntry` + balance assertion
- Story 2.1 — Money round-trip test pattern (template for FsCheck)
- Story 3.1 — Record New Debt + `IDebtRepository`
- Story 3.4 — per-contact history (audit-trail-formatter consumer)
- Story 3.8 — user-visible audit trail (audit-trail-formatter consumer)
- Story 5.1 — Transfer UI (radio + amount input)
- Story 5.2 — Transfer backend handler + Idempotency-Key
- Story 5.3 — AuditBadge
- Story 5.4 — Transfer history view (partial badge added here)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 5 Story 5.5
- FR24 + PRD Journey 2 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Money + dual-precision — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- Story 5.2 backend — [_bmad-output/implementation-artifacts/5-2-atomic-compound-journal-entry-transfer.md](./5-2-atomic-compound-journal-entry-transfer.md)
- Story 5.4 history view — [_bmad-output/implementation-artifacts/5-4-transfer-history-view.md](./5-4-transfer-history-view.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Partial transfer uses Story 5.2's handler unchanged; this story is primarily TESTING + UI affordance + audit-trail copy.
- Property-based test catches precision drift across N partials × all currencies.
- Source closure is derived (remaining = 0), not a separate command.

### File List

_Populated during implementation._
