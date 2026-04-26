# Story 6.1: Hourly rate-feed ingestion with gold as base unit

Status: ready-for-dev

## Story

As **Wahid**,
I want **a Wolverine scheduled job that ingests exchange rates hourly using gold as the internal base unit, with same-transaction outbox safety on feed failures**,
so that **every supported denomination has a fresh rate available for Exchange operations** (FR18, AR-006).

**Covers requirements:** Epic 6 Story 6.1 — FR18 (hourly rate fetch with gold as base), NFR-I1 (provider must support gold + silver + USD/EUR/GBP/SAR/AED/TRY), NFR-I2 (3 retries with exponential backoff), NFR-P8 (full processing < 30s), NFR-SC5 (zero error-rate increase during feed), AR-035 dual-precision (`numeric(28,8)` for FX) [Source: epics.md Epic 6 Story 6.1; prd.md FR18, NFR-I1, NFR-I2, NFR-P8, NFR-SC5; architecture.md §5.2.5].

**Why this story now:** Epic 6 opens with the rate-feed because every other Epic 6 story depends on a populated `rates` table:
- Story 6.3 (view current rate) reads the latest row.
- Story 6.4 (Exchange) locks a rate at transaction time.
- Story 6.5 (view historical rate) joins to `rates` by `valid_from`.
- Story 6.7 (historical storage) extends this story's table.
- Story 6.8 (rate freshness SLA) reads `recorded_at` to compute staleness.
- Story 6.9 (WireMock cassette harness) tests against the providers configured here.

This story also extends `CurrencyRegistry` (Story 0.1) to include metals XAU + XAG.

## Acceptance Criteria

### AC1 — `rates` table created with required columns + dual-precision

**Given** no `rates` table exists yet
**When** this story lands the EF Core migration
**Then** the migration `YYYYMMDDHHMMSS_AddRatesTable.cs` creates:

```sql
CREATE TABLE rates (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    denomination_from  char(3) NOT NULL CHECK (denomination_from ~ '^[A-Z]{3}$'),
    denomination_to    char(3) NOT NULL CHECK (denomination_to ~ '^[A-Z]{3}$'),
    rate            numeric(28, 8) NOT NULL CHECK (rate > 0),
    valid_from      timestamptz NOT NULL,
    source_provider text NOT NULL CHECK (length(source_provider) <= 64),
    recorded_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_rates_pair_validfrom ON rates (denomination_from, denomination_to, valid_from DESC);
CREATE INDEX ix_rates_recorded_at ON rates (recorded_at DESC);
```

**And** the `rate` column is `numeric(28, 8)` per AR-035 dual-precision rule (architecture §5.2.5)
**And** the `_rate` suffix is enforced by the schema-audit CI gate (architecture §5.2.5: every column with `_rate` suffix MUST be `numeric(28, 8)`)
**And** the table is APPEND-ONLY — a Postgres trigger blocks UPDATE and DELETE (per Story 6.7 AC2 — implemented here as a forward-stamp) [Source: epics.md Epic 6 Story 6.1; architecture.md §5.2.5; epics.md Epic 6 Story 6.7].

### AC2 — Postgres trigger blocks UPDATE/DELETE on `rates` table

**Given** rates are immutable per AR-006 + Story 6.7
**When** the migration runs
**Then** a trigger function is created:

```sql
CREATE OR REPLACE FUNCTION fn_rates_block_mutation() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'rates table is append-only — UPDATE/DELETE blocked (operation: %)', TG_OP
        USING ERRCODE = '23000';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_rates_block_update BEFORE UPDATE ON rates
    FOR EACH ROW EXECUTE FUNCTION fn_rates_block_mutation();
CREATE TRIGGER trg_rates_block_delete BEFORE DELETE ON rates
    FOR EACH ROW EXECUTE FUNCTION fn_rates_block_mutation();
```

**And** an integration test asserts: attempting `UPDATE rates SET rate = ...` throws Postgres exception
**And** the trigger is idempotently installed via `CREATE OR REPLACE FUNCTION` (Aspire MigrationService pattern per D4.3) [Source: epics.md Epic 6 Story 6.7 AC2; architecture.md §4.2 D4.3].

### AC3 — `CurrencyRegistry` extended with XAU (gold) + XAG (silver)

**Given** Story 0.1 + Story 2.1 established `CurrencyRegistry` with fiat currencies
**When** this story extends the registry
**Then** `CurrencyRegistry` adds:
- `XAU` (gold) — internal unit: **grams** (1 unit = 1 gram); display precision = 4 decimal places (e.g., `10.0000g`)
- `XAG` (silver) — internal unit: **grams**; display precision = 4 decimal places
**And** the registry's metal entries carry `IsMetal = true` to allow downstream code (Story 6.6 denomination integrity) to distinguish metal from fiat
**And** the registry's currency-code regex check constraint at the DB level (`char(3) check ~ '^[A-Z]{3}$'`) accepts XAU + XAG (already valid 3-letter codes per ISO 4217)
**And** an FsCheck round-trip test (per Story 2.1 pattern) extends to XAU + XAG [Source: epics.md Epic 6 Story 6.1; delegated decision: metals added to registry here].

### AC4 — Wolverine scheduled job: hourly cron

**Given** Wolverine supports scheduled jobs via `services.AddWolverine().UseScheduling(...)` per architecture §4.7
**When** the rate-feed job is registered
**Then** `Program.cs` includes:

```csharp
builder.Services.AddWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
    opts.UseEntityFrameworkCoreTransactions();

    // Schedule the hourly rate-feed pull
    opts.Schedules.AddJob<FetchExchangeRatesJob>().RunDaily(TimeSpan.Zero);
    // (Wolverine's scheduling API uses RunDaily/RunHourly helpers; the exact cron expression is "0 0 * * * *" — every hour on the hour)
});
```

**Note:** The exact Wolverine scheduling API surface evolved; the canonical hourly registration is via `opts.PublishMessage<TriggerRateFetch>().Schedule().Hourly()` or the `ScheduledJob` registration helper. Implementer chooses the current Wolverine 4.x API; the **invariant is hourly cron firing**.

**And** the job fires hourly on the hour (UTC reference)
**And** the next-fire time is recorded in App Insights for monitoring
**And** if a fire is missed (process restart, deploy), Wolverine's catch-up policy fires once on next start (default behavior; documented) [Source: epics.md Epic 6 Story 6.1; architecture.md §4.7 Wolverine scheduled jobs].

### AC5 — Provider configuration via env vars (runtime swap)

**Given** the delegated decision specifies provider config is runtime via env vars
**When** the job initializes
**Then** primary provider is `RATE_FEED_PRIMARY` env var (default: `metals-api.com`)
**And** secondary provider is `RATE_FEED_SECONDARY` env var (default: `goldapi.io`)
**And** API keys read from Key Vault Standard via `RATE_FEED_PRIMARY_API_KEY` + `RATE_FEED_SECONDARY_API_KEY` config keys (per ADR-025 + D4.8)
**And** both providers implement `IRateFeedProvider`:

```csharp
public interface IRateFeedProvider
{
    string Name { get; }  // for source_provider column + telemetry
    Task<IReadOnlyList<RateSnapshot>> FetchAsync(IReadOnlyList<string> targetCurrencies, CancellationToken ct);
}

public sealed record RateSnapshot(
    string DenominationFrom,  // always "XAU" gold-base per FR18
    string DenominationTo,    // e.g., "USD", "EUR", "XAG"
    decimal Rate,             // numeric(28,8) — gold-grams per unit of target
    DateTimeOffset ValidFrom);
```

**And** swapping a problem provider requires only an env-var change + restart (no deploy) [Source: delegated decisions; ADR-025; epics.md Epic 6 Story 6.2 AC4].

### AC6 — Fetches gold + silver + major currencies per NFR-I1

**Given** the job's target currency list
**When** the fetch executes
**Then** the request asks the provider for gold-base (XAU) prices for: USD, EUR, GBP, SAR, AED, TRY, EGP, JOD, KWD, BHD, TND, XAG (silver)
**And** XAU itself is implied (1g gold = 1g gold; rate = 1.0 row not stored — the registry knows this)
**And** if the provider returns a subset, missing currencies log a warning but do NOT fail the entire job
**And** rates are normalized as **gold-grams per unit of target** (so a `XAU → USD` row of `0.0108` means "1 USD = 0.0108g gold" or equivalently "1g gold = ~$92.50") [Source: epics.md Epic 6 Story 6.1 AC; prd.md NFR-I1].

### AC7 — Rates persisted with `valid_from` + `source_provider`

**Given** the fetched `RateSnapshot` list
**When** the job commits
**Then** each snapshot becomes a new row in `rates` with:
- `denomination_from = "XAU"` (always — gold is the base)
- `denomination_to = "USD"` (or whatever target)
- `rate = 0.01081...` (28,8 precision preserved)
- `valid_from` = the snapshot's timestamp from the provider (NOT `now()`)
- `source_provider` = provider name (e.g., `"metals-api.com"`)
- `recorded_at` = `now()` (DB default)
**And** all snapshots commit in a single `SaveChangesAsync` transaction
**And** Wolverine outbox enqueues a `RatesIngestedEvent` in the same transaction (downstream listeners include Story 6.8 freshness checker) [Source: epics.md Epic 6 Story 6.1 AC; architecture.md §4.4 D4.12].

### AC8 — Retry with exponential backoff + dead-letter on exhaustion

**Given** NFR-I2 mandates 3 retries with exponential backoff
**When** a provider request fails (HTTP error, timeout, malformed response)
**Then** the job retries with delays: 5s, 30s, 120s (exponential)
**And** if all 3 retries exhaust, the job enqueues to Wolverine's `dead_letter_messages` table per D4.12
**And** the failed-job event triggers an alert in App Insights (Golden-signals dashboard)
**And** the next scheduled fire (1 hour later) is NOT skipped — each cron tick is independent
**And** Wolverine replay tooling can re-execute dead-lettered jobs once root-cause is fixed [Source: epics.md Epic 6 Story 6.1 AC; prd.md NFR-I2; architecture.md §4.4 D4.12].

### AC9 — Same-transaction outbox safety on feed failures

**Given** a partial fetch (some currencies returned, others failed)
**When** the job commits what it has
**Then** ONLY the successfully-fetched rates persist
**And** the `RatesIngestedEvent` payload lists which currencies succeeded vs failed
**And** failed currencies are NOT retried within the same job execution — they wait for the next hourly tick
**And** no outbox publication occurs if zero rates were fetched (empty result is treated as full failure → goes to retry/dead-letter) [Source: epics.md Epic 6 Story 6.1 AC; architecture.md §4.4 D4.12].

### AC10 — Full processing completes in < 30s per NFR-P8

**Given** NFR-P8 specifies < 30s end-to-end
**When** the job runs
**Then** the wall-clock duration from cron-fire to outbox-commit is < 30s for the typical 12-currency fetch
**And** a Serilog timing log records actual duration
**And** if duration > 25s, a warning fires (early signal)
**And** if duration > 30s, an error fires (NFR breach)
**And** an integration test asserts < 30s against a mocked provider (full I/O path with WireMock cassettes from Story 6.9) [Source: prd.md NFR-P8].

### AC11 — Zero error-rate increase on user-facing API during feed

**Given** NFR-SC5 specifies API response times remain within targets during feed
**When** the rate-feed job is running
**Then** the job runs in a background scope (not on the request-handling thread pool)
**And** Postgres connection-pool sizing accommodates the feed's transient connections
**And** integration test (k6 + concurrent feed-trigger) asserts user-facing error rate stays < 0.1% during feed
**And** the App Insights dashboard tracks error-rate during/after feed for the silent-decay SLO [Source: prd.md NFR-SC5; architecture.md §4.9 silent-decay SLOs].

### AC12 — Telemetry events: `rate_feed_started`, `rate_feed_completed`, `rate_feed_failed`

**Given** AR-047 telemetry primitive is wired
**When** the job runs
**Then** three events emit:
- `rate_feed_started` — `{ provider_name, target_currencies, scheduled_at }`
- `rate_feed_completed` — `{ provider_name, currencies_fetched, currencies_failed, duration_ms, rate_count }`
- `rate_feed_failed` — `{ provider_name, error_class, retry_attempt, will_dead_letter: bool }`
**And** PII guard validates (no API keys, no IPs in payload)
**And** integration test helper asserts events emitted [Source: Story 0.14 telemetry primitive].

## Tasks / Subtasks

### Backend — Domain + Infrastructure

- [ ] **T1 (AC: 1, 2)** — Create migration `YYYYMMDDHHMMSS_AddRatesTable.cs`
  - [ ] Per AC1 SQL
  - [ ] Append-only triggers per AC2
  - [ ] Aspire MigrationService idempotent install pattern (per D4.3)
- [ ] **T2 (AC: 3)** — Extend `Faktuboh.Domain/Primitives/CurrencyRegistry.cs`
  - [ ] Add XAU + XAG entries with `IsMetal = true`
  - [ ] Display precision = 4 decimals (grams)
  - [ ] Update FsCheck round-trip test to include metals
- [ ] **T3 (AC: 1, 7)** — Create `Faktuboh.Domain/Rates/RateSnapshot.cs` (record)
  - [ ] Fields per AC5
  - [ ] Validation: `From != To`, `Rate > 0`, `From in CurrencyRegistry`, `To in CurrencyRegistry`
- [ ] **T4 (AC: 1, 7)** — Create `Faktuboh.Infrastructure/Persistence/Configurations/RateConfiguration.cs`
  - [ ] EF Core entity config: `numeric(28,8)` on `rate`, `timestamptz` on `valid_from`/`recorded_at`
  - [ ] Indexes per AC1

### Backend — Provider abstraction

- [ ] **T5 (AC: 5)** — Create `Faktuboh.Infrastructure/Rates/IRateFeedProvider.cs`
  - [ ] Per AC5 interface shape
- [ ] **T6 (AC: 5, 6)** — Create `MetalsApiProvider` implementing `IRateFeedProvider`
  - [ ] Reads API key from configuration (`RATE_FEED_PRIMARY_API_KEY`)
  - [ ] Calls `https://metals-api.com/api/latest?access_key={key}&base=XAU&symbols=USD,EUR,GBP,SAR,AED,TRY,EGP,JOD,KWD,BHD,TND,XAG`
  - [ ] Maps response to `RateSnapshot[]`
  - [ ] Throws `RateFeedTransientException` on HTTP 5xx / timeout (triggers retry)
  - [ ] Throws `RateFeedAuthenticationException` on 401/403 (no retry — fast-fail)
- [ ] **T7 (AC: 5, 6)** — Create `GoldApiProvider` implementing `IRateFeedProvider` (gold-only redundancy)
  - [ ] Reads API key from configuration (`RATE_FEED_SECONDARY_API_KEY`)
  - [ ] Calls `https://www.goldapi.io/api/XAU/USD` (per-pair endpoint; iterate for all targets)
  - [ ] Same exception types as T6
- [ ] **T8 (AC: 5)** — DI registration via factory
  - [ ] Resolve `IRateFeedProvider` based on `RATE_FEED_PRIMARY` env var
  - [ ] Story 6.2 will add the failover wrapper

### Backend — Wolverine scheduled job

- [ ] **T9 (AC: 4, 7, 8, 9, 10, 11, 12)** — Create `Faktuboh.Infrastructure/Rates/FetchExchangeRatesJob.cs`
  - [ ] Wolverine handler discovered via `Handle` method
  - [ ] Accepts `RateFeedTrigger` message (the scheduled message type)
  - [ ] Calls `IRateFeedProvider.FetchAsync(...)` with full target list
  - [ ] Persists snapshots via `IRateRepository.AddRangeAsync` (single-transaction)
  - [ ] Publishes `RatesIngestedEvent` via outbox in same transaction
  - [ ] Wraps with retry-policy attribute (or Polly): 3 retries, exponential 5s/30s/120s
  - [ ] Telemetry per AC12
  - [ ] Timing instrumentation per AC10
- [ ] **T10 (AC: 4)** — Register hourly schedule in `Program.cs`
  - [ ] `opts.Schedules.AddJob<RateFeedTrigger>().Hourly()` (or equivalent Wolverine 4.x API)
  - [ ] Catch-up policy: fire once on next start if missed
- [ ] **T11 (AC: 7)** — Create `Faktuboh.Domain.Events.IntegrationEvents/RatesIngestedEvent.cs`
  - [ ] Payload: `{ ProviderName, ValidFrom, CurrenciesIngested[], CurrenciesFailed[] }`

### Backend — Repository + querying

- [ ] **T12 (AC: 7)** — Create `Faktuboh.Infrastructure/Rates/RateRepository.cs : IRateRepository`
  - [ ] `AddRangeAsync(IReadOnlyList<RateSnapshot>)` — bulk insert
  - [ ] `GetLatestAsync(string from, string to)` — for Story 6.3 + 6.4
  - [ ] `GetAtAsync(string from, string to, DateTimeOffset asOf)` — for Story 6.5

### Backend — Configuration + secrets

- [ ] **T13 (AC: 5)** — Add config keys to Key Vault
  - [ ] `rate-feed-primary-api-key` (metals-api.com)
  - [ ] `rate-feed-secondary-api-key` (goldapi.io)
  - [ ] Document in `docs/ops/rate-feed-providers.md`
- [ ] **T14 (AC: 5)** — Add env vars to Aspire ServiceDefaults config
  - [ ] `RATE_FEED_PRIMARY=metals-api.com` (default)
  - [ ] `RATE_FEED_SECONDARY=goldapi.io` (default)

### Backend — Tests

- [ ] **T15 (AC: 1, 2)** — Migration tests
  - [ ] Apply migration to Testcontainers Postgres; assert table shape + constraints
  - [ ] Attempt `UPDATE rates` — assert exception
  - [ ] Attempt `DELETE FROM rates` — assert exception
- [ ] **T16 (AC: 3)** — `CurrencyRegistry` tests
  - [ ] XAU + XAG entries present
  - [ ] FsCheck round-trip extended to metals
- [ ] **T17 (AC: 4, 7, 8, 9, 10)** — Integration test `FetchExchangeRatesJobTests`
  - [ ] WireMock cassette for happy-path provider response (Story 6.9 cassettes)
  - [ ] Assert rates persisted with correct shape
  - [ ] Assert outbox event fires
  - [ ] Failure cassette (5xx) → assert 3 retries → dead-letter
  - [ ] Partial-success cassette → assert outbox lists succeeded + failed
  - [ ] Timing assertion < 30s
- [ ] **T18 (AC: 11)** — k6 + concurrent feed integration test
  - [ ] Background feed running while user-facing API serves; assert error rate < 0.1%
- [ ] **T19 (AC: 12)** — Telemetry assertion tests
  - [ ] `AssertEventEmitted("rate_feed_started"/"rate_feed_completed"/"rate_feed_failed")` per scenario

### Documentation

- [ ] **T20** — Create `docs/ops/rate-feed-providers.md`
  - [ ] Provider list + endpoints + API-key custody
  - [ ] Env-var swap procedure
  - [ ] Dead-letter replay runbook

## Dev Notes

### Critical guardrails (do not violate)

1. **`numeric(28, 8)` for `rate` column.** Per AR-035 dual-precision rule. The schema-audit CI gate enforces. Using `numeric(19, 4)` here would silently corrupt FX pivot math at scale.
2. **`rates` table is APPEND-ONLY.** Postgres trigger blocks UPDATE/DELETE. Story 6.7 makes this an explicit AC; this story implements it forward-stamped. Mutating a rate retroactively is the canonical fintech audit catastrophe.
3. **Gold-base only.** Every row's `denomination_from = "XAU"`. Cross-currency rates are derived (XAU→USD ÷ XAU→EUR = USD/EUR). This avoids quadratic explosion of the table (12 currencies × 12 = 144 rows per fetch became 12 rows).
4. **Provider config via env vars, not appsettings.** A failing provider must be swappable without redeploy. Env-var swap + container restart is the operational pattern.
5. **API keys via Key Vault Standard.** Per ADR-025. Never hardcoded, never in appsettings.json, never in source control.
6. **Same-transaction outbox.** `SaveChangesAsync` commits rates + `RatesIngestedEvent` enqueue atomically. No event publication on aborted transaction.
7. **Banker's rounding.** All `Math.Round` calls explicit `MidpointRounding.ToEven` (Story 2.1 guardrail).
8. **Wolverine catch-up policy: fire once on next start if missed.** A missed hourly tick (process restart) does NOT fire 24× to "catch up overnight" — single fire on resumption.
9. **Background scope, not request thread pool.** Feed runs on Wolverine's dedicated workers; user-facing requests are unaffected.
10. **Retry classification: transient vs auth.** 5xx and timeouts retry; 401/403 fast-fail (don't waste retries on a misconfigured key).

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Domain/
│   ├── Primitives/CurrencyRegistry.cs                      # MODIFY (+ XAU, XAG)
│   ├── Rates/
│   │   ├── RateSnapshot.cs                                 # NEW
│   │   ├── IRateRepository.cs                              # NEW
│   │   └── Rate.cs                                         # NEW (entity)
│   └── Events/IntegrationEvents/
│       └── RatesIngestedEvent.cs                           # NEW
├── Faktuboh.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/RateConfiguration.cs             # NEW
│   │   └── Migrations/YYYYMMDDHHMMSS_AddRatesTable.cs      # NEW
│   ├── Rates/
│   │   ├── IRateFeedProvider.cs                            # NEW
│   │   ├── MetalsApiProvider.cs                            # NEW
│   │   ├── GoldApiProvider.cs                              # NEW
│   │   ├── FetchExchangeRatesJob.cs                        # NEW
│   │   ├── RateFeedTrigger.cs                              # NEW (scheduled message)
│   │   ├── RateFeedTransientException.cs                   # NEW
│   │   ├── RateFeedAuthenticationException.cs              # NEW
│   │   └── RateRepository.cs                               # NEW
│   └── Persistence/Triggers/
│       └── RatesAppendOnlyTrigger.sql                      # NEW (referenced by migration)
└── Faktuboh.Api/Program.cs                                 # MODIFY (Wolverine schedule registration)

backend/tests/
├── Faktuboh.Infrastructure.Tests/Rates/
│   ├── FetchExchangeRatesJobTests.cs                       # NEW (Integration; WireMock)
│   └── RatesAppendOnlyTriggerTests.cs                      # NEW (Integration)
└── Faktuboh.Domain.Tests/Primitives/
    └── CurrencyRegistryMetalsTests.cs                      # NEW (FsCheck)

docs/ops/
└── rate-feed-providers.md                                  # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Provider mapping + retry classification | xUnit v3 + NSubstitute |
| Integration | Full job + WireMock provider + Postgres + outbox | Testcontainers + WireMock.NET (Story 6.9) |
| Property | CurrencyRegistry round-trip incl. metals | FsCheck |
| Migration | Append-only trigger blocks UPDATE/DELETE | Testcontainers |
| Performance | < 30s end-to-end | Integration test timing |
| Concurrency | User-facing error rate < 0.1% during feed | k6 mini-scenario |

**Forbidden:** Moq, EF Core in-memory, hardcoded API keys, in-memory provider for any rate fetch, raw arithmetic on rate decimals (use Money helpers), `numeric(19,4)` for rate columns.

### Project Structure Notes

This story scaffolds the `Faktuboh.Infrastructure/Rates/` folder. Stories 6.2 (failover wrapper) and 6.3 (current-rate query endpoint) build on this. Story 6.7 (historical storage) is mostly a verification story since this story already implements append-only. Story 6.9 (cassette harness) provides the test fixtures used by T17.

### Architecture compliance

- **AR-006 + FR18** — hourly fetch with gold base.
- **NFR-I1** — gold + silver + USD/EUR/GBP/SAR/AED/TRY supported.
- **NFR-I2** — 3 retries with exponential backoff.
- **NFR-P8** — < 30s.
- **NFR-SC5** — zero error-rate increase during feed.
- **AR-035 + §5.2.5** — `numeric(28, 8)` for FX rate columns.
- **§4.7** — Wolverine scheduled jobs.
- **§4.4 D4.12** — same-transaction outbox.
- **D4.3** — Aspire MigrationService idempotent triggers.
- **ADR-025 + D4.8** — API keys in Key Vault Standard.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| .NET | 10 LTS | |
| Messaging / Scheduling | Wolverine | Postgres outbox + scheduled jobs |
| ORM | EF Core 10 | `numeric(28,8)` mapping |
| HTTP client | `IHttpClientFactory` | Per-provider client + retry policy |
| Resilience | Polly (or Wolverine retry) | 3-retry exponential backoff |
| Test | xUnit v3 + Testcontainers + WireMock.NET | Story 6.9 cassettes |
| Property | FsCheck | CurrencyRegistry round-trip |

### Testing requirements

- **Migration:** schema + trigger behavior.
- **Domain:** CurrencyRegistry metals.
- **Integration:** full job lifecycle + retry + dead-letter + outbox.
- **Performance:** < 30s; user-facing error rate during feed.
- **Telemetry:** events emitted with PII guard.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.1 — `CurrencyRegistry` (extended here for metals)
- Story 0.5 — Idempotency-Key (not directly used; pattern referenced)
- Story 0.6 — `IBitemporal` (NOT applied to rates — rates are append-only via trigger, not via bitemporal interceptor)
- Story 0.10 — Azure infra (Key Vault Standard provisioned)
- Story 0.11 — App Insights (telemetry pipeline ready)
- Story 0.14 — telemetry primitive
- Story 2.1 — Money + FsCheck round-trip pattern

**Downstream consumers:**
- Story 6.2 — dual-provider failover wrapper around T6/T7
- Story 6.3 — `GET /v1/rates/current` reads `rates` table
- Story 6.4 — Exchange operation locks rate from `rates` table
- Story 6.5 — historical rate query
- Story 6.7 — formal append-only contract (this story implements forward-stamped)
- Story 6.8 — freshness SLA reads `recorded_at`
- Story 6.9 — WireMock cassettes test against this story's job

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.1
- FR18, NFR-I1, NFR-I2, NFR-P8, NFR-SC5 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Dual-precision rule (AR-035) — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- Wolverine scheduled jobs — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.7 (and §4.4 D4.12 outbox)
- Key Vault — [_bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md](../planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md)
- Story 0.1 CurrencyRegistry — [_bmad-output/implementation-artifacts/0-1-backend-solution-scaffold.md](./0-1-backend-solution-scaffold.md)
- Story 6.7 historical storage — [_bmad-output/implementation-artifacts/6-7-historical-rate-storage-for-audit.md](./6-7-historical-rate-storage-for-audit.md)
- Story 6.9 WireMock harness — [_bmad-output/implementation-artifacts/6-9-wiremock-cassette-harness-for-feed-scenario-tests.md](./6-9-wiremock-cassette-harness-for-feed-scenario-tests.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Heaviest Epic 6 story: foundational `rates` table + dual-precision + append-only + Wolverine schedule + dual-provider plumbing (failover wrapper in 6.2).
- `CurrencyRegistry` extends here to include XAU + XAG metals.
- Append-only trigger forward-stamps Story 6.7 contract.
- Gold-base normalization avoids quadratic table size.

### File List

_Populated during implementation._
