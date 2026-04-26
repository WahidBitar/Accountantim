# Story 0.15: Compound-Journal-Entry interface seam (AR-049)

Status: ready-for-dev

## Story

As a **developer**,
I want **a `ICompoundJournalEntry` Domain-layer abstraction representing multi-leg balanced atomic journal writes**,
so that **Epic 5 (Transfer) and Epic 6 (Exchange) consume a stable interface that decouples their ordering**.

**Covers requirements:** Epic 0 — AR-049. Compound-journal-entry interface seam. Epic 5 consumes first; Epic 6 consumes unchanged.

**Why this story now:** After Story 0.6 (bitemporal interceptor) — compound writes happen atomically within a `SaveChangesAsync` transaction. Before Story 0.16 (mutation testing) — Stryker.NET targets Domain, and this interface adds new Domain types worth mutating. Consumers (Epic 5 Story 5.2 — Transfer) ship long after Epic 0.

## Acceptance Criteria

### AC1 — `ICompoundJournalEntry` interface

**Given** Epic 5 + 6 both need multi-leg balanced writes
**When** this story scaffolds the interface
**Then** `backend/src/Faktuboh.Domain/JournalEntries/ICompoundJournalEntry.cs`:

```csharp
public interface ICompoundJournalEntry
{
    /// <summary>Unique correlation across all legs of this compound operation.</summary>
    Guid CorrelationId { get; }

    /// <summary>All journal-entry legs; must sum to zero per-denomination before commit.</summary>
    IReadOnlyList<IJournalEntryLeg> Legs { get; }

    /// <summary>Timestamp at which the compound operation is effective in the user's ledger.</summary>
    DateTimeOffset EffectiveAt { get; }
}

public interface IJournalEntryLeg
{
    Guid Id { get; }
    Guid ContactId { get; }
    Money Amount { get; }   // signed — negative = debit, positive = credit from user's perspective
    Direction Direction { get; }  // Receivable | Payable (from Story 0.1)
    string? Memo { get; }
}
```

[Source: epics.md Story 0.15 + architecture.md §3.5 compound-journal seam]

### AC2 — Balance invariant enforced at commit

**Given** compound entries must sum to zero per-denomination
**When** `ICompoundJournalEntry.Validate()` or a SaveChanges hook checks the invariant
**Then** `backend/src/Faktuboh.Domain/JournalEntries/CompoundJournalEntryBalance.cs`:

```csharp
public static class CompoundJournalEntryBalance
{
    public static void Assert(ICompoundJournalEntry entry)
    {
        var byCurrency = entry.Legs
            .GroupBy(l => l.Amount.Currency)
            .Select(g => (Currency: g.Key, Sum: g.Sum(l => l.Amount.Amount * (l.Direction == Direction.Payable ? -1m : 1m))))
            .ToArray();
        foreach (var (currency, sum) in byCurrency)
            if (sum != 0m)
                throw new CompoundJournalEntryUnbalancedException(entry.CorrelationId, currency, sum);
    }
}

public sealed class CompoundJournalEntryUnbalancedException(Guid correlationId, string currency, decimal imbalance)
    : DomainException(
        code: "faktuboh.journal_entries.compound_unbalanced",
        message: $"Compound journal entry {correlationId} has {imbalance} {currency} imbalance (expected 0).");
```

**And** the exception's error code is registered in `ErrorCatalog` with `httpStatus: 422` (Unprocessable Entity)

### AC3 — Atomic transaction wrapper

**Given** compound entries must commit all-or-none
**When** a compound entry is persisted
**Then** `backend/src/Faktuboh.Application/JournalEntries/CompoundJournalEntryPersister.cs`:

```csharp
public interface ICompoundJournalEntryPersister
{
    Task PersistAsync(ICompoundJournalEntry entry, CancellationToken ct = default);
}

public sealed class CompoundJournalEntryPersister(FaktubohDbContext db) : ICompoundJournalEntryPersister
{
    public async Task PersistAsync(ICompoundJournalEntry entry, CancellationToken ct = default)
    {
        CompoundJournalEntryBalance.Assert(entry);
        foreach (var leg in entry.Legs)
            db.Set<JournalEntry>().Add(JournalEntry.FromLeg(leg, entry.CorrelationId, entry.EffectiveAt));
        await db.SaveChangesAsync(ct);  // relies on EF Core implicit transaction
    }
}
```

**And** the `SaveChangesAsync` emits all legs + bitemporal history rows (Story 0.6 interceptor) in one transaction
**And** Wolverine outbox (Story 2.1+) writes any domain events atomically with the DB changes per D4.12

### AC4 — `CorrelationId` propagation

**Given** each leg carries a `CorrelationId` linking it to the compound operation
**When** legs are persisted
**Then** every `journal_entries` row has a `correlation_id uuid` column populated from `ICompoundJournalEntry.CorrelationId` (non-compound entries have `correlation_id = id`, i.e., their own id)
**And** an index `ix_journal_entries_correlation_id` supports replay queries per compound operation
**And** schema migration `YYYYMMDDHHMMSS_AddCorrelationIdToJournalEntries.cs` adds the column with `NOT NULL` constraint + default to `id` for existing rows [Source: epics.md Story 0.15]

### AC5 — Stub implementation with balance-invariant fitness test

**Given** AR-049 requires a stub implementation in Epic 0 with a fitness test validating the balance invariant
**When** this story lands
**Then** `backend/tests/Faktuboh.Domain.Tests/JournalEntries/CompoundJournalEntryBalanceTests.cs`:
- Test 1: balanced 2-leg entry (100 EUR debit + 100 EUR credit) → `Assert` succeeds
- Test 2: unbalanced 2-leg entry (100 EUR + 50 EUR credit same direction) → throws `CompoundJournalEntryUnbalancedException`
- Test 3: balanced N-leg entry (3 legs summing to zero across 2 currencies) → succeeds
- Test 4: FsCheck property — for any generator of balanced leg sets, `Assert` always succeeds
- Test 5: FsCheck property — for any generator of imbalanced leg sets, `Assert` always throws
**And** a stub `StubCompoundJournalEntry` test fixture exists for consumers to inherit

### AC6 — Epic 5 + 6 consumer contract documented

**Given** AR-049 decouples Epic 5→6 ordering
**When** consumers implement the interface
**Then** `docs/development/compound-journal-entry-guide.md` documents:
- How to implement `ICompoundJournalEntry` (Transfer, Exchange examples)
- When to use compound vs simple journal entries
- Balance invariant expectations per direction
- Epic 5 Transfer example: 2 legs (from-contact Payable, to-contact Receivable) same currency, sum = 0
- Epic 6 Exchange example: 4 legs (sell A Payable, buy A' Receivable, sell B Payable, buy B' Receivable) — two currencies, sum = 0 per currency (via pivot rate)
- Breaking-change policy: interface is frozen after Epic 5 Story 5.2 ships; additions require ADR

## Tasks / Subtasks

### T1 — Interface + leg record (AC: 1)

- [ ] Create `backend/src/Faktuboh.Domain/JournalEntries/ICompoundJournalEntry.cs` per AC1
- [ ] Create `backend/src/Faktuboh.Domain/JournalEntries/IJournalEntryLeg.cs`
- [ ] Ensure `Money` (Story 0.1) + `Direction` (Story 0.1) are referenced

### T2 — Balance assertion + exception (AC: 2)

- [ ] Create `backend/src/Faktuboh.Domain/JournalEntries/CompoundJournalEntryBalance.cs` per AC2
- [ ] Create `backend/src/Faktuboh.Domain/JournalEntries/CompoundJournalEntryUnbalancedException.cs`
- [ ] Register error code `faktuboh.journal_entries.compound_unbalanced` in `ErrorCatalog` (Story 0.4) with status 422

### T3 — Persistence abstraction (AC: 3, 4)

- [ ] Create `backend/src/Faktuboh.Application/JournalEntries/ICompoundJournalEntryPersister.cs` interface
- [ ] Create `backend/src/Faktuboh.Infrastructure/JournalEntries/CompoundJournalEntryPersister.cs` implementation
- [ ] Register in DI: `builder.Services.AddScoped<ICompoundJournalEntryPersister, CompoundJournalEntryPersister>();`

### T4 — Schema migration for correlation_id (AC: 4)

- [ ] Create migration `AddCorrelationIdToJournalEntries` — since `journal_entries` table may not yet exist (Story 2.1 creates it), this migration is a FORWARD-stamp: add `correlation_id uuid NOT NULL` + index `ix_journal_entries_correlation_id` to whatever migration Story 2.1 produces
- [ ] OR if `journal_entries` doesn't exist at story execution time (Story 0.6 < 0.15 ordering): fold `correlation_id` into the `JournalEntryConfiguration` Story 2.1 will write, and document the expectation in `docs/runbooks/migration-order.md`
- [ ] This story's AC4 is SATISFIED either way — the contract is that by Story 2.1 time, `journal_entries.correlation_id` is present

### T5 — Fitness tests (AC: 5)

- [ ] Create `backend/tests/Faktuboh.Domain.Tests/JournalEntries/CompoundJournalEntryBalanceTests.cs` with the 5 test cases from AC5
- [ ] Create `backend/tests/Faktuboh.Domain.Tests/JournalEntries/Fixtures/StubCompoundJournalEntry.cs` — test-only impl for consumer tests to inherit

### T6 — Documentation (AC: 6)

- [ ] Create `docs/development/compound-journal-entry-guide.md` with Epic 5 + 6 examples per AC6
- [ ] Add note: interface is frozen after Epic 5 Story 5.2 ships; changes require ADR

## Dev Notes

### Critical guardrails (do not violate)

1. **Interface is FROZEN after Epic 5 consumes.** Additions that break implementers require an ADR. AR-049 explicitly wants Epic 6 to consume unchanged. [Source: epics.md Story 0.15 + AR-049]
2. **Balance check is per-currency.** Cross-currency transfers require intermediate pivot legs (Epic 6 responsibility) — Epic 0's stub must not pretend to handle FX. [Source: epics.md Story 0.15 + Epic 6 scope]
3. **Direction enum values mean what they mean** — `Payable` (I owe them) = debit from my ledger; `Receivable` (they owe me) = credit. Getting this wrong inverts every balance. [Source: Story 0.1 Direction definition]
4. **`EffectiveAt` is not `RecordedAt`.** `EffectiveAt` is the user's wall-clock date for the operation; `RecordedAt` (from `IBitemporal`) is when the row was saved. A compound entry backdated to last month has `EffectiveAt` = last month, `RecordedAt` = now. [Source: architecture.md §5.2.4 date conventions]
5. **No PersistAsync on the interface itself.** Persistence is infra concern; interface is domain only.

### Source tree — files to create

```
backend/src/
├── Faktuboh.Domain/JournalEntries/
│   ├── ICompoundJournalEntry.cs                          # NEW
│   ├── IJournalEntryLeg.cs                               # NEW
│   ├── CompoundJournalEntryBalance.cs                    # NEW
│   └── CompoundJournalEntryUnbalancedException.cs        # NEW
├── Faktuboh.Application/JournalEntries/
│   └── ICompoundJournalEntryPersister.cs                 # NEW
└── Faktuboh.Infrastructure/JournalEntries/
    └── CompoundJournalEntryPersister.cs                  # NEW

backend/tests/Faktuboh.Domain.Tests/JournalEntries/
├── CompoundJournalEntryBalanceTests.cs                   # NEW (AC5)
└── Fixtures/
    └── StubCompoundJournalEntry.cs                       # NEW

docs/development/
└── compound-journal-entry-guide.md                       # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 + FluentAssertions + FsCheck | AC5 balance invariant |
| Architectural | NetArchTest | Interface lives in Domain (not Infrastructure) — covered by fitness test #1 from Story 0.8 |

### Project Structure Notes

- Interface lives in `Faktuboh.Domain` to keep Epic 5/6 consumers out of Infrastructure deps; persister implementation lives in `Faktuboh.Infrastructure`.
- `correlation_id` column merges into Story 2.1's first `journal_entries` migration if Story 0.15 ships before Story 2.1; otherwise it's an additive migration post-2.1.

### Architecture compliance

- [architecture.md §3.5](../planning-artifacts/architecture.md) — compound-journal-entry seam mention
- [epics.md Story 0.15 + AR-049](../planning-artifacts/epics.md) — definition
- [architecture.md §4 D4.12](../planning-artifacts/architecture.md) — Wolverine outbox (same-transaction guarantee)
- [architecture.md ADR-006](../planning-artifacts/architecture.md) — vertical-slice (Domain holds stable primitives)

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| Domain | (none — pure BCL) | Interface + exception |
| Infrastructure | Microsoft.EntityFrameworkCore | `SaveChangesAsync` transaction |
| Tests | FsCheck.Xunit | Property-based balance tests |

### Testing requirements

- **FsCheck property tests for balance invariant** are MANDATORY per AC5 — randomized test data catches edge cases that hand-written tests miss.
- **Epic 5 Story 5.2 + Epic 6 consumer tests** will validate this interface end-to-end against real transfer / exchange flows. Those tests are out of Story 0.15 scope.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: `Money` VO + `Direction` enum exist.
- **Story 0.4 (RFC 9457)** complete: `DomainException` + `ErrorCatalog` exist.
- **Story 0.6 (bitemporal interceptor)** complete: `FaktubohDbContext` + SaveChangesAsync transaction semantics work.
- **Story 2.1 (Contacts/Create)** — creates the first `journal_entries` table migration; `correlation_id` column must be included in that migration (ordering: 0.15 context influences 2.1's migration shape).
- **Epic 5 Story 5.2 (Transfer)** — first real consumer; validates the interface end-to-end.
- **Epic 6 (Exchange)** — second consumer; must compile against this interface unchanged.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.15 definition + AR-049
- [_bmad-output/planning-artifacts/architecture.md §3.5](../planning-artifacts/architecture.md) — compound-journal seam
- [_bmad-output/planning-artifacts/architecture.md §4 D4.12](../planning-artifacts/architecture.md) — Wolverine same-transaction outbox

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
