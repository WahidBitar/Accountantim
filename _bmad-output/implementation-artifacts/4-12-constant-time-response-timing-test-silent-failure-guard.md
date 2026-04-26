# Story 4.12: Constant-time-response timing test (silent-failure guard)

Status: ready-for-dev

## Story

As **Wahid**,
I want **a statistical timing test in CI that compares p99 response times between valid and invalid tokens across 10k requests each**,
so that **timing-oracle leaks — a silent-failure class that unit and integration tests cannot catch — are caught mechanically** (Murat's landmine).

**Covers requirements:** Epic 4 — §2.9 public-surface #2 (constant-time invariant), Murat's testing landmine, statistical timing discipline [Source: epics.md Epic 4 Story 4.12; architecture.md §2.9 #2].

**Why this story now:** Stories 4.1, 4.5, 4.6, 4.9, 4.11 all CLAIM constant-time behavior. Without a mechanical statistical test, the claim is aspirational. This story is the silent-failure guard — a nightly Welch's t-test across 20k requests compares timing distributions and fails CI if an oracle opens up.

## Acceptance Criteria

### AC1 — Timing harness exists at `tests/Timing/`

**Given** a dedicated timing test surface is required
**When** the project is scaffolded
**Then** `tests/Timing/` directory contains:
- `Faktuboh.TimingTests.csproj` — xUnit v3 test project, tagged `[Trait("Category", "Timing")]`
- `CapabilityTokenTimingHarness.cs` — test runner that measures request latency
- `WelchTTest.cs` — statistical comparison implementation
- `README.md` — documents the methodology + thresholds
**And** the project is NOT run in standard CI (too noisy); runs nightly via a dedicated workflow [Source: epics.md Story 4.12; architecture.md §2.10 pre-launch rehearsals].

### AC2 — Harness fires 10k valid + 10k invalid token requests

**Given** statistical significance requires sample size
**When** the harness runs
**Then** it:
1. Seeds a valid token against the staging environment (via bypass token from Story 4.9)
2. Fires **10,000 requests** to `/v1/statements/t/{validToken}` in parallel batches of 50
3. Fires **10,000 requests** to `/v1/statements/t/{invalidToken}` (a random 22-char base64url) in identical batches
4. Records wall-clock latency (from HTTP send to first byte received) for each request
5. Stores samples in `timing-results.json` for post-hoc analysis
**And** the harness uses `System.Net.Http.HttpClient` with `HttpVersion.Version20` (HTTP/2 parallel streams)
**And** the staging environment uses `minReplicas=1` (pre-warmed, not cold-starting during the run) [Source: epics.md Story 4.12 AC].

### AC3 — Welch's t-test p-value < 0.01 fails the test

**Given** Welch's t-test (unequal variances) compares two sample means
**When** the statistic is computed
**Then** `WelchTTest.Compute(validSamples, invalidSamples): { t: double, pValue: double, df: double }`
**And** the test assertion: `pValue >= 0.01` (i.e., "we cannot reject the null hypothesis that the two distributions have the same mean")
**And** if `pValue < 0.01`, the test FAILS with a report showing:
- Mean valid latency, mean invalid latency
- StdDev for each
- Welch's t-statistic + df + p-value
- Histogram of the two distributions (ASCII art in console output)
**And** the implementation is pinned to a reference formula; a unit test validates it against known text-book cases [Source: epics.md Story 4.12 AC; standard statistical references].

### AC4 — p99 timing difference < 5ms enforced

**Given** p99 is the more actionable threshold than mean
**When** results are analyzed
**Then** `p99_diff = |p99(validSamples) - p99(invalidSamples)|` must be < 5 milliseconds
**And** p99 is computed via nearest-rank method on sorted samples
**And** an additional assertion: p95 and p99.9 differences must each be < 10 ms (broader tail check)
**And** the test fails if ANY of these thresholds breach [Source: epics.md Story 4.12 AC].

### AC5 — Nightly workflow, not per-PR

**Given** timing tests are noisy on shared CI runners
**When** scheduling is configured
**Then** `.github/workflows/timing-tests-nightly.yml` runs at 03:00 UTC:
1. Provisions a dedicated timing-test runner (self-hosted or Azure VM — document in `docs/ops/timing-test-runner.md`)
2. Compiles + runs `Faktuboh.TimingTests`
3. Posts results to the Golden-signals dashboard as custom metrics: `timing.valid.p99`, `timing.invalid.p99`, `timing.pvalue`
4. On failure: creates a GitHub Issue titled `"Timing regression detected — manual review"` with the full report attached
**And** the workflow is NOT a required check on PRs (would block merges on flaky noise) [Source: epics.md Story 4.12; architecture.md §2.10].

### AC6 — Trend tracking + alert

**Given** gradual degradation is the silent-failure mode
**When** App Insights metrics accumulate
**Then** the Golden-signals dashboard has a panel showing `timing.pvalue` over the last 30 nights
**And** an alert fires when:
- `pvalue < 0.05` for 2 consecutive nights (warning — approaching significance)
- `pvalue < 0.01` for 1 night (critical — the test would fail)
**And** alerts email `wahid.wb@gmail.com` [Source: architecture.md §4.9 silent-decay SLOs].

### AC7 — Adversarial token shapes also tested

**Given** Story 4.10 defines 8 adversarial shapes
**When** the timing harness runs an extended suite
**Then** it fires 10,000 requests each for:
- Valid token (baseline)
- Unknown 22-char base64url (random)
- Truncated-by-1 (21 chars)
- Tampered-middle-byte
- Expired (expires_at = past)
- Revoked
- Wrong-audience (future-proofing; MVP scope-mismatch ≈ lookup miss)
- Wrong version byte
**And** all 7 invalid cases' p99 differ from valid by < 5 ms (pairwise t-test p-value ≥ 0.01)
**And** the test report includes pairwise results so regressions can be attributed to a specific shape [Source: Story 4.10 AC4; epics.md Story 4.12].

### AC8 — Harness can run against local + staging

**Given** developers should run this locally before merging risky changes
**When** the harness is invoked
**Then** CLI flag `--target <url>` selects the target (default: staging)
**And** CLI flag `--sample-size <N>` controls the sample size (default: 10000; local runs can use 1000 for speed)
**And** output goes to stdout as both table + `--output-json` for machine consumption
**And** local runs document in `tests/Timing/README.md`: "This harness validates the constant-time invariant. Run locally via `dotnet test tests/Timing/ --filter Category=Timing -- --target http://localhost:5002 --sample-size 1000`" [Source: epics.md Story 4.12].

### AC9 — CI uses bypass token to avoid rate-limit interference

**Given** Story 4.9 rate-limits view endpoints
**When** 20k requests hit within minutes
**Then** the harness attaches `X-Faktuboh-Bypass: ${FAKTUBOH_RATE_LIMIT_BYPASS_TOKEN}` header on all requests
**And** the bypass token is a GitHub Actions secret; not in source
**And** a test run WITHOUT the bypass token would trigger 429s and skew results — the harness refuses to run without the header (fail-fast) [Source: Story 4.9 AC8].

### AC10 — Harness is self-validating

**Given** a buggy harness could produce false confidence
**When** the harness is tested
**Then** a test `TimingHarnessSelfValidationTests.cs` runs against a known-asymmetric endpoint (test-only endpoint that deliberately returns faster for valid tokens — `GET /test/timing-biased-endpoint` wrapped in `#if TIMING_VALIDATION`)
**And** the harness correctly detects the bias (Welch's t-test p < 0.001, p99 difference > 10 ms)
**And** the self-validation test runs as part of the normal unit-test suite (not in the timing run itself) [Source: epics.md Story 4.12; Murat testing landmines].

## Tasks / Subtasks

### Timing harness scaffold

- [ ] **T1 (AC: 1)** — Create `tests/Timing/Faktuboh.TimingTests.csproj`
  - [ ] xUnit v3 test project
  - [ ] References `System.Net.Http`, `System.Diagnostics.Stopwatch`
  - [ ] All tests tagged `[Trait("Category", "Timing")]`
- [ ] **T2 (AC: 1, 8)** — Create `CapabilityTokenTimingHarness.cs`
  - [ ] Static method `RunAsync(string target, int sampleSize, byte[] validToken, byte[] invalidToken, string bypassToken): Task<TimingReport>`
  - [ ] CLI entry point `Program.cs` with arg parsing (`--target`, `--sample-size`, `--output-json`)

### Welch's t-test implementation

- [ ] **T3 (AC: 3)** — Create `WelchTTest.cs` in `tests/Timing/Stats/`
  - [ ] `Compute(double[] a, double[] b): WelchResult { T, PValue, Df }`
  - [ ] Uses Welch-Satterthwaite degrees-of-freedom formula
  - [ ] Unit test `WelchTTestTests.cs` with textbook fixtures (e.g., Walpole/Myers example 10.11)
- [ ] **T4 (AC: 4)** — Add p99/p95/p99.9 percentile computation
  - [ ] `PercentileNearestRank(double[] sorted, double p): double`

### Load phase

- [ ] **T5 (AC: 2, 9)** — Implement the measurement loop
  - [ ] Parallel HTTP/2 client pool (size 50)
  - [ ] Stopwatch per request
  - [ ] Attaches bypass token header
  - [ ] Warmup phase: 100 unmeasured requests (JIT + connection pool prep)

### Reporting

- [ ] **T6 (AC: 3, 4, 7)** — Generate the report
  - [ ] Mean + stddev per group
  - [ ] Percentiles (p50, p95, p99, p99.9)
  - [ ] Welch's t-test result
  - [ ] ASCII histogram
  - [ ] Pairwise table for AC7 adversarial shapes

### CI workflow

- [ ] **T7 (AC: 5)** — Create `.github/workflows/timing-tests-nightly.yml`
  - [ ] Cron: `0 3 * * *`
  - [ ] Checkout + dotnet build
  - [ ] Run against staging with bypass token from secrets
  - [ ] Post metrics to App Insights via REST API
  - [ ] On failure, file GitHub Issue via `actions/github-script`
- [ ] **T8 (AC: 5)** — Document timing-test runner in `docs/ops/timing-test-runner.md`

### Dashboards + alerts

- [ ] **T9 (AC: 6)** — Extend App Insights workbook
  - [ ] Panel: 30-day trend of `timing.pvalue`
  - [ ] Panel: p99 timing across valid + adversarial shapes
- [ ] **T10 (AC: 6)** — Alert rule
  - [ ] Critical: `timing.pvalue < 0.01` for 1 night
  - [ ] Warning: `timing.pvalue < 0.05` for 2 consecutive nights

### Self-validation

- [ ] **T11 (AC: 10)** — Create `GET /test/timing-biased-endpoint` behind `#if TIMING_VALIDATION` feature flag
  - [ ] Deliberately asymmetric: returns faster for valid tokens
  - [ ] Compiled only under `TIMING_VALIDATION` define (not in prod)
- [ ] **T12 (AC: 10)** — Create `TimingHarnessSelfValidationTests.cs`
  - [ ] Runs harness against the biased endpoint
  - [ ] Asserts correct detection (p < 0.001)
  - [ ] Runs in normal unit-test suite

### Documentation

- [ ] **T13 (AC: 1, 8)** — Create `tests/Timing/README.md`
  - [ ] Methodology: Welch's t-test + percentile thresholds
  - [ ] Local + CI run instructions
  - [ ] Interpretation guide: what to do when p-value crosses 0.01

## Dev Notes

### Critical guardrails

1. **Noise is the enemy.** Shared CI runners have unpredictable load that pollutes timing measurements. Dedicated runner or staging environment minimum. A noisy p-value < 0.01 false alarm eventually causes the team to disable the test — then the real regression slips through.
2. **Statistical significance requires sample size.** 10k per group is minimum. Fewer samples = wider confidence intervals = more false positives and more false negatives.
3. **Bypass token usage.** Rate limits would skew the run catastrophically. Without bypass, the harness must refuse to run.
4. **Self-validation on a biased endpoint is load-bearing.** If the test doesn't detect a real asymmetry, it detects nothing. Run self-validation as part of every test invocation (not nightly — in unit suite).
5. **p-value < 0.01 is failure, not p < 0.05.** Looser thresholds produce too many false positives given multi-day trend behavior.
6. **Welch's t-test, not Student's t-test.** Welch handles unequal variances — critical for timing data where valid-path variance differs from invalid-path variance.
7. **Warmup phase is non-negotiable.** JIT, connection pool, DNS cache — the first 100 requests are slower. Exclude them or the results are meaningless.

### Source tree

```
tests/Timing/
├── Faktuboh.TimingTests.csproj                             # NEW
├── CapabilityTokenTimingHarness.cs                         # NEW
├── TimingReport.cs                                         # NEW
├── Stats/
│   ├── WelchTTest.cs                                       # NEW
│   └── Percentile.cs                                       # NEW
├── Program.cs                                              # NEW (CLI entry)
├── README.md                                               # NEW
└── tests/
    ├── WelchTTestTests.cs                                  # NEW (textbook fixtures)
    └── TimingHarnessSelfValidationTests.cs                 # NEW

backend/src/Faktuboh.Api/TestEndpoints/
└── TimingBiasedEndpoint.cs                                 # NEW (#if TIMING_VALIDATION)

.github/workflows/
└── timing-tests-nightly.yml                                # NEW

docs/ops/
└── timing-test-runner.md                                   # NEW
```

### Architecture compliance

- **§2.9 #2** (constant-time response) — this story is the mechanical enforcement.
- **§2.10** (pre-launch rehearsals) — timing fitness test.
- **§4.9** (silent-decay SLOs) — `timing.pvalue` is a silent-decay metric.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Test | xUnit v3 | `[Trait("Category", "Timing")]` |
| Stats | None (hand-rolled Welch's + percentile) | Avoid adding a large stats package for this one test |
| HTTP | `System.Net.Http` | HTTP/2 + parallel streams |
| CI | Self-hosted runner OR Azure VM via GitHub Actions | Noise control |

### Testing requirements

- **Unit:** Welch's t-test formula correctness against textbook fixtures.
- **Self-validation:** Harness detects bias on the biased test endpoint.
- **Integration:** Full run against staging — nightly.

### Previous Story Intelligence

**Prerequisites:**
- Story 4.1 — constant-time endpoint stub
- Story 4.5 — full GET handler (what we're actually measuring)
- Story 4.9 — bypass token infrastructure
- Story 4.10 — adversarial shape test baseline

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.12
- Constant-time invariant — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 #2
- Pre-launch rehearsals — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.10
- Silent-decay SLOs — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.9

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Welch's t-test is hand-rolled (no MathNet dep) to keep the test project lean.
- Self-validation test against a deliberately-biased endpoint is the landmine defense — if the test doesn't catch bias there, it won't catch it in prod.

### File List

_Populated during implementation._
