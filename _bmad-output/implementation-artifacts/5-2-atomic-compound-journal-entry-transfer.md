# Story 5.2: Atomic compound-journal-entry transfer (AR-049 first real consumer)

Status: ready-for-dev

## Story

As **a developer**,
I want **the Transfer backend to use the Compound-Journal-Entry interface (AR-049) with the balance invariant asserted at commit**,
so that **source-closes-X + target-opens-X write atomically through the same seam Epic 6 will consume** (FR24 backend, FR25).

**Covers requirements:** Epic 5 Story 5.2 — FR24 (transfer all/part of debt as journal entry); FR25 (linked transactions with audit trail); AR-049 first real consumer of `ICompoundJournalEntry` (Story 0.15) [Source: epics.md Epic 5 Story 5.2; prd.md FR24, FR25; architecture.md AR-049].

**Why this story now:** Story 0.15 scaffolded `ICompoundJournalEntry` with a stub + balance-invariant fitness test. This story is the **first real implementer**. After this story lands, the interface becomes contractually frozen — Epic 6 Story 6.4 (Exchange) consumes it unchanged. If `ICompoundJournalEntry` is wrong, both Transfer and Exchange fail; getting it right here is load-bearing for Epic 6.

This is the Epic 5 analogue of Story 2.1: a deliberately-compound first slice that exercises the seam end-to-end (compound persister + balance assertion + bitemporal interceptor + same-transaction outbox + integration event + RFC 9457 + Idempotency-Key + telemetry).

## Acceptance Criteria

### AC1 — `POST /v1/journal-entries/transfer` endpoint with Idempotency-Key

**Given** an authenticated user submits a transfer request with `Idempotency-Key` header
**When** the endpoint executes
**Then** the request DTO `TransferDebtRequest` is validated:
- `sourceDebtId` (Guid, required, must exist + belong to authenticated user)
- `targetContactId` (Guid, required, must exist + belong to authenticated user, must NOT equal source-debt's contact)
- `amount` (Money, optional — null = full transfer of remaining balance)
- `note` (string, optional, max 280 chars)
**And** the handler invokes `ICompoundJournalEntry` with two `IJournalEntryLeg` instances:
1. Source close: `ContactId=sourceContactId`, `Amount=transferAmount`, `Direction=opposite-of-source-direction` (closes balance)
2. Target open: `ContactId=targetContactId`, `Amount=transferAmount`, `Direction=source-direction` (opens new balance)
**And** both legs share one `CorrelationId` (UUID v4 generated at handler entry)
**And** the response is `201 Created` with `TransferDebtResponse { correlationId, sourceClosedAt, targetOpenedAt, sourceJournalEntryId, targetJournalEntryId }` [Source: epics.md Epic 5 Story 5.2 AC; Story 0.15].

### AC2 — Both legs commit atomically (all-or-none)

**Given** the compound transfer writes through `ICompoundJournalEntryPersister` from Story 0.15
**When** the persister calls `SaveChangesAsync`
**Then** both `journal_entries` rows commit in a single Postgres transaction
**And** both `journal_entries_history` rows write via the `IBitemporal` interceptor
**And** the `TransferExecuted` outbox message enqueues in the same transaction (Wolverine same-transaction outbox per D4.12)
**And** if EITHER leg's write fails (FK violation, check constraint, etc.), the entire transaction rolls back — no partial state
**And** an integration test asserts the all-or-none invariant by injecting a fault into the second leg's write [Source: epics.md Epic 5 Story 5.2 AC; architecture.md §4.4 D4.12; Story 0.15 AC3].

### AC3 — Balance invariant asserted at commit time

**Given** `CompoundJournalEntryBalance.Assert(entry)` is called inside `CompoundJournalEntryPersister` per Story 0.15 AC2
**When** the two-leg transfer is balanced (source-close X = target-open X, same denomination)
**Then** `Assert` succeeds and the persister proceeds to `SaveChangesAsync`
**And** if a malformed transfer somehow constructs unbalanced legs (regression bug), `CompoundJournalEntryUnbalancedException` throws with code `faktuboh.journal_entries.compound_unbalanced` and HTTP 422
**And** an integration test deliberately constructs an unbalanced legs collection (test-only fixture) and asserts the exception path
**And** the cross-currency case is OUT OF SCOPE for Transfer — this story enforces same-denomination transfers; multi-denomination is Epic 6 Exchange's responsibility [Source: Story 0.15 AC2].

### AC4 — Bitemporal history rows written for both legs

**Given** both `journal_entries` rows are `IBitemporal` per Story 0.6 + 2.1
**When** the transfer commits
**Then** the source-debt journal entry's row is *not* updated; instead a *new* journal entry row (the source-close leg) is inserted with `correlation_id` matching the transfer
**And** the target-debt opening row is also a fresh insert with the same `correlation_id`
**And** when either leg is later edited or deleted (Stories 3.2/3.3 mechanism), the `IBitemporal` interceptor writes to `journal_entries_history`
**And** the `AsOf(t)` query (architecture §3.5) returns the pre-transfer view of both contacts when t < transfer commit timestamp [Source: architecture.md §3.5; Story 0.6 + 2.1; Story 0.15 AC3].

### AC5 — `TransferExecuted` integration event with `CorrelationId`

**Given** Wolverine outbox is wired per Story 2.1 + D4.12
**When** the transfer commits
**Then** `TransferExecutedEvent` is published with payload:
```csharp
public sealed record TransferExecutedEvent(
    Guid CorrelationId,
    Guid SourceContactId,
    Guid TargetContactId,
    Guid SourceJournalEntryId,
    Guid TargetJournalEntryId,
    Money Amount,
    string? Note,
    Guid OwnerSubjectId,
    DateTimeOffset ExecutedAt
);
```
**And** the event is in `Faktuboh.Domain.Events.IntegrationEvents/` namespace (cross-slice-allowed)
**And** the event enqueues atomically with the DB writes (no enqueue on aborted transaction)
**And** a no-op `TransferExecutedHandler` projection stub in `Slices/Notifications/` (Epic 7 placeholder) consumes the event for fitness-test #3 validation [Source: architecture.md §4.4 D4.12; Story 2.1 cross-slice pattern].

### AC6 — Idempotency: concurrent double-submit returns single execution

**Given** the `Idempotency-Key` middleware (Story 0.5) is active on this endpoint
**When** two concurrent POST requests submit the same key (e.g., user double-tapped Confirm)
**Then** only one handler execution completes; the second blocks on the row-level lock on `idempotency_keys` and returns the cached response
**And** key-replay-after-success returns the same response with `returnedExisting: true`
**And** an integration test asserts the single-execution invariant under parallel POSTs [Source: Story 0.5 + Story 2.1 AC2 pattern].

### AC7 — Failure of either leg's write rolls back entire operation

**Given** atomic commit per AC2
**When** a fault is injected into the second leg's write (e.g., FK violation on a manually-corrupted target contact ID)
**Then** the transaction aborts
**And** zero rows persist (no source-close, no target-open, no `journal_entries_history`, no outbox enqueue)
**And** the response is RFC 9457 with appropriate error code (e.g., `faktuboh.journal_entries.target_contact_not_found` for the FK case)
**And** an integration test uses Testcontainers Postgres + a contrived fault to assert this [Source: epics.md Epic 5 Story 5.2 AC].

### AC8 — Validation: target ≠ source contact

**Given** the request has `targetContactId`
**When** validation runs
**Then** `TransferDebtRequestValidator` (FluentValidation) checks that the source debt's contact ID ≠ `targetContactId`
**And** failure returns RFC 9457 `400 Bad Request` with code `faktuboh.transfers.same_party` and JSON Pointer `/targetContactId`
**And** the validator is server-authoritative (UI client-side check is a hint, not a guard) [Source: Story 5.1 AC3 mirror; architecture.md §4.4 D4.9].

### AC9 — Validation: amount > remaining balance (partial case)

**Given** the request has `amount` set (partial transfer)
**When** the handler resolves the source debt's remaining balance
**Then** if `amount` > remaining, RFC 9457 `400 Bad Request` with code `faktuboh.transfers.amount_exceeds_balance` and JSON Pointer `/amount/amount`
**And** if `amount` < remaining, the source contact retains a partial balance (Story 5.5 covers this case explicitly)
**And** if `amount` == remaining or `amount` is null (full transfer), the source debt closes entirely
**And** if `amount` is in a different denomination than the source debt, RFC 9457 `400` with code `faktuboh.transfers.denomination_mismatch` (Epic 6 owns multi-denom; Transfer is single-denom only) [Source: epics.md Epic 5 Story 5.2 + 5.5; architecture.md §5.2.5].

### AC10 — Telemetry: `transfer_executed` event

**Given** AR-047 telemetry primitive is wired
**When** the transfer commits
**Then** `TelemetryEmitter.Emit("transfer_executed", { opaque_owner_id, source_contact_id_hash, target_contact_id_hash, amount_bucket, denomination, correlation_id_hash, is_partial: bool })` fires
**And** PII guard asserts no raw IDs, no names, no exact amounts
**And** integration-test helper `AssertEventEmitted("transfer_executed")` passes [Source: Story 0.14 telemetry primitive; Story 5.1 AC10 mirror].

### AC11 — Performance: p95 < 500ms per NFR-P4

**Given** the full POST → persist (2 legs + 2 history rows + outbox) → response flow
**When** measured against Testcontainers Postgres
**Then** p95 response time < 500ms
**And** integration test asserts via timing measurement [Source: prd.md NFR-P4; Story 2.1 AC10 pattern].

## Tasks / Subtasks

### Backend — Slice scaffold

- [ ] **T1 (AC: 1)** — Create vertical slice at `backend/src/Faktuboh.Api/Slices/JournalEntries/Transfer/`
  - [ ] `Transfer.Endpoints.cs` with `MapGroup("/v1/journal-entries/transfer").RequireAuthorization()` + `ProcessingActivity.OwnerLedger` filter
  - [ ] `TransferDebtRequest.cs` — sealed record per AC1 fields
  - [ ] `TransferDebtResponse.cs` — sealed record per AC1 + static factory `From(...)`
  - [ ] `TransferDebtRequestValidator.cs` — FluentValidation
  - [ ] `TransferDebtHandler.cs` — Wolverine handler with `Handle` method
  - [ ] Register in `Program.cs` via `app.MapJournalEntriesTransferEndpoints()`

### Backend — Domain layer

- [ ] **T2 (AC: 1, 2, 3)** — Create `TransferCompoundEntry` implementing `ICompoundJournalEntry`
  - [ ] `Faktuboh.Domain/JournalEntries/Transfer/TransferCompoundEntry.cs`
  - [ ] Static factory: `Create(Guid sourceContactId, Guid targetContactId, Money amount, Direction sourceDirection, DateTimeOffset effectiveAt, string? note)`
  - [ ] Constructor builds 2 legs with `Direction` flipped on the source-close leg
  - [ ] Single `CorrelationId` (UUID v4) shared across legs
  - [ ] `Validate()` calls `CompoundJournalEntryBalance.Assert(this)` (Story 0.15)
- [ ] **T3 (AC: 5)** — Create `TransferExecutedEvent` integration event
  - [ ] `Faktuboh.Domain.Events.IntegrationEvents/TransferExecutedEvent.cs`
  - [ ] Per AC5 payload shape

### Backend — Infrastructure

- [ ] **T4 (AC: 1, 4)** — Verify migration: `journal_entries.correlation_id` column exists per Story 0.15 T4
  - [ ] If absent, add migration `YYYYMMDDHHMMSS_AddCorrelationIdToJournalEntries.cs` per Story 0.15 contract
  - [ ] Index `ix_journal_entries_correlation_id`
- [ ] **T5 (AC: 9)** — Add domain query `IDebtRepository.GetRemainingBalanceAsync(Guid debtId)` returning `Money`
  - [ ] Reads from `journal_entries` summed by debt's `contact_id` + `denomination`
  - [ ] Handler uses this to resolve `amount` (when partial) or full balance (when null)

### Backend — Handler logic

- [ ] **T6 (AC: 1, 2, 3, 5, 6)** — Implement `TransferDebtHandler.Handle(...)`
  - [ ] Resolve source debt → get source contact ID + remaining balance + denomination
  - [ ] Validate target ≠ source (server-authoritative belt for AC8)
  - [ ] Resolve transfer amount (request.amount ?? remaining)
  - [ ] Construct `TransferCompoundEntry`
  - [ ] Inject `ICompoundJournalEntryPersister` (Story 0.15) + call `PersistAsync`
  - [ ] On success, publish `TransferExecutedEvent` via Wolverine outbox (same transaction)
  - [ ] Emit telemetry per AC10
  - [ ] Return `TransferDebtResponse`

### Backend — Error catalog

- [ ] **T7 (AC: 8, 9)** — Register error codes in `ErrorCatalog`
  - [ ] `faktuboh.transfers.same_party` (400, transloco_key, externally visible)
  - [ ] `faktuboh.transfers.amount_exceeds_balance` (400)
  - [ ] `faktuboh.transfers.denomination_mismatch` (400)
  - [ ] `faktuboh.transfers.target_contact_not_found` (404)
  - [ ] `faktuboh.transfers.source_debt_not_found` (404)
  - [ ] (re-uses Story 0.15's `faktuboh.journal_entries.compound_unbalanced` for AC3 path)

### Backend — Cross-slice projection stub

- [ ] **T8 (AC: 5)** — Create no-op `TransferExecutedHandler` in `Slices/Notifications/Projections/`
  - [ ] Logs receipt of event with correlation ID
  - [ ] Epic 7 replaces with real notification emission
  - [ ] Validates fitness test #3 (cross-slice via IntegrationEvents) against this slice

### Tests — Backend

- [ ] **T9 (AC: 1-11)** — Unit tests in `tests/Faktuboh.Api.Tests/Slices/JournalEntries/Transfer/`
  - [ ] `TransferDebtHandlerTests` — happy path + all RFC 9457 negative paths
  - [ ] `TransferDebtRequestValidatorTests` — FluentValidation rules
  - [ ] `TransferCompoundEntryTests` — leg construction + balance invariant
- [ ] **T10 (AC: 1-7, 11)** — Integration tests `[Trait("Category", "Integration")]`
  - [ ] `TransferEndpointTests` — full slice via `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine bus
  - [ ] Happy path: full transfer; both legs persisted; both history rows; `TransferExecutedEvent` consumed
  - [ ] Happy path: partial transfer (Story 5.5 deeper coverage)
  - [ ] Concurrent-double-submit Idempotency-Key test
  - [ ] Atomicity test: inject fault on second leg → assert zero rows
  - [ ] Validation paths: same-party, amount-exceeds-balance, denomination-mismatch
  - [ ] Bitemporal `AsOf(t)` query returns pre-transfer state
  - [ ] Timing: p95 < 500ms
- [ ] **T11 (AC: 5)** — Test-only `TransferExecutedHandler` asserts event shape (not just publication — full consumption per Murat's landmine pattern)
- [ ] **T12 (AC: 3)** — FsCheck property test on `CompoundJournalEntryBalance.Assert` against Transfer-shaped fixtures
  - [ ] For any balanced 2-leg same-currency Transfer, Assert succeeds
  - [ ] For any unbalanced 2-leg fixture, Assert throws

### Frontend — Generated client

- [ ] **T13 (AC: 1)** — Generate NSwag TS client for `POST /v1/journal-entries/transfer`
  - [ ] `libs/core/api/transfers.client.ts` with typed `executeTransfer(request): Observable<TransferDebtResponse>`
  - [ ] Story 5.1's `transfer-form.component` consumes this client
  - [ ] OpenAPI snapshot updated (Story 3.11 drift gate validates)

## Dev Notes

### Critical guardrails (do not violate)

1. **`ICompoundJournalEntry` is contractually FROZEN after this story merges.** Per AR-049 + Story 0.15 AC6, additions that break implementers require an ADR. Epic 6 Story 6.4 must compile against this interface unchanged.
2. **Same-denomination only.** Transfer is single-denom — source debt and target receive use the same `Money.Currency`. Multi-denomination transfers require Exchange (Epic 6). Validation enforces.
3. **Atomicity via single `SaveChangesAsync` transaction.** No multi-statement work outside the persister. The Wolverine outbox piggybacks on the same transaction.
4. **Balance assertion BEFORE `SaveChangesAsync`.** `CompoundJournalEntryBalance.Assert(entry)` runs first; failure throws and rolls back without DB work.
5. **No accounting jargon in user-facing error messages.** Error code IDs use `faktuboh.transfers.*`; Transloco values use plain language ("Cannot transfer to same party", not "Source and target contacts must differ for double-entry compliance").
6. **Cross-slice via `IntegrationEvents` only.** `TransferExecutedHandler` lives in `Slices/Notifications/` and consumes only the event payload — never imports from `Slices/JournalEntries/Transfer/`.
7. **Bitemporal: legs are INSERTS, not UPDATES.** A transfer creates 2 new `journal_entries` rows with `correlation_id`; it does NOT mutate the existing source debt row. The remaining-balance calculation sums all legs via `AsOf(now())`.
8. **No in-memory EF Core provider.** Testcontainers Postgres for all integration tests (Wahid + Murat hard rule).
9. **Banker's rounding (`MidpointRounding.ToEven`) explicit on every `Math.Round`.** Per Story 2.1 guardrail.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Api/Slices/JournalEntries/Transfer/
│   ├── Transfer.Endpoints.cs                              # NEW
│   ├── TransferDebtRequest.cs                             # NEW
│   ├── TransferDebtResponse.cs                            # NEW
│   ├── TransferDebtRequestValidator.cs                    # NEW
│   ├── TransferDebtHandler.cs                             # NEW
│   └── TransferExceptionHandler.cs                        # NEW
├── Faktuboh.Api/Slices/Notifications/Projections/
│   └── TransferExecutedHandler.cs                         # NEW (stub)
├── Faktuboh.Domain/JournalEntries/Transfer/
│   └── TransferCompoundEntry.cs                           # NEW (implements ICompoundJournalEntry)
├── Faktuboh.Domain/Events/IntegrationEvents/
│   └── TransferExecutedEvent.cs                           # NEW
├── Faktuboh.Domain/Debts/  (or wherever IDebtRepository lives)
│   └── IDebtRepository.cs                                 # MODIFY (+ GetRemainingBalanceAsync)
├── Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDDHHMMSS_AddCorrelationIdToJournalEntries.cs  # CONDITIONAL (per Story 0.15 T4)
└── Faktuboh.Application/Errors/
    └── ErrorCatalog.cs                                    # MODIFY (+5 codes)

backend/tests/
├── Faktuboh.Api.Tests/Slices/JournalEntries/Transfer/
│   ├── TransferDebtHandlerTests.cs                        # NEW
│   ├── TransferDebtRequestValidatorTests.cs               # NEW
│   ├── TransferEndpointTests.cs                           # NEW (Integration)
│   └── TransferAtomicityTests.cs                          # NEW (Integration; fault-injection)
└── Faktuboh.Domain.Tests/JournalEntries/Transfer/
    ├── TransferCompoundEntryTests.cs                      # NEW
    └── TransferBalanceInvariantPropertyTests.cs           # NEW (FsCheck)

libs/core/api/
└── transfers.client.ts                                    # NEW (NSwag-generated)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Handler + validator + compound entry construction + balance | xUnit v3 + FluentAssertions + NSubstitute |
| Property | Balance invariant on Transfer-shaped fixtures | FsCheck |
| Integration | Full slice, atomicity, idempotency, bitemporal AsOf, timing | `WebApplicationFactory` + Testcontainers Postgres + real Wolverine |
| API contract | Verify snapshot of `TransferDebtRequest`/`TransferDebtResponse` JSON | Verify |
| Architectural | Cross-slice via IntegrationEvents (fitness #3) | NetArchTest |

**Forbidden:** Moq, EF Core in-memory, mocking the database, multi-denomination transfers (defer to Epic 6), `[JsonPropertyName]` outside RFC allowlist.

### Project Structure Notes

This story scaffolds the `JournalEntries/Transfer/` slice. Stories 5.4 (history) and 5.5 (partial) add sibling action folders. Epic 6 Story 6.4 adds `JournalEntries/Exchange/` — same parent slice, sibling action. The `JournalEntries` parent slice owns the `correlation_id` column + the compound-persister wiring; individual action folders contribute their domain models.

### Architecture compliance

- **AR-049** — first real consumer of `ICompoundJournalEntry`; freezes the contract.
- **ADR-022 + Story 0.6** — bitemporal interceptor.
- **D4.12** — Wolverine same-transaction outbox.
- **D4.9** — RFC 9457 + ErrorCatalog.
- **D4.11** — Idempotency-Key.
- **§5.2.5** — Money same-denomination invariant for Transfer.
- **§5.3.1** — DTO/handler naming (`TransferDebtRequest`, `TransferDebtHandler`).
- **§5.3.2** — slice-internal organization (per-action folder).
- **NFR-P4** — p95 < 500ms.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| .NET | 10 LTS | |
| Minimal APIs | First-party | `MapGroup`, `TypedResults` |
| Validation | FluentValidation | |
| Messaging | Wolverine | Postgres outbox, same-transaction |
| ORM | EF Core 10 | `IBitemporal` interceptor |
| DB | Postgres (Npgsql) | `numeric(19,4)` for amount |
| Test | xUnit v3 + FluentAssertions + NSubstitute + FsCheck + Testcontainers + Verify | |
| TS contracts | NSwag | Generated `transfers.client.ts` |

### Testing requirements

**Murat's false-confidence landmines applied here:**
1. EF Core in-memory — banned (interceptor differs).
2. Idempotency without concurrency — concurrent-double-submit test required.
3. IntegrationEvent without consumption — `TransferExecutedHandler` test asserts shape.
4. Money rounding without explicit `MidpointRounding.ToEven` — pinned.
5. Bitemporal assertion only on current row — assert history rows + AsOf time-travel.
6. Multi-denomination "by accident" — validator enforces same-denom.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 0.5 — Idempotency-Key middleware
- Story 0.6 — `IBitemporal` interceptor
- Story 0.15 — `ICompoundJournalEntry` interface seam + `CompoundJournalEntryPersister` + `CompoundJournalEntryBalance.Assert`
- Story 0.14 — telemetry primitive
- Story 2.1 — Contacts/Create (slice scaffold pattern)
- Story 3.1 — Record New Debt (`journal_entries` table established; `IDebtRepository` exists)
- Story 5.1 — Transfer UI (consumes the endpoint this story ships)

**Coordination notes:**
- This story FREEZES `ICompoundJournalEntry`. Any breaking change after this merge requires an ADR + sign-off.
- Epic 6 Story 6.4 implements `ExchangeCompoundEntry` against the same interface unchanged.
- Story 5.3 (AuditBadge) consumes the `correlation_id` linkage between source and target journal entries.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 5 Story 5.2
- AR-049 + Story 0.15 — [_bmad-output/implementation-artifacts/0-15-compound-journal-entry-interface-seam.md](./0-15-compound-journal-entry-interface-seam.md)
- ADR-022 bitemporal — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5
- Wolverine same-transaction outbox — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.4 D4.12
- RFC 9457 + ErrorCatalog — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.4 D4.9
- Money + same-denomination — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- FR24, FR25, NFR-P4 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 5.1 UI — [_bmad-output/implementation-artifacts/5-1-transfer-flow-ui-with-mandatory-review-dialog.md](./5-1-transfer-flow-ui-with-mandatory-review-dialog.md)
- Story 5.3 AuditBadge — [_bmad-output/implementation-artifacts/5-3-auditbadge-component-with-bidirectional-navigation.md](./5-3-auditbadge-component-with-bidirectional-navigation.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- AR-049 first real consumer; `ICompoundJournalEntry` is contractually frozen after this merge.
- Atomicity via single `SaveChangesAsync` + Wolverine same-transaction outbox.
- Same-denomination only; multi-denom is Epic 6 Exchange.
- Pattern that Story 6.4 (Exchange) inherits unchanged.

### File List

_Populated during implementation._
