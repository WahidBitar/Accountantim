# Story 0.13: NFR-SC4 load test baseline harness

Status: ready-for-dev

## Story

As a **developer**,
I want **a k6 load-test harness committed with an initial no-op script that feature epics hook endpoints into**,
so that **NFR-SC4 (83 rps sustained / 500 rps cache-sizing posture) has a home that's impossible to orphan**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. NFR-SC4 scalability baseline. Epic 4 adds public-statement throughput; Epic 8 executes full production-representative load.

**Why this story now:** After Story 0.10 (Azure infra = staging target to hit). Late in Epic 0 sequence because it's trend-tracked not PR-blocking — doesn't gate Story 2.1 merge.

## Acceptance Criteria

### AC1 — Harness scaffolded at `tests/Load/`

**Given** k6 is the load-test tool per epics.md Story 0.13
**When** the harness is scaffolded
**Then** `tests/Load/` exists with:
- `tests/Load/baseline.js` — placeholder 1-rps curl against `/health`
- `tests/Load/package.json` (or `deno.json` if using Deno runtime; simpler: no package.json — k6 has a built-in runtime)
- `tests/Load/README.md` per AC3
- `tests/Load/scenarios/` — folder where epic owners add endpoint scripts

### AC2 — Baseline script runs + exits 0

**Given** k6 is installed
**When** `k6 run tests/Load/baseline.js` executes against staging
**Then** the script fires a 1-rps GET to `https://aca-faktuboh-api-staging.<region>.azurecontainerapps.io/health` for 30 seconds
**And** the script asserts response status = 200
**And** the script exits with code 0 if all checks pass
**And** `tests/Load/baseline.js`:

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 1 }, // 1 rps steady-state
  ],
  thresholds: {
    http_req_duration: ['p(95)<300', 'p(99)<1500'], // NFR-SC4 acceptance
    http_req_failed: ['rate<0.001'],                 // <0.1% errors
  },
};

export default function () {
  const baseUrl = __ENV.FAKTUBOH_STAGING_URL || 'https://aca-faktuboh-api-staging.germanywestcentral.azurecontainerapps.io';
  const res = http.get(`${baseUrl}/health`);
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(1);
}
```

[Source: epics.md Story 0.13]

### AC3 — README documents contributor workflow

**Given** epic owners add endpoints
**When** `tests/Load/README.md` is inspected
**Then** it documents:
- **How to add an endpoint scenario**:
  1. Create `tests/Load/scenarios/<epic>-<endpoint-kebab>.js`
  2. Declare target RPS + ramp-up duration
  3. Define pass/fail thresholds: p95 <300ms, p99 <1.5s, zero 5xx, error <0.1%
  4. Submit PR — CI adds to nightly runner
- **How to run locally**: `k6 run tests/Load/scenarios/<script>.js --env FAKTUBOH_STAGING_URL=https://...`
- **How to interpret results**: k6 emits `summary.json` + checks; failing checks block nightly alerting
- **NFR-SC4 targets**:
  - **Sustained**: 83 rps with p95 <300ms, p99 <1.5s (PRD commitment for normal operation)
  - **Cache-sizing posture**: 500 rps short-burst absorbed at ASWA edge (per §7.4.1 item 1 AFD→ASWA reconciliation) — design-posture only, not operational target
  - Error rate <0.1%; zero 5xx
- **Epic hand-off schedule**:
  - Epic 4 (public-statement) adds `statement-by-token.js` with full 83 rps sustained scenario
  - Epic 8 (production rehearsal) adds full production-representative execution

### AC4 — Nightly runner against staging (not PR-blocking)

**Given** load tests run nightly, trend-tracked
**When** `.github/workflows/load-test-nightly.yml` is scaffolded
**Then** the workflow triggers via `schedule: [{ cron: '0 4 * * *' }]` (every day 04:00 UTC — after Monday mutation run, different hour)
**And** it also triggers on `workflow_dispatch`
**And** the workflow installs k6 (`actions/setup-k6-action`), runs all `tests/Load/*.js` + `tests/Load/scenarios/*.js` against staging, collects `summary.json` results, and publishes to a committed `docs/quality/load-test-trends.json` file
**And** the workflow does NOT fail the deploy on threshold miss — it emits a warning artifact + posts to a GitHub Issue for triage (non-blocking) [Source: epics.md Story 0.13 "nightly against staging — not PR-blocking; trend-tracked"]

### AC5 — Trend tracking

**Given** per-night runs produce results
**When** results accumulate
**Then** `docs/quality/load-test-trends.json` is updated nightly with `{ date, endpoint, target_rps, p95_ms, p99_ms, error_rate, status: 'pass' | 'warn' | 'fail' }`
**And** a simple text report is committed to `docs/quality/load-test-last-30-days.md` showing the last 30 nightly runs per endpoint
**And** a regression of >20% in p95 over 7-day rolling average triggers a GitHub Issue with label `load-test-regression`

### AC6 — Epic 4 hand-off scenario stub

**Given** Epic 4 will add the public-statement throughput scenario
**When** this story lands
**Then** `tests/Load/scenarios/epic4-statement-by-token.js` exists as a skeleton:

```javascript
// tests/Load/scenarios/epic4-statement-by-token.js
// Owner: Epic 4 Story 4.X — public-statement share route
// Target: 83 rps sustained per NFR-SC4
// Cache-sizing posture: 500 rps short-burst (ASWA edge absorbs)
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  stages: [
    { duration: '1m',  target: 10 },   // ramp
    { duration: '5m',  target: 83 },   // sustained NFR-SC4 commitment
    { duration: '30s', target: 500 },  // cache-sizing burst
    { duration: '30s', target: 83 },   // recover
  ],
  thresholds: {
    'http_req_duration{status:200}': ['p(95)<300', 'p(99)<1500'],
    http_req_failed: ['rate<0.001'],
  },
};

export default function () {
  const token = __ENV.TEST_STATEMENT_TOKEN || 'TBD-epic4-fixture';
  // SKIP until Epic 4 ships a stable test token + endpoint
  if (token === 'TBD-epic4-fixture') return;
  const baseUrl = __ENV.FAKTUBOH_STAGING_URL;
  const res = http.get(`${baseUrl}/v1/public/statement/${token}`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
```

**And** the scenario file has a `// SKIP` early-return so nightly runs don't fail while the fixture is pending

### AC7 — Epic 8 note

**Given** Epic 8 is the production rehearsal
**When** `tests/Load/README.md` documents the Epic 8 plan
**Then** it covers:
- Production-representative execution: exact target rps + distribution per endpoint (based on actual Phase-A telemetry from Epics 1-4 production usage)
- Scale-out verification: ACA's `minReplicas=1` + `maxReplicas=3` must handle the target without 5xx
- Rate-feed redundancy scenario: trigger primary-provider failure + verify failover within 60 min per NFR-I6
- Cache-hit ratio: assert ASWA edge cache-hit >95% on public-statement traffic at target load

## Tasks / Subtasks

### T1 — Directory + baseline script (AC: 1, 2)

- [ ] Create `tests/Load/baseline.js` per AC2 snippet
- [ ] Create `tests/Load/scenarios/.gitkeep`
- [ ] Document local install in README: `winget install k6` (Windows), `brew install k6` (macOS), `apt install k6` (Linux)

### T2 — README (AC: 3)

- [ ] Create `tests/Load/README.md` per AC3 sections

### T3 — Nightly workflow (AC: 4, 5)

- [ ] Create `.github/workflows/load-test-nightly.yml`:
  ```yaml
  name: load-test-nightly
  on:
    schedule: [{ cron: '0 4 * * *' }]
    workflow_dispatch:
  permissions: { contents: write, issues: write }
  jobs:
    load-test:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@<sha>
        - uses: grafana/setup-k6-action@<sha>
        - run: |
            for script in tests/Load/baseline.js tests/Load/scenarios/*.js; do
              k6 run --summary-export=results-$(basename $script .js).json $script --env FAKTUBOH_STAGING_URL=${{ secrets.STAGING_URL }}
            done
        - run: |
            # Aggregate to docs/quality/load-test-trends.json
            node tests/Load/scripts/aggregate-results.js
        - run: |
            git config user.email "load-test@faktuboh.com"
            git config user.name "load-test-bot"
            git add docs/quality/load-test-trends.json docs/quality/load-test-last-30-days.md
            git diff --cached --quiet || git commit -m "chore(load-test): nightly run $(date -u +%Y-%m-%d)"
            git push
  ```
- [ ] Create `tests/Load/scripts/aggregate-results.js` — parses per-script `results-*.json` + updates trends

### T4 — Epic 4 skeleton (AC: 6)

- [ ] Create `tests/Load/scenarios/epic4-statement-by-token.js` per AC6 snippet
- [ ] Add TODO in `epics.md` Story 4.X definition referencing this scenario + required token fixture

### T5 — Epic 8 note (AC: 7)

- [ ] Append Epic 8 section to `tests/Load/README.md` with the four bullets from AC7

## Dev Notes

### Critical guardrails (do not violate)

1. **NOT PR-blocking at MVP.** Load tests are trend-tracked nightly. Promoting to PR-gated would slow every change for marginal signal. [Source: epics.md Story 0.13]
2. **Target is STAGING, not production.** Production load testing requires explicit pre-approval + customer comms plan + off-peak window. Epic 8 covers that.
3. **Initial 1-rps baseline is intentionally minimal.** Growing the harness is cheap; shipping something is hard. Ship the skeleton now.
4. **Epic owners own their scenario files.** This story owns the harness — subsequent epics own the assertions against their endpoints.
5. **Threshold tuning is iterative.** First weeks of data establish reasonable p95/p99 envelopes per endpoint; bake those into thresholds rather than guessing.

### Source tree — files to create

```
tests/Load/
├── baseline.js                                          # NEW (1-rps /health check)
├── README.md                                            # NEW (contributor workflow + NFR-SC4 targets)
├── scenarios/
│   ├── .gitkeep                                         # NEW (empty folder marker)
│   └── epic4-statement-by-token.js                      # NEW (skeleton)
└── scripts/
    └── aggregate-results.js                             # NEW (trend aggregator)

.github/workflows/
└── load-test-nightly.yml                                # NEW

docs/quality/
├── load-test-trends.json                                # NEW (empty initial)
└── load-test-last-30-days.md                            # NEW (empty initial)
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Load | k6 | Baseline + per-endpoint scenarios |
| Trend | GitHub Action + committed JSON | 30-day rolling comparison |

### Project Structure Notes

- `tests/Load/` lives at repo root, not under `backend/tests/` or `frontend/e2e/` — load tests span the whole system, not a single stack.
- k6 scripts are plain JavaScript run by k6's embedded runtime — no Node.js dep required for execution.

### Architecture compliance

- [architecture.md §4 D4.6 + D4.19](../planning-artifacts/architecture.md) — 83 rps sustained / 500 rps cache-sizing
- [prd.md NFR-SC4](../planning-artifacts/prd.md) — scalability commitment
- [epics.md Story 0.13](../planning-artifacts/epics.md) — harness definition

### Library/framework requirements

| Layer | Tool | Purpose |
|---|---|---|
| Load test | k6 (grafana/k6) | Script runtime |
| CI | grafana/setup-k6-action | GitHub Action installer |

### Testing requirements

- **Baseline script must exit 0** when run against a healthy staging — proves the harness works.
- **Nightly workflow run at least once successfully** before marking story complete.

### Previous Story Intelligence

- **Story 0.10 (Azure infra)** complete: staging ACA + `/health` endpoint exists.
- **Story 0.9 (CI/CD)** complete: GitHub Actions infra for the nightly workflow.
- **Epic 4 (public-statement)** + **Epic 8 (production rehearsal)** are consumers.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.13 definition
- [_bmad-output/planning-artifacts/prd.md NFR-SC4](../planning-artifacts/prd.md) — 83 rps sustained + 500 rps posture
- [_bmad-output/planning-artifacts/architecture.md §4 D4.6 + §4 D4.19](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/architecture.md §7.4.1](../planning-artifacts/architecture.md) — AFD→ASWA reconciliation

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
