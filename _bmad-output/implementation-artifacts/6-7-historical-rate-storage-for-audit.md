# Story 6.7: Historical rate storage for audit

Status: ready-for-dev

## Story

As **Wahid**,
I want **every fetched rate stored with `valid_from` + `source_provider` so that any past exchange can be audited against the rate that was actually used**,
so that **disputes can be resolved against immutable rate history** (FR22).

**Covers requirements:** Epic 6 Story 6.7 — FR22 (historical rate storage for reference + audit); rates append-only contract; 7-year retention [Source: epics.md Epic 6 Story 6.7; prd.md FR22].

**Why this story now:** Story 6.1 forward-stamped most of this story's contract (table shape + columns + Postgres trigger blocking UPDATE/DELETE). This story is primarily a **verification + retention-policy + query-perf** story:
- Asserts the append-only invariant survives in production (not just in tests).
- Adds the 7-year retention policy + archive process (deferred-implementation stub for now).
- Adds the indexed historical-lookup query path needed by Story 6.5 (view historical exchange rate).
- Documents the audit contract.

## Acceptance Criteria

### AC1 — `rates` table shape includes all required audit columns

**Given** Story 6.1 created the `rates` table
**When** this story verifies the schema
**Then** the table has columns:
- `denomination_from char(3) NOT NULL CHECK (~ '^[A-Z]{3}$')`
- `denomination_to char(3) NOT NULL CHECK (~ '^[A-Z]{3}$')`
- `rate numeric(28, 8) NOT NULL CHECK (rate > 0)`
- `valid_from timestamptz NOT NULL`
- `source_provider text NOT NULL CHECK (length(source_provider) <= 64)`
- `recorded_at timestamptz NOT NULL DEFAULT now()`

**And** if any column is missing or has wrong type, this story adds the missing piece via additive migration
**And** the schema-audit CI gate (architecture §5.2.5: `_rate` suffix → `numeric(28, 8)`) passes [Source: epics.md Epic 6 Story 6.7 AC; Story 6.1 AC1].

### AC2 — Rates append-only enforced by DB trigger

**Given** Story 6.1 implemented the append-only trigger forward-stamped
**When** this story verifies + formalizes
**Then** the trigger `trg_rates_block_update` exists and blocks UPDATE
**And** the trigger `trg_rates_block_delete` exists and blocks DELETE
**And** an integration test asserts (already from Story 6.1, re-verified here):
```csharp
[Fact, Trait("Category", "Integration")]
public async Task UpdateRate_Throws()
{
    await db.Database.ExecuteSqlRawAsync("INSERT INTO rates (denomination_from, denomination_to, rate, valid_from, source_provider) VALUES ('XAU', 'USD', 92.50, now(), 'test')");
    var ex = await Assert.ThrowsAsync<PostgresException>(() =>
        db.Database.ExecuteSqlRawAsync("UPDATE rates SET rate = 99.99 WHERE denomination_to = 'USD'"));
    ex.SqlState.Should().StartWith("P");  // raised by trigger
    ex.Message.Should().Contain("rates table is append-only");
}
```
**And** a NetArchTest assertion: no C# code path calls `SaveChangesAsync` after modifying an attached `Rate` entity (entity is read-only)
**And** the EF Core entity configuration marks `Rate` properties as readonly post-construction (per the immutable-entity pattern) [Source: epics.md Epic 6 Story 6.7 AC2; Story 6.1 AC2].

### AC3 — Indexed historical-rate lookup < 50ms

**Given** Story 6.5 (view historical rate) and Story 6.4 (lock rate at exchange time) both need fast lookups
**When** the index `ix_rates_pair_validfrom` from Story 6.1 is queried
**Then** `SELECT * FROM rates WHERE denomination_from = $1 AND denomination_to = $2 AND valid_from <= $3 ORDER BY valid_from DESC LIMIT 1` runs in < 50ms
**And** the test seeds 100K rate rows (3+ years of hourly data × multiple currency pairs) and asserts p95 < 50ms
**And** `EXPLAIN ANALYZE` confirms the index is used (no sequential scan) [Source: epics.md Epic 6 Story 6.7 AC3].

### AC4 — `IRateRepository.GetAtAsync` for historical lookup

**Given** Story 6.1 added basic `GetLatestAsync`
**When** this story extends the repository
**Then** `IRateRepository.GetAtAsync(string from, string to, DateTimeOffset asOf)` returns the rate row with `valid_from <= asOf` ordered DESC LIMIT 1
**And** if no rate exists at-or-before `asOf` for the pair, returns `null`
**And** the method is documented: "Returns the rate that WAS in effect at `asOf`. Used by Story 6.4 to lock the rate at transaction commit time, and by Story 6.5 to display past exchange rates."
**And** an integration test asserts:
- 5 rates inserted at hourly intervals → query at midpoint returns the earlier one
- Query before earliest rate → returns null
- Query after latest rate → returns latest [Source: epics.md Epic 6 Story 6.7 AC; Story 6.5 dependency].

### AC5 — 7-year retention policy documented + scheduled archive job (stub)

**Given** the audit retention requirement is 7 years (aligned with general fintech audit retention norms)
**When** this story documents + stubs the retention process
**Then** `docs/ops/rate-retention-policy.md` documents:
- Retention period: 7 years from `recorded_at`
- Archive target: cold storage in Azure Blob (Cool tier) — out-of-scope to provision in this story; documented requirement
- Deletion window: NEVER delete from `rates` while in active retention (the append-only trigger enforces)
- After 7 years: rows MAY be moved to cold-storage `rates_archive_*.csv` files via export, then a one-time exception to the append-only trigger (operations runbook)
**And** a stub Wolverine scheduled job `RatesRetentionAuditJob` runs WEEKLY:
- Counts rows in `rates`
- Computes oldest `recorded_at`
- Logs telemetry `rates_retention_status` with `{ row_count, oldest_recorded_at, days_since_oldest }`
- If oldest_recorded_at > 7 years, fires alert (no auto-archive at MVP — operator intervention required) [Source: epics.md Epic 6 Story 6.7 AC4].

### AC6 — Audit-trail integration: every exchange row references the `rate` row by ID

**Given** Story 6.4 (Exchange operation) locks a rate at transaction time
**When** the exchange's journal entries are written
**Then** the audit trail metadata includes `rate_id uuid` referencing the `rates.id` that was locked
**And** the `journal_entries` table gains a nullable `locked_rate_id uuid` column (added by this story's migration; consumed by Story 6.4)
**And** an FK constraint references `rates.id` (ON DELETE RESTRICT — rates are append-only anyway, but the FK formalizes)
**And** Story 6.4 populates this column on Exchange writes
**And** Story 6.5 displays the historical rate by joining via this FK [Source: epics.md Epic 6 Story 6.7 + 6.4 + 6.5].

### AC7 — Telemetry: `rate_audit_query` event

**Given** historical rate lookups have audit relevance
**When** `IRateRepository.GetAtAsync` is called from a user-context (vs background job)
**Then** `TelemetryEmitter.Emit("rate_audit_query", { opaque_user_id, denomination_pair_hash, asof_bucket })` fires
**And** background job calls (Story 6.1's feed) are NOT counted (they are write-side, not audit queries)
**And** PII guard validates [Source: Story 0.14 telemetry primitive].

### AC8 — `Rate` entity is read-only in the C# domain

**Given** the table is append-only at the DB layer
**When** the C# domain models the entity
**Then** `Rate` is a `sealed record` with all properties `init`-only
**And** there is no `Rate.UpdateXxx()` method
**And** the EF Core configuration sets `metadata.SetIsReadOnly(true)` (or equivalent) so EF Core would refuse to track changes
**And** a Roslyn analyzer rule (or NetArchTest) flags any code attempting to `db.Set<Rate>().Update(...)` or `Attach(...).State = Modified`

### AC9 — Backup + restore tested end-to-end (smoke test)

**Given** rate history is the auditable foundation
**When** a backup-restore drill is performed (per architecture §2.10 rehearsal #1)
**Then** the test:
1. Seeds 100 rates
2. Triggers a backup via `pg_dump`
3. Drops the table
4. Restores via `pg_restore`
5. Asserts all 100 rates present + readable + queryable via `IRateRepository.GetLatestAsync`
**And** the test runs as part of the disaster-recovery rehearsal suite (not every CI run — quarterly per AR-044) [Source: architecture.md §2.10 rehearsal #1].

## Tasks / Subtasks

### Backend — Schema verification + extension

- [ ] **T1 (AC: 1)** — Verify Story 6.1's schema matches AC1 exactly
  - [ ] If any column missing, add additive migration
- [ ] **T2 (AC: 6)** — Add `locked_rate_id uuid` column to `journal_entries` table
  - [ ] Migration `YYYYMMDDHHMMSS_AddLockedRateIdToJournalEntries.cs`
  - [ ] FK to `rates(id)` ON DELETE RESTRICT
  - [ ] Index `ix_journal_entries_locked_rate_id` (for Story 6.5 reverse lookup)
  - [ ] Nullable (only Exchange entries populate; Transfer + Record-Debt entries leave null)

### Backend — Repository extension

- [ ] **T3 (AC: 4)** — Extend `IRateRepository` with `GetAtAsync(string from, string to, DateTimeOffset asOf)`
  - [ ] Implementation in `RateRepository.cs`
  - [ ] Uses `ix_rates_pair_validfrom` index
  - [ ] XML doc comment per AC4
- [ ] **T4 (AC: 8)** — Make `Rate` entity read-only
  - [ ] `sealed record Rate(Guid Id, string DenominationFrom, string DenominationTo, decimal RateValue, DateTimeOffset ValidFrom, string SourceProvider, DateTimeOffset RecordedAt);`
  - [ ] EF Core entity configuration: explicitly mark properties `init`-only via configuration

### Backend — Append-only enforcement

- [ ] **T5 (AC: 2)** — NetArchTest rule: no `Update`/`Attach...Modified` on `Rate`
  - [ ] Custom NetArchTest assertion in `Faktuboh.ArchitectureTests/RatesAppendOnlyTests.cs`
- [ ] **T6 (AC: 2)** — Re-verify the Postgres trigger from Story 6.1
  - [ ] Integration test (re-asserted in this story for completeness)

### Backend — Retention job (stub)

- [ ] **T7 (AC: 5)** — Create `Faktuboh.Infrastructure/Rates/RatesRetentionAuditJob.cs`
  - [ ] Wolverine handler for `RatesRetentionAuditTrigger` message
  - [ ] Weekly schedule
  - [ ] Counts rows + oldest row + computes age
  - [ ] Emits telemetry per AC5
  - [ ] Fires alert if oldest > 7 years
- [ ] **T8 (AC: 5)** — Register weekly schedule in `Program.cs`
  - [ ] `opts.Schedules.AddJob<RatesRetentionAuditTrigger>().Weekly()`

### Backend — Telemetry

- [ ] **T9 (AC: 7)** — Wire `rate_audit_query` event
  - [ ] Emit on user-context calls to `GetAtAsync`
  - [ ] Skip on background-job calls (pass `IsBackgroundCall: bool` parameter or use `ProcessingActivity`)

### Documentation

- [ ] **T10 (AC: 5)** — Create `docs/ops/rate-retention-policy.md` per AC5
- [ ] **T11 (AC: 6)** — Update `docs/development/exchange-audit-trail.md` (Epic 6 audit doc)
  - [ ] Document `journal_entries.locked_rate_id` FK + its consumption by 6.4 + 6.5

### Tests

- [ ] **T12 (AC: 1, 2)** — Schema + trigger verification tests in `Faktuboh.Infrastructure.Tests/Rates/RatesSchemaTests.cs`
- [ ] **T13 (AC: 3)** — Performance test
  - [ ] Seed 100K rows; assert `GetAtAsync` p95 < 50ms
  - [ ] `EXPLAIN ANALYZE` assertion (custom test helper)
- [ ] **T14 (AC: 4)** — `GetAtAsync` integration tests (5-row hourly seed scenarios)
- [ ] **T15 (AC: 5)** — `RatesRetentionAuditJobTests`
  - [ ] Seed rows of varying age; assert telemetry payload + alert firing
- [ ] **T16 (AC: 8)** — NetArchTest for read-only `Rate` entity
- [ ] **T17 (AC: 9)** — Backup-restore drill test (tagged `[Trait("Category", "DR-rehearsal")]`)

## Dev Notes

### Critical guardrails (do not violate)

1. **Append-only is sacred.** The Postgres trigger blocks UPDATE/DELETE at the DB layer. The C# `Rate` entity is read-only at the domain layer. The NetArchTest rule blocks accidental mutation patterns at the architecture layer. Three-layer defense for the audit foundation.
2. **No `Rate.UpdateXxx()` methods.** Period. If a rate needs correction, the operational pattern is "insert a corrective row with a new `valid_from`" + "operator runbook documents the correction" — never mutation.
3. **7-year retention.** No `DELETE FROM rates` in production code. The retention audit job ALERTS on aged rows; archive is operator-driven (out-of-scope at MVP).
4. **`locked_rate_id` FK is ON DELETE RESTRICT.** Defense-in-depth — even if the append-only trigger were dropped, the FK prevents losing the audit linkage.
5. **`numeric(28, 8)` enforced by schema-audit CI gate.** Per architecture §5.2.5.
6. **Index usage MUST be verified.** A query that compiles but does a sequential scan is a 50ms→5s regression at scale. `EXPLAIN ANALYZE` test asserts.
7. **Background-job calls do NOT emit `rate_audit_query` telemetry.** Audit telemetry is for user-driven queries; the hourly feed write itself is not an audit event.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Domain/Rates/
│   ├── Rate.cs                                             # MODIFY (sealed record + init-only props)
│   └── IRateRepository.cs                                  # MODIFY (+ GetAtAsync)
├── Faktuboh.Infrastructure/
│   ├── Persistence/Configurations/
│   │   └── RateConfiguration.cs                            # MODIFY (read-only metadata)
│   ├── Persistence/Migrations/
│   │   └── YYYYMMDDHHMMSS_AddLockedRateIdToJournalEntries.cs  # NEW
│   └── Rates/
│       ├── RateRepository.cs                               # MODIFY (+ GetAtAsync)
│       ├── RatesRetentionAuditJob.cs                       # NEW
│       └── RatesRetentionAuditTrigger.cs                   # NEW (scheduled message)

backend/tests/
├── Faktuboh.Infrastructure.Tests/Rates/
│   ├── RatesSchemaTests.cs                                 # NEW
│   ├── RatesAppendOnlyTriggerTests.cs                      # MODIFY (extend from Story 6.1)
│   ├── RateRepositoryGetAtTests.cs                         # NEW
│   ├── RateLookupPerformanceTests.cs                       # NEW (100K seed)
│   └── RatesRetentionAuditJobTests.cs                      # NEW
├── Faktuboh.ArchitectureTests/
│   └── RatesAppendOnlyTests.cs                             # NEW (NetArchTest)
└── Faktuboh.Infrastructure.Tests/DR/
    └── RatesBackupRestoreDrillTests.cs                     # NEW (DR rehearsal)

docs/ops/
├── rate-retention-policy.md                                # NEW
└── exchange-audit-trail.md                                 # MODIFY
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Schema + trigger | Append-only enforcement | Testcontainers Postgres |
| Repository | Historical lookup correctness | Testcontainers Postgres |
| Performance | < 50ms with 100K rows | Testcontainers + `EXPLAIN ANALYZE` |
| Architectural | Read-only entity | NetArchTest |
| DR rehearsal | Backup-restore drill | Quarterly suite |
| Telemetry | Event emission for user queries | AR-047 helper |

### Architecture compliance

- **FR22** — historical rate storage.
- **AR-006** — gold-base + immutable history.
- **§2.10 rehearsal #1** — backup-restore drill.
- **§5.2.5** — `numeric(28, 8)` for `_rate` columns.
- **§4.2 D4.3** — Aspire MigrationService idempotent triggers.
- **§4.7** — Wolverine scheduled jobs (retention audit).

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| ORM | EF Core 10 | Read-only entity metadata |
| Test | NetArchTest | Read-only enforcement |
| Test | Testcontainers Postgres | Schema + perf + DR tests |

### Testing requirements

- **Schema verification:** column shape + constraints + triggers.
- **Repository:** `GetAtAsync` correctness across seed scenarios.
- **Performance:** < 50ms with realistic data volume.
- **Architectural:** `Rate` is read-only; NetArchTest enforces.
- **DR:** quarterly backup-restore drill.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.6 — Aspire MigrationService pattern
- Story 0.10 — backup configuration on Postgres Flexible Server
- Story 0.14 — telemetry primitive
- Story 6.1 — `rates` table + Wolverine schedule + initial trigger (forward-stamp this story's contract)

**Downstream consumers:**
- Story 6.4 — Exchange writes `journal_entries.locked_rate_id`
- Story 6.5 — Historical rate view joins via `locked_rate_id`
- Story 6.8 — Freshness SLA reads `recorded_at`

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.7
- FR22 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Dual-precision — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- Backup-restore drill — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.10 rehearsal #1
- Story 6.1 rate-feed — [_bmad-output/implementation-artifacts/6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md](./6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Append-only is enforced at DB (trigger), domain (read-only entity), and architecture (NetArchTest) — three layers.
- `journal_entries.locked_rate_id` FK formalizes the audit linkage consumed by 6.4 + 6.5.
- 7-year retention policy documented; archive process is operator-driven at MVP.
- Backup-restore drill runs quarterly per AR-044 rehearsal cadence.

### File List

_Populated during implementation._
