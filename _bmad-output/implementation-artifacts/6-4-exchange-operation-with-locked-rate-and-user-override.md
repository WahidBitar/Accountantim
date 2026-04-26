# Story 6.4: Exchange operation with locked rate + user override (AR-049 second consumer)

Status: ready-for-dev

## Story

As **Nadia (PRD Journey 5)**,
I want **to convert a 10g gold debt to USD at $95/g (overriding the system rate of $92.50/g) with the rate locked to this transaction forever**,
so that **Hassan's repayment in dollars is recorded correctly and the original denomination is preserved in the audit trail** (FR19, FR21).

**Covers requirements:** Epic 6 Story 6.4 — FR19 (exchange operation with rate auto-populate + user override + locked rate); FR21 (original denomination preserved in audit); AR-049 second consumer of `ICompoundJournalEntry` (consumed UNCHANGED from Story 5.2's freeze) [Source: epics.md Epic 6 Story 6.4; prd.md FR19, FR21].

**Why this story now:** Stories 6.1–6.3 + 6.6 + 6.7 ship the rate-feed foundation + denomination integrity guard. This story is the **AR-049 second consumer** — the second real implementation of `ICompoundJournalEntry` (after Story 5.2 Transfer). Critically, this story consumes the interface UNCHANGED. If the interface needs adjustment for Exchange, that's a regression in Story 5.2's freeze and triggers an ADR.

This is the Exchange analogue of Story 5.2: same compound-persister + balance-invariant + bitemporal interceptor + outbox + Idempotency-Key pattern, but with **two currencies** (cross-denomination) and a **locked rate** referenced via `journal_entries.locked_rate_id` (Story 6.7).

## Acceptance Criteria

### AC1 — `POST /v1/journal-entries/exchange` endpoint

**Given** an authenticated user submits an exchange request with `Idempotency-Key` header
**When** the endpoint executes
**Then** the request DTO `ExchangeDebtRequest` is validated:
- `sourceDebtId` (Guid, required, must exist + belong to user)
- `targetDenomination` (string, required, must be in `CurrencyRegistry`, must differ from source's denomination)
- `lockedRate` (decimal, required, > 0, `numeric(28,8)` precision) — the user-confirmed rate at which conversion happens
- `lockedRateSource` (`'system' | 'user-override'`, required) — provenance flag
- `systemRateAtTime` (decimal, optional, populated only when `lockedRateSource = user-override` for audit)
- `note` (string, optional, max 280 chars)
**And** the handler invokes `ICompoundJournalEntry` (UNCHANGED from Story 5.2's freeze) with TWO legs:
1. Source close: `Amount=sourceRemainingBalance, Currency=sourceCurrency, Direction=opposite-of-source`
2. Target open: `Amount=convertedAmount, Currency=targetCurrency, Direction=source-direction` where `convertedAmount = sourceRemainingBalance * lockedRate` (rounded per target's minor units, banker's rounding)
**And** both legs share one `CorrelationId`
**And** both legs reference the same `locked_rate_id` (the `rates.id` from Story 6.7)
**And** the response is `201 Created` with `ExchangeDebtResponse { correlationId, sourceClosedAt, targetOpenedAt, sourceJournalEntryId, targetJournalEntryId, lockedRate, lockedRateSource, originalDenomination, derivedDenomination }` [Source: epics.md Epic 6 Story 6.4 AC; AR-049; Story 5.2 first consumer].

### AC2 — Exchange uses `ICompoundJournalEntry` UNCHANGED

**Given** Story 5.2 froze the `ICompoundJournalEntry` contract
**When** this story implements `ExchangeCompoundEntry : ICompoundJournalEntry`
**Then** the implementer references the interface from `Faktuboh.Domain.JournalEntries.ICompoundJournalEntry` directly
**And** zero modifications are made to the interface (no new properties, no new methods)
**And** `CompoundJournalEntryPersister` (Story 0.15) is reused unchanged
**And** `CompoundJournalEntryBalance.Assert` (Story 0.15) is invoked at commit
**And** an integration test asserts: a successful Exchange uses the SAME persister type as a successful Transfer (no parallel persister exists)
**And** if the interface DID need a change, this story is BLOCKED + an ADR + Story 5.2 amendment + Story 0.15 amendment are required [Source: AR-049; Story 0.15 AC6; Story 5.2 AC2].

### AC3 — Balance invariant for cross-currency: per-currency, sum = 0

**Given** `CompoundJournalEntryBalance.Assert` checks sum=0 PER CURRENCY (Story 0.15 AC2)
**When** the Exchange creates 2 legs in 2 currencies
**Then** for currency `XAU`: source-close leg has -10g, sum = -10g — FAILS the per-currency invariant
**And** to fix: the implementation includes the locked rate in the BALANCE math — Exchange does NOT pretend cross-currency legs balance to zero per currency; instead, the persister (or this story's wrapper) treats cross-currency Exchange as a special case where the invariant is "value is preserved at locked rate" not "per-currency sum is zero"
**And** decision flag: there are TWO valid implementations:
- **Option A:** `ExchangeCompoundEntry` adds two MORE pivot legs (e.g., XAU↔USD pivot via the locked rate) to balance per-currency. Pro: keeps the invariant unchanged. Con: 4 legs per Exchange (clutters audit).
- **Option B:** `CompoundJournalEntryBalance.Assert` gains an exception: when ALL legs share the same `locked_rate_id`, the per-currency check is replaced with `valuePreservedAtLockedRate` check.
**And** the chosen approach (default: Option A — pivot legs) MUST be documented in `docs/development/exchange-balance-invariant.md`
**And** the Story 0.15 contract is preserved either way (Option A doesn't touch the interface; Option B requires Story 0.15 amendment) [Source: Story 0.15 AC2; this story].

**Decision flag for Wahid:** Option A keeps Story 0.15's invariant pristine. Option B requires Story 0.15 amendment but produces cleaner 2-leg Exchange entries. Default to Option A unless Wahid prefers Option B.

### AC4 — Original denomination preserved in audit row

**Given** FR21 mandates original denomination preservation
**When** the Exchange commits
**Then** both legs' `journal_entries` rows include metadata:
- `original_denomination` (text, e.g., `"XAU"`)
- `original_amount numeric(19,4)` (the source amount BEFORE conversion)
- `derived_denomination` (text, e.g., `"USD"`)
- `derived_amount numeric(19,4)` (the target amount AFTER conversion)
- `locked_rate_id uuid` (FK to `rates.id` from Story 6.7)
- `locked_rate_source text` (`'system'` | `'user-override'`)
- `system_rate_at_time numeric(28,8) NULL` (for `user-override` only)
**And** these columns are added by migration `YYYYMMDDHHMMSS_AddExchangeMetadataToJournalEntries.cs`
**And** the columns are NULL for non-Exchange journal entries (Record-Debt, Transfer)
**And** an integration test asserts the audit row preservation [Source: epics.md Epic 6 Story 6.4 AC; FR21; Story 6.7 AC6].

### AC5 — Audit-trail line format per PRD Journey 5

**Given** PRD Journey 5 specifies the audit line format
**When** the audit trail (Story 3.8) renders an Exchange event
**Then** the line reads exactly:
- System rate used: "Exchange: 10g gold → $925 USD at $92.50/g (system rate)"
- User-overridden: "Exchange: 10g gold → $950 USD at $95/g (user-adjusted from system rate $92.50/g)"
**And** the helper that builds this line is in `libs/core/audit-trail-formatter.ts` (extends Story 5.5's helper) with function `formatExchangeAuditLine(exchange, locale): string`
**And** Transloco keys `audit.exchange.system-rate` + `audit.exchange.user-override` carry locale variants
**And** an integration test asserts the rendered string matches the format [Source: epics.md Epic 6 Story 6.4 AC; prd.md Journey 5].

### AC6 — UI: Exchange form with rate display + override + lock dialog

**Given** Story 6.3 returns the current rate
**When** the user opens the Exchange form (drawer-style on a debt detail page)
**Then** the form fields:
1. Source debt context (read-only, pre-populated)
2. Target denomination (`<p-dropdown>` filtered to currencies compatible with source per UX-DR10 — "DenominationPicker limited variant")
3. System rate display: "1g gold = $92.50 today" (auto-populated from Story 6.3)
4. User override: optional `<p-inputNumber>` for custom rate
5. Computed converted amount: live preview "10g gold = $925 USD (at $92.50/g)" — updates as user types override
6. Optional note
**And** tapping "Continue" opens a PrimeNG `<p-dialog modal="true" closable="false">` with:
- Header: "Lock exchange rate"
- Body: "Lock 10g gold → $950 USD at $95/g? This rate stays with this exchange forever."
- Two buttons: "Lock & Confirm" (primary) + "Cancel" (text)
**And** Cancel returns to form preserving values
**And** Confirm submits via `POST /v1/journal-entries/exchange`
**And** this dialog is the SECOND allowed `<p-dialog>` (per Story 5.1 AC6 ESLint allowlist: Transfer + Exchange) [Source: ux-design-specification.md Secondary Flow Currency/Metal Exchange; Story 5.1 AC6].

### AC7 — `DenominationPicker limited` variant filters compatible targets

**Given** UX-DR10 specifies the `limited` variant
**When** the user opens the target denomination dropdown
**Then** the list shows only denominations supported by the rate-feed (intersect of `CurrencyRegistry` ∩ available rates for the source denomination)
**And** for source `XAU`, the dropdown lists USD, EUR, GBP, SAR, AED, TRY, EGP, JOD, KWD, BHD, TND, XAG (all gold-base targets)
**And** for source `USD`, the dropdown lists currencies derivable via gold pivot (same set, excluding USD itself)
**And** if a denomination has no rate available (e.g., feed degraded), the option is disabled with tooltip "Rate unavailable" [Source: ux-design-specification.md UX-DR10].

### AC8 — Idempotency + atomicity inherited from Story 5.2 pattern

**Given** Story 5.2 established the Idempotency-Key + atomic-commit + outbox pattern
**When** Exchange follows the same pattern
**Then** concurrent double-submit returns single execution (cached response)
**And** failure of either leg's write rolls back entire operation (all-or-none)
**And** `ExchangeExecutedEvent` enqueues atomically with the DB writes
**And** integration tests assert these invariants identically to Story 5.2 tests [Source: Story 5.2 AC2, AC6, AC7].

### AC9 — Bitemporal: legs are inserts; history rows on subsequent edits

**Given** the Exchange writes to `journal_entries` (which is `IBitemporal` per Story 0.6)
**When** the Exchange commits
**Then** both legs are FRESH inserts (not updates of pre-existing rows)
**And** subsequent edits (Stories 3.2/3.3) write to `journal_entries_history`
**And** `AsOf(t)` query (Story 0.6 + Story 3.5 consumers) returns pre-Exchange state for `t` < commit time

### AC10 — `ExchangeExecutedEvent` integration event

**Given** the cross-slice contract pattern from Story 5.2
**When** the Exchange commits
**Then** `ExchangeExecutedEvent` publishes via Wolverine outbox with payload:
```csharp
public sealed record ExchangeExecutedEvent(
    Guid CorrelationId,
    Guid OwnerSubjectId,
    Guid ContactId,
    Guid SourceJournalEntryId, Money SourceAmount,
    Guid TargetJournalEntryId, Money TargetAmount,
    decimal LockedRate, string LockedRateSource,
    Guid LockedRateId,
    DateTimeOffset ExecutedAt
);
```
**And** the event is in `Faktuboh.Domain.Events.IntegrationEvents/`
**And** a no-op `ExchangeExecutedHandler` projection stub in `Slices/Notifications/` consumes for fitness-test #3 validation [Source: Story 5.2 AC5].

### AC11 — Validation: target denomination differs from source

**Given** Exchange to same denomination is meaningless
**When** `targetDenomination == sourceCurrency`
**Then** RFC 9457 `400 Bad Request` with code `faktuboh.exchanges.same_denomination` and JSON Pointer `/targetDenomination`
**And** the validator is server-authoritative

### AC12 — Validation: `lockedRate > 0` and within sane bounds

**Given** the user might submit a fat-finger rate (e.g., $0.0001/g gold)
**When** validation runs
**Then** `lockedRate > 0` is enforced
**And** if `lockedRate` deviates from `systemRate` by > 50%, a soft-warning is added to the response (NOT a rejection — user override is allowed) but telemetry fires `exchange_rate_outlier` for review
**And** if `lockedRate` deviates by > 1000%, RFC 9457 `400` with code `faktuboh.exchanges.rate_implausible` (likely fat-finger)

### AC13 — Telemetry: `exchange_executed` + `exchange_rate_overridden`

**Given** AR-047 telemetry primitive
**When** Exchange fires
**Then** `exchange_executed` event with `{ opaque_owner_id, contact_id_hash, source_currency, target_currency, locked_rate_source, amount_bucket, correlation_id_hash }`
**And** when `lockedRateSource = user-override`, ALSO emit `exchange_rate_overridden` with `{ opaque_owner_id, system_rate, user_rate, deviation_pct, denomination_pair_hash }`
**And** PII guard validates [Source: Story 0.14].

### AC14 — Performance: p95 < 500ms per NFR-P4

**Given** the same NFR-P4 commitment as Story 5.2
**When** measured against Testcontainers Postgres
**Then** p95 response time < 500ms

### AC15 — AuditBadge consumed UNCHANGED for Exchange linkage

**Given** Story 5.3 froze `<du-audit-badge>` API including `kind="exchange-prior" | "exchange-derived"`
**When** the source-side debt detail renders post-Exchange
**Then** `<du-audit-badge kind="exchange-prior" [linkedRoute]="targetRoute" [label]="targetLabel" />` displays
**And** the target-side debt detail renders `<du-audit-badge kind="exchange-derived" [linkedRoute]="sourceRoute" [label]="sourceLabel" />`
**And** the component is consumed UNCHANGED [Source: Story 5.3 AC10].

## Tasks / Subtasks

### Backend — Slice scaffold

- [ ] **T1 (AC: 1)** — Create `backend/src/Faktuboh.Api/Slices/JournalEntries/Exchange/`
  - [ ] `Exchange.Endpoints.cs`
  - [ ] `ExchangeDebtRequest.cs`
  - [ ] `ExchangeDebtResponse.cs`
  - [ ] `ExchangeDebtRequestValidator.cs`
  - [ ] `ExchangeDebtHandler.cs`
  - [ ] Register in `Program.cs`

### Backend — Domain layer

- [ ] **T2 (AC: 2, 3)** — Create `Faktuboh.Domain/JournalEntries/Exchange/ExchangeCompoundEntry.cs`
  - [ ] Implements `ICompoundJournalEntry` UNCHANGED
  - [ ] Static factory: `Create(...)` per AC1 inputs
  - [ ] Per AC3 decision (default Option A: 4 legs with pivot)
  - [ ] Single `CorrelationId`
- [ ] **T3 (AC: 10)** — Create `ExchangeExecutedEvent` integration event

### Backend — Infrastructure migration

- [ ] **T4 (AC: 4)** — Create migration `YYYYMMDDHHMMSS_AddExchangeMetadataToJournalEntries.cs`
  - [ ] `original_denomination`, `original_amount`, `derived_denomination`, `derived_amount`, `locked_rate_source`, `system_rate_at_time` columns
  - [ ] `locked_rate_id` already added by Story 6.7
  - [ ] All columns nullable

### Backend — Handler logic

- [ ] **T5 (AC: 1, 2, 3, 4, 8)** — Implement `ExchangeDebtHandler.Handle(...)`
  - [ ] Resolve source debt → currency + remaining balance
  - [ ] Validate target ≠ source denomination + within bounds
  - [ ] Read current system rate via `IRateRepository.GetLatestAsync(...)`
  - [ ] Decide locked rate: user-provided OR system-fallback
  - [ ] Compute converted amount: `sourceAmount * lockedRate` (banker's rounding to target's minor units)
  - [ ] Construct `ExchangeCompoundEntry` (per AC3 decision)
  - [ ] Persist via `ICompoundJournalEntryPersister` (UNCHANGED from Story 0.15)
  - [ ] Publish `ExchangeExecutedEvent` via outbox (same transaction)
  - [ ] Emit telemetry per AC13

### Backend — Error catalog

- [ ] **T6 (AC: 11, 12)** — Register error codes
  - [ ] `faktuboh.exchanges.same_denomination` (400)
  - [ ] `faktuboh.exchanges.rate_implausible` (400)
  - [ ] `faktuboh.exchanges.target_denomination_unsupported` (400)
  - [ ] `faktuboh.exchanges.source_debt_not_found` (404)

### Backend — Cross-slice projection stub

- [ ] **T7 (AC: 10)** — Create no-op `ExchangeExecutedHandler` in `Slices/Notifications/Projections/`

### Frontend — Form + dialog

- [ ] **T8 (AC: 6, 7)** — Create `apps/owner-workspace/src/app/features/exchanges/exchange-form/exchange-form.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Selector `ow-exchange-form`
  - [ ] Lazy route `/contacts/:id/debts/:debtId/exchange`
  - [ ] Calls `getCurrentRate(from, to)` on target-denom change
  - [ ] Computed converted amount preview
  - [ ] DenominationPicker `limited` variant per UX-DR10
- [ ] **T9 (AC: 6)** — Create `exchange-rate-lock-dialog.component.ts`
  - [ ] PrimeNG `<p-dialog modal="true" closable="false">`
  - [ ] Cancel preserves form state
  - [ ] Confirm submits via NSwag-generated client
  - [ ] Allowlist update for `single-routine-confirmation-dialog` ESLint rule
- [ ] **T10 (AC: 15)** — Wire AuditBadge into source + target debt detail pages post-Exchange

### Frontend — Audit-trail formatter

- [ ] **T11 (AC: 5)** — Extend `libs/core/audit-trail-formatter.ts` with `formatExchangeAuditLine(exchange, locale)`
  - [ ] Branches on `lockedRateSource`
  - [ ] Transloco keys `audit.exchange.system-rate` + `.user-override`

### Tests — Backend

- [ ] **T12 (AC: 1-14)** — Unit tests in `tests/Faktuboh.Api.Tests/Slices/JournalEntries/Exchange/`
  - [ ] Handler happy path + validation negatives
  - [ ] Compound entry construction
- [ ] **T13 (AC: 1, 2, 3, 4, 8, 9, 10, 14)** — Integration tests
  - [ ] Full slice via Testcontainers + WireMock rates
  - [ ] Atomicity (fault injection on second leg)
  - [ ] Idempotency (concurrent double-submit)
  - [ ] Bitemporal AsOf time-travel
  - [ ] AR-049 regression: assert `CompoundJournalEntryPersister` is the SAME type used by Transfer
- [ ] **T14 (AC: 4, 5)** — Audit-row preservation tests
  - [ ] Assert all 6 metadata columns populated correctly

### Tests — Frontend

- [ ] **T15 (AC: 6, 7, 15)** — Vitest tests for form + dialog + DenominationPicker filtering
- [ ] **T16 (AC: 5, 6, 15)** — Playwright E2E
  - [ ] Full exchange flow with override
  - [ ] Audit-trail line format assertion
  - [ ] AuditBadge bidirectional navigation
  - [ ] axe-core a11y + RTL

## Dev Notes

### Critical guardrails (do not violate)

1. **`ICompoundJournalEntry` consumed UNCHANGED.** Per AR-049 + Story 5.2 freeze. If you find yourself wanting to add a property to the interface, STOP and trigger an ADR.
2. **Cross-currency balance invariant requires explicit handling.** Per AC3 — Option A (pivot legs) is the default. Option B (interface amendment) requires Story 0.15 + 5.2 amendments.
3. **Locked rate is FOREVER.** No code path may "auto-update" `locked_rate_id` after commit. Story 6.7's append-only `rates` table backstops this.
4. **Original denomination preserved.** The `original_denomination` + `original_amount` columns survive even if the legs are later edited (bitemporal).
5. **Banker's rounding when converting.** `MidpointRounding.ToEven` explicit on every `Math.Round` for the converted amount.
6. **The Exchange slice is the SOLE allowlisted exception** to the Story 6.6 NetArchTest cross-denomination rule.
7. **Rate-lock dialog is the SECOND allowed `<p-dialog>` in Faktuboh** (the first is Transfer review per Story 5.1). The ESLint rule `single-routine-confirmation-dialog` allowlist is updated here.
8. **Same-denom Exchange is rejected.** No-op operations are not allowed (validation enforces).
9. **Implausible rate (>1000% deviation) is rejected as fat-finger.** 50% deviation is allowed (real-world overrides happen) but telemetry fires for review.
10. **DenominationPicker `limited` variant** filters by available rates — degraded feed implies disabled options with tooltip.
11. **AuditBadge consumed UNCHANGED.** Per Story 5.3 single-owner rule. `kind="exchange-prior"` and `kind="exchange-derived"` were declared in Story 5.3.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Api/Slices/JournalEntries/Exchange/
│   ├── Exchange.Endpoints.cs                               # NEW
│   ├── ExchangeDebtRequest.cs                              # NEW
│   ├── ExchangeDebtResponse.cs                             # NEW
│   ├── ExchangeDebtRequestValidator.cs                     # NEW
│   └── ExchangeDebtHandler.cs                              # NEW
├── Faktuboh.Api/Slices/Notifications/Projections/
│   └── ExchangeExecutedHandler.cs                          # NEW (stub)
├── Faktuboh.Domain/JournalEntries/Exchange/
│   └── ExchangeCompoundEntry.cs                            # NEW (implements ICompoundJournalEntry UNCHANGED)
├── Faktuboh.Domain/Events/IntegrationEvents/
│   └── ExchangeExecutedEvent.cs                            # NEW
├── Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDDHHMMSS_AddExchangeMetadataToJournalEntries.cs  # NEW
└── Faktuboh.Application/Errors/
    └── ErrorCatalog.cs                                     # MODIFY (+4 codes)

apps/owner-workspace/
├── src/app/features/exchanges/exchange-form/
│   ├── exchange-form.component.ts                          # NEW
│   ├── exchange-form.component.html                        # NEW
│   ├── exchange-form.component.scss                        # NEW
│   └── exchange-form.component.spec.ts                     # NEW
├── src/app/features/exchanges/exchange-rate-lock-dialog/
│   ├── exchange-rate-lock-dialog.component.ts              # NEW
│   └── exchange-rate-lock-dialog.component.spec.ts         # NEW
├── src/app/features/exchanges/exchanges.routes.ts          # NEW
└── src/assets/i18n/{ar,en}.json                            # MODIFY (exchange.* + audit.exchange.* keys)

libs/core/
├── api/exchanges.client.ts                                 # NEW (NSwag-generated)
└── audit-trail-formatter.ts                                # MODIFY (+ formatExchangeAuditLine)

eslint-rules/
└── single-routine-confirmation-dialog.js                   # MODIFY (allowlist exchange-rate-lock-dialog)

backend/tests/
├── Faktuboh.Api.Tests/Slices/JournalEntries/Exchange/
│   ├── ExchangeDebtHandlerTests.cs                         # NEW
│   ├── ExchangeEndpointTests.cs                            # NEW (Integration)
│   └── ExchangeAR049RegressionTests.cs                     # NEW (asserts persister unchanged)
└── Faktuboh.Domain.Tests/JournalEntries/Exchange/
    └── ExchangeCompoundEntryTests.cs                       # NEW

docs/development/
└── exchange-balance-invariant.md                           # NEW (per AC3 decision)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Handler + validator + compound entry | xUnit + FluentAssertions + NSubstitute |
| Integration | Full slice + atomicity + idempotency + bitemporal + AR-049 regression | Testcontainers + Wolverine |
| Architectural | Persister type unchanged from Story 5.2 | NetArchTest |
| Component | Form + dialog + DenominationPicker filtering | Vitest |
| E2E | Full flow + audit format + AuditBadge nav | Playwright |

### Architecture compliance

- **AR-049** — second consumer of `ICompoundJournalEntry`; consumed UNCHANGED.
- **FR19, FR21** — locked rate + original denomination preservation.
- **§5.2.5** — `numeric(28,8)` for `locked_rate`; `numeric(19,4)` for amounts.
- **D4.12** — Wolverine same-transaction outbox.
- **Story 5.1 AC6 + Story 6.4 AC6** — Exchange dialog is the second allowed routine-confirmation dialog.
- **Story 5.3** — AuditBadge consumed unchanged.
- **Story 6.6** — Exchange slice is the SOLE allowlisted exception to the cross-denomination NetArchTest.
- **Story 6.7** — `journal_entries.locked_rate_id` consumed.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Same as Story 5.2 + UI deps | | |
| TS contracts | NSwag | exchanges.client.ts |

### Testing requirements

- **Unit + integration:** mirror of Story 5.2 testing rigor.
- **AR-049 regression:** assert no parallel persister exists; Story 0.15's persister is used.
- **Audit-trail format:** assert exact PRD-Journey-5 string format.
- **Component + E2E:** form, dialog, denomination filtering, audit badge navigation.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.15 — `ICompoundJournalEntry` interface (consumed unchanged)
- Story 5.2 — first consumer (interface frozen here)
- Story 5.3 — AuditBadge (`exchange-prior`/`exchange-derived` kinds)
- Story 5.5 — audit-trail-formatter (extended here)
- Story 6.1 — rates feed
- Story 6.3 — current rate endpoint
- Story 6.6 — denomination integrity NetArchTest (exception allowlist for this slice)
- Story 6.7 — `journal_entries.locked_rate_id` column + `IRateRepository.GetAtAsync`
- Story 5.1 — `single-routine-confirmation-dialog` ESLint allowlist (extended here)

**Coordination:**
- Story 6.5 reads the audit metadata columns this story creates.
- Story 6.8 (freshness banner) gates the Exchange button when rates > 24h stale.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.4
- FR19, FR21, PRD Journey 5 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Secondary Flow Currency/Metal Exchange — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md)
- AR-049 + ICompoundJournalEntry — [_bmad-output/implementation-artifacts/0-15-compound-journal-entry-interface-seam.md](./0-15-compound-journal-entry-interface-seam.md)
- Story 5.2 first consumer — [_bmad-output/implementation-artifacts/5-2-atomic-compound-journal-entry-transfer.md](./5-2-atomic-compound-journal-entry-transfer.md)
- Story 6.6 denomination integrity — [_bmad-output/implementation-artifacts/6-6-denomination-integrity-enforcement.md](./6-6-denomination-integrity-enforcement.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- AR-049 second consumer; `ICompoundJournalEntry` consumed UNCHANGED.
- Decision flag (AC3): Option A (pivot legs) keeps Story 0.15 invariant pristine; Option B (interface amendment) requires Stories 0.15 + 5.2 amendments. Default Option A.
- Original denomination + system rate preserved in audit metadata columns.
- Audit line format matches PRD Journey 5 exactly.
- Exchange slice is SOLE allowlisted exception to Story 6.6 NetArchTest.
- Rate-lock dialog is SECOND allowed routine-confirmation dialog (after Transfer).
- AuditBadge `exchange-prior`/`exchange-derived` kinds consumed unchanged.

### File List

_Populated during implementation._
