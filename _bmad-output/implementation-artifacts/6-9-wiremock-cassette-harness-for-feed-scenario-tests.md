# Story 6.9: WireMock cassette harness for feed-scenario tests

Status: ready-for-dev

## Story

As **Wahid**,
I want **a WireMock.NET cassette harness simulating 8-12 rate-feed failure scenarios (blackout, partial blackout, divergence above/below threshold, duplicate timestamp, out-of-order timestamp, stale 4h/24h, schema drift, auth failure)**,
so that **AR-044 #3 rate-feed rehearsal can run in CI without hitting live providers** (Murat's commitment).

**Covers requirements:** Epic 6 Story 6.9 — AR-044 #3 (rate-feed blackout + divergence drill, in §2.10 rehearsal #3); Pact consumer-side drift tests for both providers; FsCheck property test on dual-provider reconciliation arbiter; `numeric(28,8)` round-trip precision tests across currency conversion chains [Source: epics.md Epic 6 Story 6.9; architecture.md §2.10 rehearsal #3].

**Why this story now:** Stories 6.1, 6.2, 6.4 all depend on a deterministic test harness for rate-feed behavior. Hitting metals-api.com from CI is fragile (rate limits, network flakes, cost). WireMock.NET cassettes give us the same coverage with deterministic playback. This story lands the cassettes used by all prior Epic 6 integration tests.

## Acceptance Criteria

### AC1 — Cassette harness directory + base infrastructure

**Given** the harness needs a structured location
**When** this story creates the infrastructure
**Then** `tests/Faktuboh.Infrastructure.Tests/RateFeedCassettes/` exists with:
- `RateFeedCassetteFixture.cs` — xUnit fixture managing WireMock.NET server lifecycle
- `cassettes/` subdirectory with one JSON file per scenario
- `RateFeedCassetteLoader.cs` — helper to load + apply cassettes to a WireMock server
**And** the fixture starts WireMock on a random port; the test injects the URL into `IHttpClientFactory` via test-scoped DI override
**And** cassettes are committed to the repo (NOT generated at runtime — deterministic by design) [Source: epics.md Epic 6 Story 6.9 AC1].

### AC2 — Cassette: provider-blackout (full outage)

**Given** the primary provider is down
**When** the cassette `cassettes/blackout.json` is applied
**Then** WireMock returns HTTP `503 Service Unavailable` for ALL requests to `/api/latest`
**And** the integration test asserts:
- 3 retries fire with exponential backoff (5s, 30s, 120s)
- Job enqueues to `dead_letter_messages` after exhaustion
- `rate_feed_failed` telemetry fires per retry [Source: epics.md Epic 6 Story 6.9 AC; Story 6.1 AC8].

```json
// cassettes/blackout.json (skeleton)
{
  "name": "blackout",
  "description": "Primary provider returns 503 for all requests",
  "mappings": [
    {
      "request": { "method": "GET", "urlPattern": "/api/latest.*" },
      "response": { "status": 503, "body": "Service Unavailable" }
    }
  ]
}
```

### AC3 — Cassette: partial-blackout (some currencies missing)

**Given** the provider returns a subset of requested currencies
**When** the cassette `cassettes/partial-blackout.json` is applied
**Then** WireMock returns HTTP 200 with rates for `["USD", "EUR"]` but missing `["GBP", "SAR", "AED", "TRY", "EGP", "JOD", "KWD", "BHD", "TND", "XAG"]`
**And** the integration test asserts:
- Only USD + EUR rates persist
- `RatesIngestedEvent` payload lists succeeded vs failed currencies
- No retry triggered (HTTP 200 = success)
- `rate_feed_completed` telemetry fires with `currencies_failed` listing 10 missing [Source: Story 6.1 AC9].

### AC4 — Cassette: divergence above 5% (warning threshold)

**Given** primary returns USD = $92.50, secondary returns USD = $96.00 (3.78% divergence — below 5%, OK)
**Given** another scenario: primary = $92.50, secondary = $97.50 (5.4% — above 5% warning threshold)
**When** cassettes `cassettes/divergence-below-5pct.json` + `cassettes/divergence-above-5pct.json` apply
**Then** below-5% scenario: rates persist, no alert fires
**And** above-5% scenario: primary rate persists (winner), `rate_feed_divergence_detected` alert fires at warning severity [Source: Story 6.2 AC4].

### AC5 — Cassette: divergence above 20% (critical, no persistence)

**Given** primary = $92.50, secondary = $115.00 (24.3% divergence — above 20% critical)
**When** cassette `cassettes/divergence-above-20pct.json` applies
**Then** NEITHER rate persists
**And** `rate_feed_divergence_detected` alert fires at critical severity
**And** the integration test asserts zero new rows in `rates` table after job execution [Source: Story 6.2 AC4 critical path].

### AC6 — Cassette: duplicate timestamp

**Given** the provider returns a rate with `valid_from` identical to an already-persisted row
**When** cassette `cassettes/duplicate-timestamp.json` applies
**Then** the test asserts:
- Either: a unique-constraint violation is raised, OR
- The duplicate is silently skipped (idempotent insert)
**And** the chosen behavior is documented + tested
**And** decision flag: silent-skip is the default (provider re-publishing a rate at the same `valid_from` is benign noise) [Source: epics.md Epic 6 Story 6.9 AC; this story].

### AC7 — Cassette: out-of-order timestamp

**Given** the provider returns a rate with `valid_from` BEFORE the latest persisted row's `valid_from`
**When** cassette `cassettes/out-of-order-timestamp.json` applies
**Then** the test asserts:
- The out-of-order rate IS persisted (rates table is append-only chronological order doesn't constrain inserts)
- Subsequent `GetLatestAsync` query still returns the most-recent `valid_from` row (not the late-arriving one)
- `rate_feed_completed` includes a metadata flag `out_of_order_timestamps_detected: true`

### AC8 — Cassette: stale 4h + stale 24h

**Given** provider returns rates with `valid_from` 5 hours ago (stale per Story 6.8)
**When** cassette `cassettes/stale-4h.json` applies
**Then** the rate persists (the FEED isn't broken; the provider is just slow)
**And** Story 6.8's freshness endpoint returns `status: "stale"`
**And** banner appears in UI integration test
**And** similarly for `cassettes/stale-24h.json` with `valid_from` 25 hours ago — `status: "degraded"` + Exchange disabled

### AC9 — Cassette: schema drift (provider response changes shape)

**Given** the provider's response shape changes (e.g., key `rates` becomes `data.rates`)
**When** cassette `cassettes/schema-drift.json` applies
**Then** the deserializer throws `RateFeedSchemaException`
**And** the test asserts:
- Job retries 3× then dead-letters (schema drift is a transient-classified failure for retry purposes; if persistent across retries, dead-letter alerts the operator)
- Telemetry `rate_feed_failed` fires with `error_class: "schema_drift"` [Source: epics.md Epic 6 Story 6.9 AC].

### AC10 — Cassette: auth failure (401/403)

**Given** the provider returns `401 Unauthorized` (API key invalid/expired)
**When** cassette `cassettes/auth-failure.json` applies
**Then** the test asserts:
- NO retry (auth failures are fast-fail per Story 6.1 T6)
- `RateFeedAuthenticationException` thrown
- Telemetry `rate_feed_failed` fires with `error_class: "auth"` + `will_dead_letter: true`
- Operator alert fires immediately (auth failure is operationally critical) [Source: Story 6.1 T6].

### AC11 — Pact consumer-side drift tests for both providers

**Given** Pact (consumer-driven contract testing) catches provider schema drift outside CI
**When** Pact tests run against both `metals-api.com` and `goldapi.io` (live, in a low-frequency separate workflow — NOT in main PR CI)
**Then** the consumer-side Pact files describe the expected response shape:
- `tests/Faktuboh.Infrastructure.Pact/MetalsApiConsumerPactTests.cs`
- `tests/Faktuboh.Infrastructure.Pact/GoldApiConsumerPactTests.cs`
**And** these run in `.github/workflows/external-contract-validation.yml` daily (or weekly per cost)
**And** failure alerts but does NOT block PR merges (since main CI uses cassettes for determinism) [Source: epics.md Epic 6 Story 6.9 AC2].

### AC12 — FsCheck property test on dual-provider reconciliation arbiter

**Given** Story 6.2's `ReconciliationArbiter` is a pure function `Arbitrate(primaryResponse, secondaryResponse) → ArbitrationResult`
**When** the FsCheck property test runs
**Then** the test generates `(primaryRate, secondaryRate)` pairs in `[0.001, 1000.0]` range with various divergences
**And** asserts:
- For divergence < 5%, result is `{ winner: primary, alert: none, persist: true }`
- For 5% ≤ divergence < 20%, result is `{ winner: primary, alert: warning, persist: true }`
- For divergence ≥ 20%, result is `{ winner: none, alert: critical, persist: false }`
- For one-sided unavailability, failover triggers correctly
**And** 1000 generated cases per branch [Source: epics.md Epic 6 Story 6.9 AC3; Story 6.2 AC10].

### AC13 — `numeric(28,8)` round-trip precision tests across conversion chains

**Given** AR-035 dual-precision rule + Story 6.3 cross-currency derivation
**When** the test seeds rates for chain `XAU → USD → EUR → GBP → JOD` (length 4)
**Then** the round-trip query (compute `XAU → JOD` via chain) preserves precision
**And** chain length 1, 2, 3, 4, 5 all tested
**And** the test asserts: `chained_result == direct_result` exactly at `numeric(28, 8)` precision (no IEEE-754 drift)
**And** if any chain length introduces drift, the test fails with exact diagnostic [Source: epics.md Epic 6 Story 6.9 AC4; architecture.md §5.2.5].

### AC14 — Cassette harness documented + reusable

**Given** future stories may add cassettes
**When** documentation is written
**Then** `docs/development/rate-feed-cassettes.md` documents:
- Directory structure
- Cassette JSON schema
- How to add a new cassette
- How to consume from an integration test
- WireMock.NET version + API conventions used

## Tasks / Subtasks

### Backend — Cassette infrastructure

- [ ] **T1 (AC: 1)** — Create `tests/Faktuboh.Infrastructure.Tests/RateFeedCassettes/RateFeedCassetteFixture.cs`
  - [ ] xUnit `IAsyncLifetime` fixture
  - [ ] Starts WireMock.NET server on random port
  - [ ] Disposes on test class teardown
- [ ] **T2 (AC: 1)** — Create `RateFeedCassetteLoader.cs`
  - [ ] Reads cassette JSON
  - [ ] Applies mappings to WireMock server
  - [ ] Helper method `LoadCassette(string name)` for tests

### Backend — Cassettes

- [ ] **T3 (AC: 2)** — `cassettes/blackout.json` + integration test
- [ ] **T4 (AC: 3)** — `cassettes/partial-blackout.json` + test
- [ ] **T5 (AC: 4)** — `cassettes/divergence-below-5pct.json` + `cassettes/divergence-above-5pct.json` + tests
- [ ] **T6 (AC: 5)** — `cassettes/divergence-above-20pct.json` + test
- [ ] **T7 (AC: 6)** — `cassettes/duplicate-timestamp.json` + test
- [ ] **T8 (AC: 7)** — `cassettes/out-of-order-timestamp.json` + test
- [ ] **T9 (AC: 8)** — `cassettes/stale-4h.json` + `cassettes/stale-24h.json` + tests
- [ ] **T10 (AC: 9)** — `cassettes/schema-drift.json` + test
- [ ] **T11 (AC: 10)** — `cassettes/auth-failure.json` + test

### Backend — Pact consumer tests

- [ ] **T12 (AC: 11)** — Create `tests/Faktuboh.Infrastructure.Pact/`
  - [ ] `MetalsApiConsumerPactTests.cs`
  - [ ] `GoldApiConsumerPactTests.cs`
  - [ ] Pact files committed
- [ ] **T13 (AC: 11)** — Create `.github/workflows/external-contract-validation.yml`
  - [ ] Daily (or weekly) cron schedule
  - [ ] Runs Pact verification against live providers
  - [ ] Alerts on failure but doesn't block PR merges

### Backend — Property tests

- [ ] **T14 (AC: 12)** — Create `tests/Faktuboh.Infrastructure.Tests/Rates/ReconciliationArbiterPropertyTests.cs`
  - [ ] FsCheck generators per AC12
  - [ ] All 4 branches asserted
- [ ] **T15 (AC: 13)** — Create `tests/Faktuboh.Infrastructure.Tests/Rates/ChainedRateConversionPrecisionTests.cs`
  - [ ] Chain length 1-5 across multiple currency sequences
  - [ ] Assert exact `(28, 8)` precision

### Documentation

- [ ] **T16 (AC: 14)** — Create `docs/development/rate-feed-cassettes.md`

### CI integration

- [ ] **T17** — Verify cassette tests run in main PR CI (fast)
- [ ] **T18** — Verify Pact tests run in separate scheduled workflow (slow + external)

## Dev Notes

### Critical guardrails (do not violate)

1. **Main CI uses cassettes; Pact runs separately.** Don't hit live providers from PR CI — flakes + cost + rate limits. Cassettes are deterministic.
2. **Cassettes are committed to repo.** Generating cassettes at runtime defeats the determinism + reproducibility purpose.
3. **8-12 scenarios is the calibrated coverage.** Adding more is fine; dropping below 8 leaves gaps in AR-044 #3 rehearsal.
4. **Pact failure alerts but doesn't block.** Live providers can degrade transiently; main CI shouldn't be hostage to external state.
5. **Property test on arbiter generates 1000 cases per branch.** Below this, edge cases at threshold boundaries (5.001%, 19.999%) may not be hit.
6. **Chain precision test runs in Postgres, not C#.** The `(28, 8)` precision lives in the column type; `decimal` C# math may behave differently.
7. **WireMock port is random per test class.** Avoids port conflicts in parallel CI execution.
8. **Cassette naming is descriptive.** `blackout.json` not `cassette-1.json`. Future readers reference by behavior.

### Source tree — files to create or touch

```
tests/Faktuboh.Infrastructure.Tests/RateFeedCassettes/
├── RateFeedCassetteFixture.cs                              # NEW
├── RateFeedCassetteLoader.cs                               # NEW
├── BlackoutScenarioTests.cs                                # NEW
├── PartialBlackoutScenarioTests.cs                         # NEW
├── DivergenceScenarioTests.cs                              # NEW (covers AC4 + AC5)
├── DuplicateTimestampScenarioTests.cs                      # NEW
├── OutOfOrderTimestampScenarioTests.cs                     # NEW
├── StaleFreshnessScenarioTests.cs                          # NEW (covers AC8)
├── SchemaDriftScenarioTests.cs                             # NEW
├── AuthFailureScenarioTests.cs                             # NEW
└── cassettes/
    ├── blackout.json                                       # NEW
    ├── partial-blackout.json                               # NEW
    ├── divergence-below-5pct.json                          # NEW
    ├── divergence-above-5pct.json                          # NEW
    ├── divergence-above-20pct.json                         # NEW
    ├── duplicate-timestamp.json                            # NEW
    ├── out-of-order-timestamp.json                         # NEW
    ├── stale-4h.json                                       # NEW
    ├── stale-24h.json                                      # NEW
    ├── schema-drift.json                                   # NEW
    └── auth-failure.json                                   # NEW

tests/Faktuboh.Infrastructure.Pact/
├── MetalsApiConsumerPactTests.cs                           # NEW
└── GoldApiConsumerPactTests.cs                             # NEW

tests/Faktuboh.Infrastructure.Tests/Rates/
├── ReconciliationArbiterPropertyTests.cs                   # NEW
└── ChainedRateConversionPrecisionTests.cs                  # NEW

.github/workflows/
└── external-contract-validation.yml                        # NEW (daily/weekly)

docs/development/
└── rate-feed-cassettes.md                                  # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Cassette-driven integration | 11 scenarios | WireMock.NET + Testcontainers |
| Property | Reconciliation arbiter (1000/branch) | FsCheck |
| Property | Chained conversion precision | xUnit + Postgres |
| Contract (external) | Pact daily/weekly | Pact.NET + GitHub Actions cron |

### Architecture compliance

- **AR-044 #3** — rate-feed blackout + divergence rehearsal.
- **§2.10 rehearsal #3** — operationalized as cassettes.
- **§5.2.5** — `numeric(28, 8)` precision verified.
- **AR-035** — dual-precision rule.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Mock HTTP | WireMock.NET | latest stable |
| Property | FsCheck.Xunit | |
| Contract | PactNet | Daily workflow only |
| Test runner | xUnit v3 | Same as elsewhere |

### Testing requirements

- **Cassette-driven:** all 11 scenarios + assertions on Story 6.1/6.2 behavior.
- **Property:** arbiter + chain precision.
- **Pact:** external scheduled validation; alerts on drift.
- **Reusable:** future stories add cassettes via documented pattern.

### Previous Story Intelligence

**Prerequisites:**
- Story 6.1 — rate-feed job + provider abstraction (cassettes exercise THIS)
- Story 6.2 — failover + reconciliation (cassettes exercise THIS)
- Story 6.3 — current rate query (cross-currency derivation tested)
- Story 6.7 — append-only enforcement (cassettes verify duplicate-timestamp behavior)
- Story 6.8 — freshness banner (cassettes drive UI banner integration tests)

**Coordination:**
- This story should land NEAR THE END of Epic 6 (per execution order: last) so cassettes test the full integrated stack.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.9
- AR-044 #3 — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.10 rehearsal #3
- Dual-precision — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- Story 6.1 — [_bmad-output/implementation-artifacts/6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md](./6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md)
- Story 6.2 — [_bmad-output/implementation-artifacts/6-2-dual-provider-redundancy-with-automatic-failover.md](./6-2-dual-provider-redundancy-with-automatic-failover.md)
- Story 6.8 — [_bmad-output/implementation-artifacts/6-8-rate-freshness-sla-banner-and-disable.md](./6-8-rate-freshness-sla-banner-and-disable.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- 11 cassettes covering AR-044 #3 rehearsal scenarios.
- Pact runs separately on schedule; main CI uses cassettes for determinism.
- Property tests catch arbiter + precision regressions across 1000+ generated cases.
- Last Epic 6 story; closes the test-harness loop for the entire epic.

### File List

_Populated during implementation._
