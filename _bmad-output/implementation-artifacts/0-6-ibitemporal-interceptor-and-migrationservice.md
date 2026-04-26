# Story 0.6: IBitemporal interceptor and MigrationService

Status: ready-for-dev

## Story

As a **developer**,
I want **every entity implementing `IBitemporal` to automatically write history rows on UPDATE/DELETE via an EF Core `SaveChanges` interceptor, with the MigrationService ensuring triggers and extensions are idempotently installed on every cold start**,
so that **ADR-022's application-level bitemporal storage is enforced by infrastructure, not discipline**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. ADR-022 application-level bitemporal pattern + Aspire MigrationService idempotent boot.

**Why this story now:** Per architecture §4.7 step 2, database (DB + migrations + bitemporal interceptor) is the second step. Within Epic 0 sequencing we ship middleware (0.4, 0.5) first because they compile against 0.1's stubs, and this story lands once Story 0.10 is up (needs real Postgres to validate interceptor against). Story 2.1 (first real slice) requires this story complete — the first bitemporal entity is Contact.

## Acceptance Criteria

### AC1 — `IBitemporal` marker interface finalized

**Given** Story 0.1 scaffolded `IBitemporal.cs` in `Faktuboh.Domain/Primitives/`
**When** this story finalizes the interface
**Then** the interface shape matches ADR-022 verbatim:

```csharp
// backend/src/Faktuboh.Domain/Primitives/IBitemporal.cs
public interface IBitemporal
{
    DateTimeOffset ValidFrom { get; set; }
    DateTimeOffset? ValidTo { get; set; }
    DateTimeOffset RecordedAt { get; set; }
}
```

**And** a second marker interface `IBitemporalHistoryRow` exists for history-table entities (carries additional `Guid HistoryId` + `OperationType` enum columns) [Source: adrs/adr-022]

### AC2 — `SaveChanges` interceptor writes history rows

**Given** EF Core 10's `SaveChangesInterceptor` API
**When** a mutation (UPDATE or DELETE) on any `IBitemporal` entity is saved
**Then** `BitemporalSaveChangesInterceptor` runs via interface dispatch (`entry.Entity is IBitemporal` check, not reflection)
**And** for each UPDATE: pre-mutation entity state is copied to `<entity>_history` table with `RecordedAt = UtcNow`, `OperationType = Update`
**And** for each DELETE: pre-delete entity state is copied to `<entity>_history` with `OperationType = Delete`
**And** `RecordedAt` on the source row is stamped to `UtcNow` on every mutation
**And** the interceptor lives at `backend/src/Faktuboh.Infrastructure/Persistence/Interceptors/BitemporalSaveChangesInterceptor.cs`:

```csharp
public sealed class BitemporalSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        var db = eventData.Context;
        if (db is null) return ValueTask.FromResult(result);
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.Entity is not IBitemporal bitemporal) continue;
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                // Build history row from original values (pre-mutation)
                var historyType = HistoryTypeResolver.For(entry.Entity.GetType());
                var historyEntity = HistoryRowFactory.Create(entry, historyType, now, operation: entry.State);
                db.Add(historyEntity);
            }
            bitemporal.RecordedAt = now;
        }
        return ValueTask.FromResult(result);
    }
}
```

[Source: adrs/adr-022]

### AC3 — Interceptor registered on every `DbContext`

**Given** `Faktuboh.Infrastructure/Persistence/FaktubohDbContext.cs` is the sole DbContext
**When** `Program.cs` wires EF Core
**Then** `builder.Services.AddDbContext<FaktubohDbContext>(o => { o.UseNpgsql(...); o.UseSnakeCaseNamingConvention(); o.AddInterceptors(new BitemporalSaveChangesInterceptor()); });`
**And** a unit test `BitemporalInterceptorRegistrationTests.cs` resolves `FaktubohDbContext` from DI + asserts via reflection that `BitemporalSaveChangesInterceptor` is in the context's interceptor collection [Source: adrs/adr-022]

### AC4 — Stub `TestBitemporal` entity + `test_bitemporals_history` round-trip test

**Given** an architecture-only test entity `TestBitemporal` (lives in `Faktuboh.Infrastructure.Tests/Fixtures/` not production code)
**When** the round-trip test runs
**Then** `TestBitemporal` implements `IBitemporal` with a simple string `Name` property + Guid id
**And** the test performs:
1. Insert `TestBitemporal` with Name="A"
2. Update to Name="B"
3. Delete
4. Query `test_bitemporals_history`
**And** exactly 2 history rows exist (one per mutation: update + delete)
**And** the history rows contain the pre-mutation state (first history row has Name="A", second has Name="B")
**And** every row's `RecordedAt` is distinct and monotonically increasing
**And** this is the "audit round-trip" fitness test per ADR-022 + §7.5.3 + AC5 below

### AC5 — CI-wired audit round-trip fitness test

**Given** the audit round-trip test lives in `backend/tests/Faktuboh.Infrastructure.Tests/Bitemporal/AuditRoundTripTests.cs`
**When** Story 0.9's `ci.yml` runs
**Then** the test runs as part of the `backend-integration-tests` job (uses Testcontainers Postgres)
**And** failure blocks PR merge [Source: architecture.md §4 D4.1 + ADR-022]

### AC6 — `AsOf(DateTimeOffset)` query extension

**Given** repositories expose `AsOf(DateTimeOffset transactionTime, DateTimeOffset? validTime = null)` per ADR-022
**When** the extension is called with a historical time `t`
**Then** the query returns the entity view as it existed at `t`:
- For current table: if `RecordedAt <= t` AND (the row wasn't deleted before `t`)
- For history table: union of rows where `RecordedAt <= t AND (ValidTo IS NULL OR ValidTo > validTime)` (valid-time filter applied when `validTime` provided)
**And** the extension method is defined in `Faktuboh.Infrastructure/Persistence/Extensions/BitemporalQueryExtensions.cs`:

```csharp
public static IQueryable<T> AsOf<T>(this IQueryable<T> source, DateTimeOffset transactionTime, DateTimeOffset? validTime = null)
    where T : class, IBitemporal
{
    // Implementation uses EF Core's FromSqlInterpolated or explicit UNION ALL across table + history table
    // Details in implementation; test-driven against AuditRoundTripTests + AsOfQueryTests
}
```

**And** unit tests cover: query at `t = now` returns current state; query at `t < first mutation` returns the original insert state; query at `t` between two updates returns the state between them [Source: adrs/adr-022]

### AC7 — Aspire MigrationService idempotent boot

**Given** per D4.3 the MigrationService runs EF Core migrations + installs any non-EF DB objects (extensions) on every cold start idempotently
**When** `Faktuboh.AppHost/Program.cs` orchestrates services
**Then** a `MigrationService` project (or embedded hosted service in `Faktuboh.Api`) executes at startup:
1. `await dbContext.Database.MigrateAsync()` — applies pending EF migrations
2. Executes idempotent SQL to install required Postgres extensions (e.g., `CREATE EXTENSION IF NOT EXISTS "uuid-ossp"`, `CREATE EXTENSION IF NOT EXISTS "pgcrypto"` — reservation for envelope encryption)
3. Asserts that every `IBitemporal` entity has a corresponding `_history` table (fails boot if missing — detects developer who forgot to add history table to migration)
**And** the service lives at `backend/src/Faktuboh.Infrastructure/Persistence/MigrationService.cs` — implements `IHostedService`
**And** the service is registered as the first hosted service in `Faktuboh.Api/Program.cs` (via `builder.Services.AddHostedService<MigrationService>()`)
**And** running the service twice produces identical schema state [Source: architecture.md §4 D4.3]

### AC8 — History tables created alongside source tables in migrations

**Given** EF Core migrations are the schema path
**When** a developer adds a new `IBitemporal` entity + runs `dotnet ef migrations add AddContactsAndJournalEntries` (Story 2.1 migration)
**Then** the migration must create BOTH `contacts` + `contacts_history` tables (history tables are conventional — same columns + `HistoryId uuid PRIMARY KEY` + `OperationType text`)
**And** a code-generator / convention (via EF Core modelBuilder convention) auto-adds history table shapes, OR the team maintains a manual-discipline + fitness test that asserts presence:
  - `backend/tests/Faktuboh.ArchitectureTests/BitemporalHistoryTableTests.cs` scans `FaktubohDbContext.Model` for every `IBitemporal` type + asserts a matching `_history` entity is registered
**And** failure blocks PR merge

## Tasks / Subtasks

### T1 — Finalize `IBitemporal` + add `IBitemporalHistoryRow` (AC: 1)

- [ ] Edit `backend/src/Faktuboh.Domain/Primitives/IBitemporal.cs` per AC1 (from Story 0.1 stub)
- [ ] Create `backend/src/Faktuboh.Domain/Primitives/IBitemporalHistoryRow.cs`:
  ```csharp
  public interface IBitemporalHistoryRow : IBitemporal
  {
      Guid HistoryId { get; set; }
      BitemporalOperation Operation { get; set; }
  }
  public enum BitemporalOperation { Update, Delete }
  ```

### T2 — DbContext + EF configuration baseline (AC: 3, 8)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/FaktubohDbContext.cs`:
  - Inherits `DbContext`
  - `protected override void OnModelCreating(ModelBuilder mb)` applies all `IEntityTypeConfiguration` via `mb.ApplyConfigurationsFromAssembly(typeof(FaktubohDbContext).Assembly)`
- [ ] Edit `backend/src/Faktuboh.Api/Program.cs`:
  ```csharp
  builder.Services.AddDbContext<FaktubohDbContext>((sp, o) =>
  {
      o.UseNpgsql(builder.Configuration.GetConnectionString("Faktuboh"));
      o.UseSnakeCaseNamingConvention();
      o.AddInterceptors(sp.GetRequiredService<BitemporalSaveChangesInterceptor>());
  });
  builder.Services.AddSingleton<BitemporalSaveChangesInterceptor>();
  ```
- [ ] Add package `EFCore.NamingConventions` to `Faktuboh.Infrastructure`

### T3 — BitemporalSaveChangesInterceptor (AC: 2)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/Interceptors/BitemporalSaveChangesInterceptor.cs` per AC2 snippet
- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/Interceptors/HistoryTypeResolver.cs` — maps `typeof(Contact)` → `typeof(ContactHistory)` via convention + registration
- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/Interceptors/HistoryRowFactory.cs` — copies entity property values from the `EntityEntry.OriginalValues` to a new history-row instance
- [ ] Register via DI in Program.cs (T2 above)

### T4 — Stub `TestBitemporal` + audit round-trip integration test (AC: 4, 5)

- [ ] Create `backend/tests/Faktuboh.Infrastructure.Tests/Fixtures/TestBitemporal.cs` (test-only entity)
- [ ] Create `backend/tests/Faktuboh.Infrastructure.Tests/Fixtures/TestBitemporalConfiguration.cs` (EF config for test entity — applied only in test DbContext)
- [ ] Create `backend/tests/Faktuboh.Infrastructure.Tests/Bitemporal/AuditRoundTripTests.cs`:
  - `[Trait("Category", "Integration")]`
  - Testcontainers Postgres
  - Inserts TestBitemporal, updates, deletes; queries history table; asserts 2 rows with correct pre-mutation state
- [ ] Wire into Story 0.9's `ci.yml` `backend-integration-tests` job (already configured)

### T5 — `AsOf` query extension (AC: 6)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/Extensions/BitemporalQueryExtensions.cs`
- [ ] Strategy: `AsOf(t)` emits a `UNION ALL` between current table (where `RecordedAt <= t`) and history table (where `RecordedAt <= t`), then DISTINCT ON the entity key
- [ ] Unit tests in `backend/tests/Faktuboh.Infrastructure.Tests/Bitemporal/AsOfQueryTests.cs` — 3 scenarios: query at-now, before-first-mutation, between-two-updates

### T6 — MigrationService (AC: 7, 8)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/MigrationService.cs`:
  ```csharp
  public sealed class MigrationService(IServiceScopeFactory scopeFactory, ILogger<MigrationService> logger) : IHostedService
  {
      public async Task StartAsync(CancellationToken ct)
      {
          await using var scope = scopeFactory.CreateAsyncScope();
          var db = scope.ServiceProvider.GetRequiredService<FaktubohDbContext>();
          logger.LogInformation("Applying EF Core migrations...");
          await db.Database.MigrateAsync(ct);
          await EnsureExtensionsAsync(db, ct);
          EnsureBitemporalHistoryTables(db);
          logger.LogInformation("Migration complete.");
      }
      public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

      private static async Task EnsureExtensionsAsync(FaktubohDbContext db, CancellationToken ct)
      {
          await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"", ct);
          await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\"", ct);
      }

      private static void EnsureBitemporalHistoryTables(FaktubohDbContext db)
      {
          var bitemporalTypes = db.Model.GetEntityTypes()
              .Where(t => typeof(IBitemporal).IsAssignableFrom(t.ClrType) && !typeof(IBitemporalHistoryRow).IsAssignableFrom(t.ClrType))
              .ToArray();
          foreach (var t in bitemporalTypes)
          {
              var historyName = t.GetTableName() + "_history";
              var historyType = db.Model.GetEntityTypes().FirstOrDefault(ht => ht.GetTableName() == historyName);
              if (historyType is null)
                  throw new InvalidOperationException($"IBitemporal entity '{t.ClrType.Name}' has no registered history type (expected table '{historyName}'). Add configuration.");
          }
      }
  }
  ```
- [ ] Register via `builder.Services.AddHostedService<MigrationService>();` in `Program.cs`
- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/BitemporalHistoryTableTests.cs` — same assertion as AC8 but test-time (catches missing history table in PRs before boot)

### T7 — Idempotency test (AC: 7)

- [ ] Create `backend/tests/Faktuboh.Infrastructure.Tests/Bitemporal/MigrationServiceIdempotencyTests.cs`:
  - Runs `MigrationService.StartAsync` twice
  - Queries `information_schema.tables` before + after second run
  - Asserts zero schema diff between runs

## Dev Notes

### Critical guardrails (do not violate)

1. **Interface dispatch, not reflection.** `entry.Entity is IBitemporal` is the hot-path check per ADR-022 — attributes + reflection would cost per-save. [Source: adrs/adr-022]
2. **History table shape MUST include `HistoryId` + `OperationType`.** Source table's PK is copied as a non-unique column (the same entity id appears in multiple history rows).
3. **`RecordedAt` on source row is stamped by the interceptor** on every mutation. Do not expose `RecordedAt` as a developer-set value in aggregate constructors — it's infrastructure-owned.
4. **Migration service must run BEFORE the web host accepts traffic** — it's the first IHostedService. If a container starts serving requests before migrations apply, the first request breaks.
5. **No `DROP TABLE` in migrations.** Migrations are append-only — rollback via forward-migration per §6.5.8. [Source: architecture.md §6.5.8]
6. **In-memory EF provider is forbidden** — bitemporal interceptor behavior differs. Testcontainers Postgres is the only integration-test DB. [Source: Story 2.1 `Murat's landmines` + architecture.md §5.3.3]
7. **`AsOf` query must use `UNION ALL`**, not `UNION` — `UNION` deduplicates and hides the scenario where a row was identical before and after an update (still 2 rows of audit, not 1).

### Source tree — files to create or edit

```
backend/src/
├── Faktuboh.Domain/Primitives/
│   ├── IBitemporal.cs                                  # EDIT (finalize)
│   └── IBitemporalHistoryRow.cs                        # NEW
├── Faktuboh.Infrastructure/Persistence/
│   ├── FaktubohDbContext.cs                            # NEW
│   ├── MigrationService.cs                             # NEW (IHostedService)
│   ├── Interceptors/
│   │   ├── BitemporalSaveChangesInterceptor.cs         # NEW
│   │   ├── HistoryTypeResolver.cs                      # NEW
│   │   └── HistoryRowFactory.cs                        # NEW
│   └── Extensions/
│       └── BitemporalQueryExtensions.cs                # NEW (AsOf)
└── Faktuboh.Api/Program.cs                             # EDIT (wire DbContext + interceptor + MigrationService)

backend/tests/
├── Faktuboh.Infrastructure.Tests/
│   ├── Fixtures/
│   │   ├── TestBitemporal.cs                           # NEW
│   │   ├── TestBitemporalHistory.cs                    # NEW
│   │   └── TestBitemporalConfiguration.cs              # NEW
│   └── Bitemporal/
│       ├── AuditRoundTripTests.cs                      # NEW (ADR-022 fitness test)
│       ├── AsOfQueryTests.cs                           # NEW
│       └── MigrationServiceIdempotencyTests.cs         # NEW
└── Faktuboh.ArchitectureTests/
    ├── BitemporalInterceptorRegistrationTests.cs       # NEW
    └── BitemporalHistoryTableTests.cs                  # NEW (IBitemporal → _history table exists)
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Architecture | NetArchTest + DbContext model scan | AC3 + AC8 — interceptor registered + every IBitemporal has history table |
| Integration | Testcontainers Postgres | AC4 + AC5 audit round-trip + AC7 MigrationService idempotency |
| Unit | xUnit v3 | `AsOf` query logic per scenario |

### Project Structure Notes

- MigrationService is an `IHostedService` embedded in `Faktuboh.Api` (per this story's simplification), NOT a separate Aspire project. Architecture.md §4 D4.3 mentions "Aspire-orchestrated MigrationService" — the spirit (idempotent schema sync) is preserved; the form (embedded vs separate project) is a variance justified by cost-floor (keeping ACA container count minimal).
- `TestBitemporal` + `TestBitemporalHistory` live in the test project's `Fixtures/` — NEVER in production code. The `FaktubohDbContext` production config does not reference these types; tests use a subclass `TestFaktubohDbContext` that adds them.

### Architecture compliance

- [architecture.md §4 D4.1](../planning-artifacts/architecture.md) — DB engine + bitemporal
- [architecture.md §4 D4.3](../planning-artifacts/architecture.md) — migration tooling + MigrationService
- [architecture.md §3.5](../planning-artifacts/architecture.md) — bitemporal storage strategy
- [architecture.md §7.5.3 #2](../planning-artifacts/architecture.md) — slice isolation fitness test (relies on bitemporal interceptor being stable)
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md) — full rationale

### Library/framework requirements

| Layer | Package | Version | Purpose |
|---|---|---|---|
| ORM | Microsoft.EntityFrameworkCore | 10.0.x | DbContext + interceptors |
| Postgres | Npgsql.EntityFrameworkCore.PostgreSQL | latest | Provider |
| Naming | EFCore.NamingConventions | latest | `UseSnakeCaseNamingConvention()` |
| Migrations CLI | Microsoft.EntityFrameworkCore.Design | 10.0.x | `dotnet ef` |
| Test DB | Testcontainers.PostgreSql | latest | Integration tests |

### Testing requirements

- **AuditRoundTripTests is THE fitness test for this story.** Marked story done only when it passes in CI against real Postgres.
- **Interceptor registration test** uses reflection on `DbContext.Set<...>()` or examines `IDbContextOptions.Extensions` via reflection — document the mechanism chosen.
- **`AsOf` tests must exercise both transaction-time and valid-time axes** — valid-time is the second optional argument per ADR-022.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: `IBitemporal` stub exists.
- **Story 0.5 (Idempotency)** complete: `idempotency_keys` table migration exists but is NOT `IBitemporal` (it's ephemeral cache). Must verify MigrationService `EnsureBitemporalHistoryTables` doesn't false-positive on `idempotency_keys`.
- **Story 0.10 (Azure infra)** complete: Postgres Flex Server exists; `ConnectionStrings:Faktuboh` env var is populated.
- **Story 2.1 (Contacts/Create)** depends on this story — first bitemporal aggregate is `Contact`.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.6 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.1 + D4.3](../planning-artifacts/architecture.md) — DB + migrations
- [_bmad-output/planning-artifacts/architecture.md §3.5](../planning-artifacts/architecture.md) — bitemporal strategy
- [_bmad-output/planning-artifacts/adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
