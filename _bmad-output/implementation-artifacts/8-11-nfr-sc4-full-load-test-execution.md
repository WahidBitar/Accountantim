# Story 8.11: NFR-SC4 full load test execution

Status: ready-for-dev

## Story

As **Wahid**,
I want **the k6 load-test baseline harness (from Story 0.13) executed at production-representative scale targeting 83 rps sustained + 500 rps cache-sizing posture**,
so that **NFR-SC4 is formally validated before opening beta** (AR-044 rehearsal tied to real traffic) [Source: epics.md Epic 8 Story 8.11; prd.md NFR-SC4].

**Covers requirements:** Epic 8 — NFR-SC4 (full load test), AR-044 rehearsal completion, Story 0.13 baseline harness extension to production scale [Source: epics.md Story 8.11].

**Why this story now:** Final Epic 8 story. Sequenced after rotation drill (8.10) so the load test runs against a fully-rotated, fully-rehearsed system. This story is the gate for opening beta — proves the system holds under production-representative load.

## Acceptance Criteria

### AC1 — k6 scenario `production-representative.js`

**Given** Story 0.13 established the baseline harness
**When** the production-representative scenario is added at `tests/Load/production-representative.js`
**Then** the scenario:
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    sustained_load: {
      executor: 'constant-arrival-rate',
      rate: 83,
      timeUnit: '1s',
      duration: '30m',
      preAllocatedVUs: 200,
      maxVUs: 500,
      exec: 'sustained',
    },
    cache_storm: {
      executor: 'constant-arrival-rate',
      rate: 500,
      timeUnit: '1s',
      duration: '30s',
      preAllocatedVUs: 100,
      maxVUs: 600,
      startTime: '32m',
      exec: 'cacheStorm',
    },
  },
  thresholds: {
    'http_req_duration{scenario:sustained_load}': ['p(95)<300', 'p(99)<1500'],
    'http_req_failed{scenario:sustained_load}': ['rate<0.001'],
    'http_req_duration{scenario:cache_storm}': ['p(95)<1000'],
  },
};

export function sustained() {
  // Mix of: 60% public statement views, 30% owner-workspace reads, 10% creates
  // ...
}

export function cacheStorm() {
  // 100% public statement views — exercises AFD edge cache + origin
  // ...
}
```
**And** the scenario inherits the auth + payload patterns from Story 0.13 [Source: epics.md Story 8.11 AC; Story 0.13].

### AC2 — Sustained 83 rps for 30 minutes meets performance targets

**Given** the sustained-load scenario runs
**When** 83 rps holds for 30 minutes
**Then** all performance thresholds met:
- p95 < 300ms (across all endpoints)
- p99 < 1500ms
- Error rate < 0.1% (`http_req_failed{rate} < 0.001`)
- Wolverine DLQ rows < 50 throughout the test
**And** k6 reports the actual values
**And** any threshold breach fails the test [Source: epics.md Story 8.11 AC; prd.md performance NFRs].

### AC3 — 500 rps burst absorbed at AFD edge with cache-hit > 95%

**Given** the cache-storm scenario simulates viral-share burst
**When** 500 rps hits public-statement URLs for 30s
**Then** AFD edge cache absorbs > 95% of requests (cache-hit > 95%)
**And** origin (ACA) sees cache-miss tail < 50 rps (5% of 500 = 25; safety threshold 50)
**And** AFD cache-hit-rate metric is queried via `az monitor metrics list` post-test
**And** the cache-storm scenario's URLs are a SMALL set (10-20 URLs) so AFD's TTL caches them quickly — production reality is a small set of viral links each receiving high traffic [Source: epics.md Story 8.11 AC; architecture.md §4.6 D4.18 AFD].

### AC4 — Wolverine DLQ remains < 50 rows

**Given** Wolverine DLQ rows indicate handler failures
**When** the load test runs
**Then** the DLQ row count is queried before + during + after the test
**And** the count never exceeds 50 throughout (allows for occasional transient failure but flags systemic issue)
**And** any DLQ entries are inspected post-test + categorized in the runbook [Source: epics.md Story 8.11 AC DLQ].

### AC5 — Test environment matches production

**Given** load test must run against production-representative infrastructure
**When** the test runs
**Then** target environment is staging (NOT production directly)
**And** staging has:
- Same ACA SKU as production
- Same Postgres SKU as production
- Same AFD config as production
- Synthetic data seeded from Story 8.8 + scaled up: 1,000 users + 10,000 contacts + 50,000 entries + 1,000 capability tokens
- Cache-storm URLs use 20 pre-generated capability tokens
**And** the test does NOT run against production (that's a separate "production smoke test" exercise post-launch) [Source: epics.md Story 8.11 AC representative].

### AC6 — Test results documented in dated runbook

**Given** results must be auditable + comparable
**When** the test completes
**Then** `docs/runbooks/load-test-YYYY-MM-DD.md` is created with:
1. Date + operator name (Wahid)
2. Source code commit SHA tested
3. Environment (staging — with config snapshot)
4. Test parameters (rps, duration)
5. Actual measurements:
   - Sustained: p50, p95, p99, p99.9 latencies + error rate + total requests
   - Cache storm: same metrics + AFD cache-hit-rate
   - DLQ row counts at t=0, t=15m, t=30m, t=32m, t=33m, t=end
6. Pass/Fail per threshold
7. Resource usage (CPU + memory + connections during the test)
8. Findings + action items
**And** the template `docs/runbooks/load-test-template.md` exists for consistency [Source: epics.md Story 8.11 AC documentation].

### AC7 — Regression-comparison gate

**Given** load tests are repeatable across releases
**When** subsequent test executions occur
**Then** the runbook tracks results across runs
**And** a regression > 10% on any p95 metric vs prior baseline triggers investigation per AC requirement
**And** the comparison is manual at MVP (visual review of consecutive runbook files); Phase-2 could automate via dashboards [Source: epics.md Story 8.11 AC regression check].

### AC8 — Beta-launch gate flag

**Given** AR-044 ties this rehearsal to first-beta-user readiness
**When** the load test passes all thresholds
**Then** `docs/runbooks/load-test-passed-YYYY-MM-DD.flag` is committed
**And** `scripts/check-beta-launch-readiness.sh` validates this flag exists within last 30 days before allowing beta launch (script wired into deploy workflow)
**And** if the flag is absent or > 30 days old, the script fails with "NFR-SC4 load test required before beta" [Source: design decision aligned with RT-46 + 8.8 pattern].

### AC9 — Test telemetry visible in App Insights

**Given** the load test creates real telemetry events
**When** the test runs against staging
**Then** all events emit to staging App Insights
**And** post-test, KQL queries can verify:
```kql
customEvents
| where timestamp between (datetime('<test_start>') .. datetime('<test_end>'))
| summarize count() by name
```
**And** event counts match expected request counts (e.g., 83 rps * 30 min * 60 sec = 149,400 sustained-scenario events) — within ±5% tolerance [Source: epics.md Story 8.11 AC indirectly; Story 0.14 telemetry].

### AC10 — Post-test cleanup

**Given** the test seeds significant data
**When** the test completes
**Then** the cleanup procedure documented in runbook:
1. Drop synthetic test data (the 1k users + 10k contacts + 50k entries seeded for the test)
2. Clear App Insights staging events (or tag them for separate retention)
3. Reset AFD cache (purge all paths)
4. Restart ACA to clear in-memory state
**And** cleanup is idempotent [Source: design decision].

## Tasks / Subtasks

### k6 scenario

- [ ] **T1 (AC: 1)** — Create `tests/Load/production-representative.js` per AC1
  - [ ] Sustained scenario: 83 rps × 30 min, mixed endpoints
  - [ ] Cache-storm scenario: 500 rps × 30s, public-statement URLs only
  - [ ] Thresholds matching AC2

### Synthetic data scaling

- [ ] **T2 (AC: 5)** — Extend Story 8.8's seeder to support `--scale` flag
  - [ ] `--scale=10` produces 10x dataset (1k users instead of 200)
  - [ ] Generates 20 long-lived capability tokens for cache-storm URLs

### Test execution scripts

- [ ] **T3 (AC: 4)** — Create `scripts/run-load-test.sh`
  - [ ] Pre-test: snapshot DLQ count + record
  - [ ] During-test: poll DLQ count every 60s + record
  - [ ] Post-test: snapshot DLQ count + cache-hit metrics from AFD
  - [ ] Outputs all measurements to a structured JSON file
- [ ] **T4 (AC: 3)** — Create `scripts/query-afd-cache-metrics.sh`
  - [ ] Calls `az monitor metrics list` for AFD cache-hit-rate
  - [ ] Returns JSON with breakdowns

### Runbook + template

- [ ] **T5 (AC: 6)** — Create `docs/runbooks/load-test-template.md`
- [ ] **T6 (AC: 6)** — Execute first load test + commit `docs/runbooks/load-test-YYYY-MM-DD.md`

### Beta-launch gate

- [ ] **T7 (AC: 8)** — Create `scripts/check-beta-launch-readiness.sh`
  - [ ] Globs `docs/runbooks/load-test-passed-*.flag`
  - [ ] Validates within last 30 days
  - [ ] Fails deploy if absent

### Cleanup procedure

- [ ] **T8 (AC: 10)** — Document cleanup in runbook + provide `scripts/cleanup-after-load-test.sh`
  - [ ] Drops test data (using a tag column added to test-seeded rows)
  - [ ] Purges AFD cache via `az afd endpoint purge`
  - [ ] Restarts ACA via `az containerapp revision restart`

### Tests

- [ ] **T9 (AC: 1)** — Validation test for k6 scenario syntax
  - [ ] CI runs `k6 archive tests/Load/production-representative.js` to validate
- [ ] **T10 (AC: 9)** — Post-test telemetry-count assertion script
  - [ ] Runs the KQL query + compares to expected count + flags discrepancy > 5%

## Dev Notes

### Critical guardrails (do not violate)

1. **NEVER run against production.** Staging only. The risk of accidentally DoSing the production system is unacceptable.
2. **DLQ count is the leading indicator of trouble.** Without monitoring it, errors hide in the DLQ silently.
3. **Cache-storm scenario uses a SMALL URL set (10-20 URLs).** Production viral reality: a small set of links each gets a lot of traffic. A large URL set wouldn't exercise AFD edge cache realistically.
4. **5% telemetry-count tolerance.** Some retries / dropped events are expected. > 5% indicates a real telemetry pipeline issue.
5. **30-day flag freshness.** Beyond 30 days, infrastructure changes invalidate the test result.
6. **Regression-comparison is manual at MVP.** Automating it requires dashboards + alerting. Phase-2.
7. **Cleanup is mandatory.** Without it, staging data accumulates → next load test starts from a polluted state.
8. **Threshold breaches FAIL the test.** k6's exit code is non-zero on threshold failure. CI workflow must respect this.
9. **AFD cache-hit > 95% is the cost-control invariant.** Below this, origin cost spikes during viral events.
10. **The test SHA + environment config snapshot in the runbook.** Without it, "what was tested" is unverifiable post-hoc.
11. **AC2 explicit: error rate < 0.1%, NOT < 1%.** Stricter than typical web app — fintech-grade.

### Source tree — files to create or touch

```
tests/Load/
├── production-representative.js                                # NEW
└── lib/                                                        # Inherited from Story 0.13

scripts/
├── run-load-test.sh                                            # NEW
├── query-afd-cache-metrics.sh                                  # NEW
├── cleanup-after-load-test.sh                                  # NEW
└── check-beta-launch-readiness.sh                              # NEW

tools/SeedRehearsalDataset/
└── Program.cs                                                  # MODIFY (+ --scale flag)

docs/runbooks/
├── load-test-template.md                                       # NEW
├── load-test-YYYY-MM-DD.md                                     # NEW (per execution)
└── load-test-passed-YYYY-MM-DD.flag                            # NEW (after first pass)

.github/workflows/
└── load-test-validation.yml                                    # NEW (validates k6 syntax in PR)
```

### Testing standards

| Level | Tools |
|---|---|
| Operational | k6 + Azure CLI |
| Validation | k6 archive command in CI |

### Project Structure Notes

This story extends `tests/Load/` (established in Story 0.13) with the production-representative scenario. The `scripts/` directory accumulates more operational scripts. The `docs/runbooks/` directory holds dated load-test results.

### Architecture compliance

- [epics.md Epic 8 Story 8.11](../planning-artifacts/epics.md)
- [architecture.md AR-044, §4.6 D4.18 AFD](../planning-artifacts/architecture.md)
- [prd.md NFR-SC4, performance NFRs](../planning-artifacts/prd.md)
- [Story 0.13 baseline load-test harness](./0-13-nfr-sc4-load-test-baseline-harness.md)
- [Story 0.14 telemetry](./0-14-phase-a-telemetry-primitive.md)
- [Story 8.8 synthetic seeder](./8-8-restore-under-pressure-tabletop-rehearsal.md)
- [Story 8.10 rotation drill (executes BEFORE this for fully-rotated test target)](./8-10-secrets-rotation-dry-run-auth0-and-kek.md)

### Library / framework requirements

| Layer | Tool | Notes |
|---|---|---|
| Load testing | k6 | Latest stable; per Story 0.13 |
| Cloud queries | Azure CLI (az) | AFD metrics + DLQ inspection |

### Testing requirements

- **k6 archive validation in CI** — without it, broken scenarios ship to staging.
- **Telemetry count assertion** — without it, "the test ran" doesn't prove "the system observed the load."
- **DLQ monitoring during test** — without it, mid-test failures hide.
- **Cleanup script idempotency** — without it, partial cleanup leaves staging in a weird state.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.13 — baseline harness
- Story 0.14 — telemetry primitive
- Story 8.8 — synthetic seeder (extended here with --scale)
- Story 8.10 — rotation drill (test target should be fully rotated for full validation)

**This story is the final Epic 8 prerequisite for beta launch per AR-044.**

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.11
- [_bmad-output/planning-artifacts/architecture.md AR-044, §4.6 D4.18](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/prd.md NFR-SC4](../planning-artifacts/prd.md)
- [Story 0.13 baseline harness](./0-13-nfr-sc4-load-test-baseline-harness.md)
- [Story 8.8](./8-8-restore-under-pressure-tabletop-rehearsal.md)
- [Story 8.10](./8-10-secrets-rotation-dry-run-auth0-and-kek.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
