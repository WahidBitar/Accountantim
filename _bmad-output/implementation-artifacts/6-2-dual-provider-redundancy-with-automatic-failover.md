# Story 6.2: Dual-provider redundancy with automatic failover

Status: ready-for-dev

## Story

As **Wahid**,
I want **the gold-base pricing to use two independent providers (primary + warm secondary) with automatic failover within 60 minutes of primary failure**,
so that **gold-base rate availability meets NFR-I6 on MVP Day 1**.

**Covers requirements:** Epic 6 Story 6.2 — NFR-I6 (dual-provider redundancy for gold-base; failover ≤ 60 min); Story 6.1 provider plumbing (extended here); reconciliation alert on divergence; runtime config swap [Source: epics.md Epic 6 Story 6.2; prd.md NFR-I6].

**Why this story now:** Story 6.1 implements primary + secondary providers as separate `IRateFeedProvider` instances. This story adds the **failover wrapper + reconciliation logic + alerting**. Without it, a primary outage silently degrades the rate-feed (no fresh rates after primary fails) — visible only when Story 6.8's freshness banner trips at 4h. NFR-I6 demands a 60-minute failover window, so this story is required for MVP shipment.

## Acceptance Criteria

### AC1 — `FailoverRateFeedProvider` wraps primary + secondary

**Given** Story 6.1 ships `MetalsApiProvider` (primary) and `GoldApiProvider` (secondary) implementing `IRateFeedProvider`
**When** this story adds the failover wrapper
**Then** `FailoverRateFeedProvider : IRateFeedProvider` is registered as the default DI binding for `IRateFeedProvider`
**And** the wrapper holds two underlying providers (primary + secondary) injected via keyed DI:
```csharp
public sealed class FailoverRateFeedProvider(
    [FromKeyedServices("primary")] IRateFeedProvider primary,
    [FromKeyedServices("secondary")] IRateFeedProvider secondary,
    IFailoverState state,
    ILogger<FailoverRateFeedProvider> logger,
    ITelemetryEmitter telemetry) : IRateFeedProvider { ... }
```
**And** the wrapper's `Name` property returns the active provider's name (changes during failover) [Source: epics.md Epic 6 Story 6.2; Story 6.1 provider abstraction].

### AC2 — Automatic failover after 3 consecutive primary failures OR 60-min outage

**Given** the failover trigger conditions per NFR-I6
**When** the wrapper executes
**Then** failover state machine:
- State `PrimaryActive` (default): all requests go to primary
- On primary failure: increment `consecutiveFailures` counter
- Transition to `SecondaryActive` when `consecutiveFailures >= 3` OR `(now - primaryLastSuccessAt) >= 60.minutes`
- State `SecondaryActive`: all requests go to secondary
- Periodic primary health check every 15 minutes; on success, transition back to `PrimaryActive`
**And** state is persisted in a `failover_state` table (single row keyed by feed name) so restarts don't lose the state
**And** integration test asserts the 3-failure trigger + 60-min trigger separately [Source: epics.md Epic 6 Story 6.2 AC; prd.md NFR-I6].

### AC3 — Failover fires dashboard alert

**Given** failover is operationally significant
**When** the wrapper transitions to `SecondaryActive`
**Then** an App Insights alert fires:
- Alert name: `RateFeed.PrimaryProviderUnhealthy`
- Severity: `Warning`
- Payload: `{ from_provider, to_provider, trigger_reason: "consecutive_failures" | "outage_duration", failure_count, last_success_at }`
**And** an alert ALSO fires on transition back to `PrimaryActive` (informational, signals recovery)
**And** the alert routes to the configured ops channel (initially: email-only at MVP per architecture §4.6 D4.20) [Source: epics.md Epic 6 Story 6.2 AC; architecture.md §4.6 D4.20].

### AC4 — Reconciliation: compare primary + secondary rates when both healthy

**Given** rate divergence between providers signals a data-quality issue
**When** both providers are healthy AND a fetch is due
**Then** every Nth fetch (default: every 6th, i.e., every 6 hours), the wrapper fetches from BOTH providers in parallel
**And** for each currency present in BOTH responses, computes `divergence = |primary_rate - secondary_rate| / primary_rate`
**And** if `divergence > 0.05` (5%) for any currency, fires alert `RateFeed.ProviderDivergence`:
- Severity: `Warning`
- Payload: `{ currency, primary_rate, secondary_rate, divergence_pct }`
**And** if `divergence > 0.20` (20%), fires `Critical` alert + the wrapper REFUSES to commit either rate (writes nothing, retries next hour)
**And** the integration test asserts both threshold paths via WireMock cassettes (Story 6.9) [Source: epics.md Epic 6 Story 6.2 AC2].

### AC5 — Non-gold currencies may use single provider per NFR-I6

**Given** NFR-I6 documents that non-gold rates may share a single provider at MVP
**When** the secondary provider only supports gold (e.g., `goldapi.io` doesn't return EUR/GBP)
**Then** the wrapper's failover ONLY covers the gold-base XAU rate
**And** non-gold currencies (USD, EUR, GBP, etc.) are still fetched from the primary; if primary is down, they are missing from this fetch (Story 6.8 freshness banner handles)
**And** the documented revisit trigger: "When sustained MAU > 5,000 OR primary provider has > 2 outages per quarter, onboard secondary provider for non-gold currencies" [Source: prd.md NFR-I6].

### AC6 — Provider config runtime via env vars (no deploy required)

**Given** the delegated decision says provider config is runtime
**When** an operator wants to swap a problem provider
**Then** changing `RATE_FEED_PRIMARY` (e.g., `metals-api.com` → `metals.live`) + restarting the container is sufficient
**And** the keyed DI registration reads the env var at startup
**And** documentation in `docs/ops/rate-feed-providers.md` (Story 6.1 T20) documents the swap procedure
**And** an integration test asserts the wrapper resolves the right concrete provider based on env-var value [Source: delegated decisions; epics.md Epic 6 Story 6.2 AC4].

### AC7 — Failover state persisted in `failover_state` Postgres table

**Given** restarts must not reset failover state
**When** the migration `YYYYMMDDHHMMSS_AddFailoverStateTable.cs` runs
**Then** the table has columns:
```sql
CREATE TABLE failover_state (
    feed_name text PRIMARY KEY,                -- e.g., 'rates'
    active_provider_name text NOT NULL,
    state text NOT NULL CHECK (state IN ('PrimaryActive', 'SecondaryActive')),
    consecutive_failures int NOT NULL DEFAULT 0,
    primary_last_success_at timestamptz NULL,
    state_changed_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO failover_state (feed_name, active_provider_name, state, primary_last_success_at)
VALUES ('rates', 'metals-api.com', 'PrimaryActive', now());
```
**And** the `IFailoverState` interface reads + updates this row atomically (row-level lock during state transitions)
**And** integration test asserts state survives a process restart [Source: epics.md Epic 6 Story 6.2 AC; architecture.md §4.4 D4.12 outbox semantics].

### AC8 — Primary health-check job (15-min cron)

**Given** failover should auto-recover
**When** the wrapper is in `SecondaryActive`
**Then** a Wolverine scheduled job `PrimaryHealthCheckJob` runs every 15 minutes:
- Fetches a single currency (XAU→USD) from primary via a lightweight call
- On success: transitions state to `PrimaryActive` + alert "primary recovered"
- On failure: leaves state as-is; logs the attempt
**And** the health-check is SKIPPED when state is `PrimaryActive` (no need to check what's working)
**And** the job's telemetry emits `rate_feed_primary_healthcheck` events [Source: epics.md Epic 6 Story 6.2; AC2 transition logic].

### AC9 — Telemetry: failover events

**Given** AR-047 telemetry primitive
**When** state transitions occur
**Then** events emit:
- `rate_feed_failover_to_secondary` — `{ trigger_reason, failure_count, from_provider, to_provider }`
- `rate_feed_failover_to_primary` — `{ from_provider, to_provider, recovery_duration_min }`
- `rate_feed_divergence_detected` — `{ currency, primary_rate, secondary_rate, divergence_pct, threshold_breached }`
**And** PII guard validates [Source: Story 0.14].

### AC10 — Property-based test on reconciliation arbiter

**Given** Story 6.9 (WireMock cassette harness) requires a property-based test on the dual-provider reconciliation arbiter
**When** the test runs
**Then** FsCheck generators produce `(primaryResponse, secondaryResponse)` pairs across the divergence range
**And** the property asserts:
- For divergence < 5%, both rates accepted (primary wins; secondary discarded)
- For 5% ≤ divergence < 20%, alert fires + primary wins
- For divergence ≥ 20%, no rate persisted + critical alert fires
- For one-sided unavailability (primary fails, secondary succeeds), failover triggers correctly [Source: epics.md Epic 6 Story 6.9 AC; this story's reconciliation logic].

### AC11 — Failover happens within 60 minutes of primary failure (NFR-I6)

**Given** NFR-I6 specifies failover within 60 minutes
**When** the primary becomes unavailable
**Then** the worst-case time to failover is:
- 3 consecutive failures × 1-hour cron = 3 hours (CONFLICTS with 60-min NFR)
- OR `(now - primaryLastSuccessAt) >= 60.minutes` triggers AT the next cron tick
**And** since the cron is hourly, the SECOND failed cron tick (at ~1h after first failure) triggers the 60-min outage path
**And** documentation flag: 60-min failover IS achievable via the outage-duration trigger; the 3-consecutive-failures trigger may take up to 3h. Both triggers are active for defense-in-depth.
**And** integration test asserts: with primary failing on every call, failover triggers no later than the second cron fire after `primaryLastSuccessAt + 60.minutes` elapses

## Tasks / Subtasks

### Backend — Failover wrapper

- [ ] **T1 (AC: 1)** — Create `Faktuboh.Infrastructure/Rates/FailoverRateFeedProvider.cs`
  - [ ] Per AC1 constructor signature
  - [ ] Implements `IRateFeedProvider`
  - [ ] State-machine logic per AC2
- [ ] **T2 (AC: 1, 6)** — Configure keyed DI in `Program.cs`
  - [ ] `services.AddKeyedScoped<IRateFeedProvider, MetalsApiProvider>("primary")` (or factory based on `RATE_FEED_PRIMARY`)
  - [ ] `services.AddKeyedScoped<IRateFeedProvider, GoldApiProvider>("secondary")`
  - [ ] `services.AddScoped<IRateFeedProvider, FailoverRateFeedProvider>()` (default binding)

### Backend — State persistence

- [ ] **T3 (AC: 7)** — Create migration `YYYYMMDDHHMMSS_AddFailoverStateTable.cs`
  - [ ] Per AC7 SQL
  - [ ] Seed row for `'rates'` feed
- [ ] **T4 (AC: 7)** — Create `IFailoverState` + `FailoverState` repository
  - [ ] `Faktuboh.Infrastructure/Rates/FailoverState.cs`
  - [ ] Methods: `GetAsync(string feedName)`, `RecordFailureAsync`, `RecordSuccessAsync`, `TransitionAsync(state, reason)`
  - [ ] Atomic updates via Postgres row-level lock

### Backend — Reconciliation

- [ ] **T5 (AC: 4, 10)** — Implement reconciliation logic in `FailoverRateFeedProvider`
  - [ ] Every 6th fetch, dual-fetch in parallel
  - [ ] Compute divergence per currency
  - [ ] Threshold-based alert + persist-or-skip decision
- [ ] **T6 (AC: 4)** — Create `ReconciliationArbiter` service (extracted from wrapper for testability)
  - [ ] Pure function: `Arbitrate(primaryResponse, secondaryResponse) → ArbitrationResult`

### Backend — Health check

- [ ] **T7 (AC: 8)** — Create `Faktuboh.Infrastructure/Rates/PrimaryHealthCheckJob.cs`
  - [ ] 15-min cron via Wolverine
  - [ ] Skips when state is `PrimaryActive`
  - [ ] Lightweight single-currency fetch
- [ ] **T8 (AC: 8)** — Register in `Program.cs`
  - [ ] `opts.Schedules.AddJob<PrimaryHealthCheckTrigger>().Every(TimeSpan.FromMinutes(15))`

### Backend — Alerts

- [ ] **T9 (AC: 3, 4)** — Create `IRateFeedAlertSink` abstraction
  - [ ] Default impl: writes to App Insights via `ITelemetryEmitter` + sends email via Resend (per ADR-023)
  - [ ] Methods: `FireFailoverAlert`, `FireRecoveryAlert`, `FireDivergenceAlert`
- [ ] **T10 (AC: 3, 4)** — Register alert routes in App Insights config
  - [ ] Alert rule: `customEvents | where name == "rate_feed_failover_to_secondary" | summarize count() by bin(timestamp, 5m) | where count_ > 0`

### Tests

- [ ] **T11 (AC: 1, 2, 7)** — Unit tests on `FailoverRateFeedProvider` state-machine
  - [ ] Mock primary + secondary
  - [ ] Assert 3-failure trigger
  - [ ] Assert 60-min outage trigger
  - [ ] Assert health-check transition back
  - [ ] State-survival across simulated restart
- [ ] **T12 (AC: 4, 10)** — FsCheck property test on `ReconciliationArbiter`
  - [ ] Per AC10 generator + assertions
- [ ] **T13 (AC: 1-9)** — Integration test `FailoverIntegrationTests` using WireMock cassettes (Story 6.9)
  - [ ] Cassette: primary returns 5xx 3 times → assert failover transition
  - [ ] Cassette: primary unreachable for >60min → assert failover via outage trigger
  - [ ] Cassette: divergence cassette → assert threshold logic
  - [ ] Cassette: primary recovers → assert transition back via health check
- [ ] **T14 (AC: 11)** — NFR-I6 compliance test
  - [ ] Assert worst-case failover time ≤ 60 minutes via outage-duration trigger

### Documentation

- [ ] **T15 (AC: 6)** — Update `docs/ops/rate-feed-providers.md` (Story 6.1 T20)
  - [ ] Failover behavior + state machine
  - [ ] Env-var swap procedure
  - [ ] Alert routing + on-call response

## Dev Notes

### Critical guardrails (do not violate)

1. **State persisted across restarts.** A pod recycle that resets failover state would re-test a known-bad primary on every restart — the `failover_state` table prevents this.
2. **Health check skipped when primary is active.** No need to probe what's working; the regular hourly fetch is the implicit health check during `PrimaryActive`.
3. **20% divergence = NO RATE PERSISTED.** Defensive default — better to be stale than wrong. Story 6.8's freshness banner handles user-visible degradation.
4. **Non-gold redundancy is OUT OF SCOPE.** Per NFR-I6 documented exception. The revisit trigger documents when to revisit (sustained MAU > 5K or > 2 outages/quarter).
5. **60-min outage trigger is the load-bearing path** for the NFR-I6 commitment. The 3-consecutive-failures trigger is defense-in-depth for fast failover during burst failures, but the OUTAGE trigger is what makes the 60-min NFR hold.
6. **Alert deduplication.** Don't re-fire `rate_feed_failover_to_secondary` on every subsequent failed cron tick after failover. Fire once on transition; resume on transition back.
7. **Reconciliation is COSTLY (2× provider quota).** Run every 6th fetch (configurable), not every fetch. The default is calibrated to balance signal quality vs API quota.
8. **Provider config via env var.** Operator response time on a bad provider is minutes (env-var change + restart) vs hours (PR + deploy). Don't bury config in appsettings.json.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Infrastructure/Rates/
│   ├── FailoverRateFeedProvider.cs                         # NEW
│   ├── FailoverState.cs                                    # NEW
│   ├── IFailoverState.cs                                   # NEW
│   ├── ReconciliationArbiter.cs                            # NEW
│   ├── PrimaryHealthCheckJob.cs                            # NEW
│   ├── PrimaryHealthCheckTrigger.cs                        # NEW
│   ├── IRateFeedAlertSink.cs                               # NEW
│   └── RateFeedAlertSink.cs                                # NEW
├── Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDDHHMMSS_AddFailoverStateTable.cs             # NEW
└── Faktuboh.Api/Program.cs                                 # MODIFY (keyed DI + health-check schedule)

backend/tests/
├── Faktuboh.Infrastructure.Tests/Rates/
│   ├── FailoverRateFeedProviderTests.cs                    # NEW
│   ├── ReconciliationArbiterPropertyTests.cs               # NEW (FsCheck)
│   ├── FailoverStateTests.cs                               # NEW (Integration)
│   ├── PrimaryHealthCheckJobTests.cs                       # NEW
│   └── FailoverIntegrationTests.cs                         # NEW (WireMock cassettes)

docs/ops/
└── rate-feed-providers.md                                  # MODIFY
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | State machine + arbiter | xUnit + NSubstitute |
| Property | Reconciliation arbiter | FsCheck |
| Integration | Failover scenarios via cassettes | Testcontainers + WireMock.NET (Story 6.9) |
| NFR | 60-min failover compliance | Integration timing |

### Architecture compliance

- **NFR-I6** — dual-provider redundancy + 60-min failover.
- **§4.4 D4.12** — Wolverine outbox + state persistence patterns.
- **§4.6 D4.20** — App Insights alerting.
- **ADR-023** — Resend for email alerts.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| DI | Microsoft.Extensions.DependencyInjection | Keyed services |
| Resilience | Polly (or Wolverine retry) | Reused from Story 6.1 |
| Test | WireMock.NET | Story 6.9 cassettes |

### Testing requirements

- **Unit:** state machine + arbiter (pure-function tests).
- **Property:** arbiter across the divergence range.
- **Integration:** full failover lifecycle via cassettes.
- **NFR:** 60-min trigger compliance.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.10 — Azure infra (alert routing configured)
- Story 0.14 — telemetry primitive
- Story 6.1 — provider abstraction + Wolverine schedule

**Coordination:**
- Story 6.9 ships the WireMock cassettes used by T13 — coordinate cassette names early.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.2
- NFR-I6 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 6.1 rate-feed — [_bmad-output/implementation-artifacts/6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md](./6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md)
- Story 6.9 cassettes — [_bmad-output/implementation-artifacts/6-9-wiremock-cassette-harness-for-feed-scenario-tests.md](./6-9-wiremock-cassette-harness-for-feed-scenario-tests.md)
- App Insights alerting — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.6 D4.20

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- 60-min outage trigger is the load-bearing path for NFR-I6.
- 20% divergence = no persistence (defensive default; staleness preferred to wrongness).
- Failover state persisted in dedicated table; survives restarts.
- Reconciliation runs every 6th fetch to balance signal vs API quota.

### File List

_Populated during implementation._
