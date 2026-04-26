# Story 4.14: Capability-token replay-across-cache-boundary staged test

Status: ready-for-dev

## Story

As **Wahid**,
I want **a staged test deployed against AFD that generates a token, consumes it, rotates it, then attempts replay of the original URL**,
so that **Vary-header misconfiguration or AFD cache-key leak — the silent security failure Murat flagged — is caught before prod** (AR-044 #6 validation, §2.10 cache-sizing storm drill).

**Covers requirements:** Epic 4 Story 4.14 — §2.10 pre-launch rehearsal #6 (cache-sizing storm drill); AR-016 capability-token cache-key isolation; §4.6 D4.19 cache-key spec `{token}:{accept-language}`; CI gate on `Cache-Control` + `Vary` headers preventing silent decay from >95% hit-rate to 70% [Source: epics.md Epic 4 Story 4.14; architecture.md §2.10, §4.6 D4.19].

**Why this story now:** Story 4.13 ships the throughput harness (NFR-SC4). This story ships the *adversarial* drill — the one Murat called out as "the silent security failure." Story 4.1 set the constant-time invariant; Story 4.12 proved it holds at the application layer; this story proves the invariant holds *across the AFD cache boundary*. Without this, a Vary-header misconfiguration could leak a cached statement to a rotated/revoked token URL — a silent capability-leak indistinguishable from happy-path 200s.

## Acceptance Criteria

### AC1 — Token generated → consumed → rotated → replayed sequence

**Given** the staging environment is live with AFD in front of ACA + SWA per §4.6 D4.19
**When** the test runs the full sequence
**Then** the script:
1. Calls `POST /v1/statements` (authenticated) → obtains token `T1`
2. `GET https://statement.faktuboh.com/t/{T1}` → returns 200 + statement payload + AFD `X-Cache: MISS` header on first request
3. `GET https://statement.faktuboh.com/t/{T1}` (immediate replay) → returns 200 + AFD `X-Cache: HIT` (or `Age > 0`) — confirms cache populated
4. Calls `POST /v1/statements/{statement_id}/rotate` (authenticated) → token `T1` revoked, new token `T2` issued
5. `GET https://statement.faktuboh.com/t/{T1}` (cache-replay attempt) → MUST return constant-time 404 with `code: faktuboh.statements.token_invalid` per Story 4.1 AC3 — NEVER the cached statement payload from steps 2-3
6. `GET https://statement.faktuboh.com/t/{T2}` → returns 200 + new statement payload (fresh cache entry)

**And** the test asserts byte-for-byte that the response body in step 5 is the constant-time rejection envelope (NOT the previously-cached statement) [Source: epics.md Epic 4 Story 4.14 AC1; architecture.md §2.9 public-surface item #2].

### AC2 — Cache-key includes token + Accept-Language + no other Vary dimensions

**Given** §4.6 D4.19 specifies cache key = `{token}:{accept-language}` and nothing else
**When** the test inspects AFD response headers on `/t/*` paths
**Then** the `Vary` header value matches exactly: `Accept-Language` (single value, no other axes)
**And** the `Cache-Control` header matches the spec: `public, max-age=300, stale-while-revalidate=60` (or whatever Story 4.4 ships — this story BASELINES the spec value)
**And** any deviation (e.g., `Vary: Accept-Language, User-Agent` or `Vary: *`) FAILS the test with diagnostic output naming the offending header [Source: architecture.md §4.6 D4.19; epics.md Epic 4 Story 4.14 AC3].

### AC3 — k6 cache-storm drill: 500 rps burst for 30s with replay attempts

**Given** §2.10 rehearsal #6 specifies a 500 rps burst replicating WhatsApp-share viral spike
**When** the script runs
**Then** k6 scenario `cache_storm` uses `constant-arrival-rate` executor at 500 rps for 30s (15,000 total requests)
**And** the request mix includes:
- 80% valid live-token requests (cache-hit path)
- 10% rotated-token requests (must return constant-time 404)
- 10% never-issued unknown-token requests (must return constant-time 404)
**And** the AFD edge cache-hit ratio for valid tokens is > 95% across the burst window
**And** zero rotated-token or unknown-token responses contain the body `"balances"` or any field from the statement payload schema (asserted via JSON schema-mismatch check in k6) [Source: architecture.md §2.10 rehearsal #6; epics.md Epic 4 Story 4.14 AC2].

### AC4 — Constant-time invariant holds across cache boundary

**Given** Story 4.12 already proved constant-time at the application layer
**When** this test measures rotated-token response timing through AFD
**Then** the timing distribution of rotated-token responses (cache-hit path) is statistically indistinguishable from unknown-token responses (cache-miss path) under Welch's t-test at p > 0.05
**And** if the timing diverges, the test fails with the diagnostic that AFD is differentially short-circuiting on cache-key recognition (a silent existence-oracle leak)
**And** the test runs the timing comparison with N=200 samples per branch (statistically meaningful per Story 4.12 baseline) [Source: epics.md Epic 4 Story 4.14; architecture.md §2.9 public-surface #2].

### AC5 — CI gate asserts `Cache-Control` and `Vary` headers on `/t/*` responses match spec

**Given** the silent-decay failure mode is "cache-hit ratio quietly drops from 95% to 70% because someone added `Vary: Authorization` to debug a cookie issue"
**When** the CI workflow runs on PRs touching `Slices/PublicStatements/` or `infra/azure/afd/`
**Then** a header-spec assertion runs against staging:
- `GET /t/{validToken}` → response has `Cache-Control: public, max-age=300, stale-while-revalidate=60` (exact match) AND `Vary: Accept-Language` (exact match, single axis)
- `GET /t/{invalidToken}` → response has `Cache-Control: no-store` (rejection responses MUST NOT cache)
- `GET /t/{validToken}` with `Accept-Language: ar` then `GET /t/{validToken}` with `Accept-Language: en` → returns DIFFERENT cache entries (asserted via different `traceId` or `Etag`)
**And** the gate is non-bypassable; merging a PR that breaks the spec requires explicit reviewer override + audit-log entry [Source: epics.md Epic 4 Story 4.14 AC4; architecture.md §4.6 D4.19; §4.9 silent-decay SLOs].

### AC6 — k6 script `tests/Load/cache-storm-drill.js`

**Given** k6 is already the load-test tool per Story 4.13
**When** the script is authored
**Then** `tests/Load/cache-storm-drill.js` exists with this skeleton:

```javascript
import http from 'k6/http';
import { check } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const cacheLeakCounter = new Counter('cache_leak_violations');
const validTimingTrend = new Trend('rotated_token_response_ms', true);
const unknownTimingTrend = new Trend('unknown_token_response_ms', true);

export const options = {
  scenarios: {
    cache_storm: {
      executor: 'constant-arrival-rate',
      rate: 500,
      timeUnit: '1s',
      duration: '30s',
      preAllocatedVUs: 100,
      maxVUs: 200,
    },
  },
  thresholds: {
    'cache_leak_violations': ['count==0'],  // ZERO leaks tolerated
    'http_req_duration{token_kind:valid}': ['p(95)<300'],
    'http_req_failed{expected_response:true}': ['rate<0.001'],
  },
};

const validTokens = JSON.parse(open('./cache-storm-tokens.json')).valid;
const rotatedTokens = JSON.parse(open('./cache-storm-tokens.json')).rotated;
const unknownTokens = JSON.parse(open('./cache-storm-tokens.json')).unknown;
const bypassToken = __ENV.FAKTUBOH_RATE_LIMIT_BYPASS_TOKEN;

if (!bypassToken) {
  throw new Error('FAKTUBOH_RATE_LIMIT_BYPASS_TOKEN required. See docs/ops/load-test-bypass-token.md');
}

export default function () {
  const r = Math.random();
  const lang = Math.random() > 0.5 ? 'ar' : 'en';
  let token, kind;
  if (r < 0.8) { token = validTokens[Math.floor(Math.random() * validTokens.length)]; kind = 'valid'; }
  else if (r < 0.9) { token = rotatedTokens[Math.floor(Math.random() * rotatedTokens.length)]; kind = 'rotated'; }
  else { token = unknownTokens[Math.floor(Math.random() * unknownTokens.length)]; kind = 'unknown'; }

  const res = http.get(`${__ENV.TARGET}/v1/statements/t/${token}`, {
    headers: { 'X-Faktuboh-Bypass': bypassToken, 'Accept-Language': lang },
    tags: { token_kind: kind },
  });

  // CRITICAL: rotated/unknown must NEVER return statement payload
  if (kind !== 'valid') {
    const body = res.body;
    if (body && (body.includes('"balances"') || body.includes('"contacts"'))) {
      cacheLeakCounter.add(1);  // CACHE LEAK DETECTED
    }
    if (kind === 'rotated') rotatedTimingTrend.add(res.timings.duration);
    if (kind === 'unknown') unknownTimingTrend.add(res.timings.duration);
  }

  check(res, {
    'valid returns 200': (r) => kind !== 'valid' || r.status === 200,
    'rotated returns 404': (r) => kind !== 'rotated' || r.status === 404,
    'unknown returns 404': (r) => kind !== 'unknown' || r.status === 404,
    'rejection body shape constant': (r) => {
      if (kind === 'valid') return true;
      try {
        const j = r.json();
        return j.code === 'faktuboh.statements.token_invalid' && j.status === 404;
      } catch { return false; }
    },
  });
}
```

[Source: epics.md Epic 4 Story 4.14; Story 4.13 k6 patterns]

### AC7 — Seed script generates valid + rotated + unknown token pools

**Given** the test needs three distinct token pools
**When** `scripts/load-test/seed-cache-storm-tokens.mjs` runs
**Then** it:
1. Authenticates as a staging test owner
2. Creates 50 statements, captures the 50 issued tokens → `valid[]`
3. Calls `POST /v1/statements/{id}/rotate` on 25 of them → captures the now-revoked old tokens → `rotated[]`
4. Generates 25 cryptographically random 22-char base64url strings (never issued) → `unknown[]`
5. Writes `tests/Load/cache-storm-tokens.json`
**And** the script is idempotent (cleans up prior load-test data before seeding)
**And** the unknown-token pool is generated with `crypto.randomBytes(16)` to ensure they are well-formed but never recorded in `capability_tokens` [Source: Story 4.10 token format; Story 4.13 AC7 seed pattern].

### AC8 — Header-spec audit script

**Given** the CI gate from AC5 needs a script
**When** `scripts/load-test/audit-cache-headers.mjs` runs
**Then** it:
1. `GET /t/{validToken}` → asserts `Cache-Control` matches spec exactly + `Vary: Accept-Language` (single axis)
2. `GET /t/{invalidToken}` → asserts `Cache-Control: no-store`
3. `GET /t/{validToken}` with `Accept-Language: ar` 5 times → asserts `Age` increases (confirms cached)
4. `GET /t/{validToken}` with `Accept-Language: en` once → asserts `X-Cache: MISS` or `Age: 0` (different cache key)
**And** any spec mismatch exits with non-zero status + structured diagnostic naming the offending header [Source: epics.md Epic 4 Story 4.14 AC4].

### AC9 — Results post to Golden-signals dashboard

**Given** load-test results should be trended across releases per Story 4.13 AC5
**When** the cache-storm drill completes
**Then** results post to App Insights as `load_test_run` event with properties:
```json
{
  "scenario": "cache_storm",
  "rps": 500,
  "duration_s": 30,
  "cache_hit_ratio": 0.97,
  "cache_leak_violations": 0,
  "rotated_p95_ms": 45,
  "unknown_p95_ms": 47,
  "timing_divergence_pvalue": 0.83,
  "pass": true
}
```
**And** the dashboard shows historical pass/fail per release for this drill [Source: architecture.md §4.6 D4.20; Story 4.13 AC5].

### AC10 — Release-candidate gate: cache-storm drill blocks prod deploy on fail

**Given** `deploy-prod.yml` already gates on Story 4.13's throughput suite
**When** this story extends the gate
**Then** `load-test-release-candidate.yml` includes the cache-storm drill alongside throughput tests
**And** prod deploy blocks until both pass
**And** manual override path is the same as Story 4.13 AC6 (approval + audit log) [Source: Story 4.13 AC6].

## Tasks / Subtasks

### k6 script

- [ ] **T1 (AC: 3, 6)** — Create `tests/Load/cache-storm-drill.js` per AC6 skeleton
  - [ ] 500 rps × 30s `constant-arrival-rate`
  - [ ] 80/10/10 valid/rotated/unknown mix
  - [ ] Custom `cache_leak_violations` counter (zero-tolerance threshold)
  - [ ] Per-kind tag for `http_req_duration` thresholds
- [ ] **T2 (AC: 4)** — Welch's t-test post-processor
  - [ ] Create `scripts/load-test/welch-ttest-cache-timing.mjs`
  - [ ] Reads k6 summary JSON; extracts `rotated_token_response_ms` and `unknown_token_response_ms` samples
  - [ ] Computes Welch's t-test statistic + p-value
  - [ ] Asserts p > 0.05 (timings statistically indistinguishable)
  - [ ] Reuse Story 4.12 timing-test math primitives if available

### Seed script

- [ ] **T3 (AC: 7)** — Create `scripts/load-test/seed-cache-storm-tokens.mjs`
  - [ ] 50 valid + 25 rotated + 25 unknown tokens
  - [ ] Idempotent cleanup (revokes prior `cache-storm-test-*` tokens)
  - [ ] Writes `tests/Load/cache-storm-tokens.json` (gitignored)

### Header-spec audit

- [ ] **T4 (AC: 2, 5, 8)** — Create `scripts/load-test/audit-cache-headers.mjs`
  - [ ] Asserts `Vary: Accept-Language` exact match (no other axes)
  - [ ] Asserts `Cache-Control: public, max-age=300, stale-while-revalidate=60` for valid responses
  - [ ] Asserts `Cache-Control: no-store` for rejection responses
  - [ ] Asserts AR vs EN return different cache entries
  - [ ] Exits non-zero on any spec mismatch with diagnostic

### Replay sequence integration test

- [ ] **T5 (AC: 1)** — Create `scripts/load-test/cache-replay-sequence.mjs`
  - [ ] Generates → consumes → rotates → replays per AC1 6-step sequence
  - [ ] Asserts step 5 returns constant-time 404 (NOT cached statement)
  - [ ] Body byte-equality check against the rejection envelope schema

### CI integration

- [ ] **T6 (AC: 5, 10)** — Extend `.github/workflows/load-test-release-candidate.yml`
  - [ ] Add `cache-storm-drill` job depending on seed + bypass-token availability
  - [ ] Add `cache-headers-audit` job (runs on every PR touching `Slices/PublicStatements/` or `infra/azure/afd/`)
  - [ ] Add `cache-replay-sequence` job (release-candidate only)
  - [ ] All three jobs block prod deploy on failure
- [ ] **T7 (AC: 9)** — Post results to App Insights via `scripts/load-test/post-results.mjs` (extend Story 4.13 helper)

### Documentation

- [ ] **T8 (AC: 5, 10)** — Update `docs/ops/load-test-playbook.md` with cache-storm section
  - [ ] How to interpret `cache_leak_violations > 0` (P0 incident)
  - [ ] Diagnostic procedure when `Vary` drifts from spec
  - [ ] Bypass token rotation reminder

### Backend support

- [ ] **T9 (AC: 1)** — Verify `POST /v1/statements/{id}/rotate` exists or stub it for staging
  - [ ] Per Story 4.4 (statement generation) — if rotation endpoint not yet shipped, add a minimal stub gated to staging-only that revokes old token + issues new one (production rotation flow is Epic 7 Story 7.x)
  - [ ] If rotation endpoint exists, this task is a verification only

## Dev Notes

### Critical guardrails (do not violate)

1. **`cache_leak_violations` is zero-tolerance.** A single response from a rotated/unknown token containing statement payload is a CRITICAL incident. The k6 threshold `'cache_leak_violations': ['count==0']` is the build gate.
2. **`Vary` header MUST be exactly `Accept-Language`.** Adding any other axis (User-Agent, Authorization, Cookie) is the canonical silent-decay failure mode. The CI gate enforces exact-match.
3. **Constant-time across cache boundary.** Application-layer constant-time (Story 4.12) is necessary but not sufficient. AFD's cache layer can introduce its own timing oracle by short-circuiting on cache-key recognition. Welch's t-test on rotated vs unknown responses validates this.
4. **Staging only.** Like Story 4.13, this drill against prod would trigger cost cap + alert storms. The bypass token + cap override are staging-only.
5. **Bypass token required.** Without it, rate-limit 429s contaminate timing measurements and skew the t-test.
6. **`crypto.randomBytes(16)` for unknown-token pool.** Using `Math.random()` produces low-entropy tokens that may have a (vanishing) probability of matching a real token. Use the same RNG class Story 4.10 uses.
7. **Header-spec audit runs on AFD/SWA infra changes too.** A change to `staticwebapp.config.json` or AFD bicep can silently break headers. The CI gate watches both code paths.

### Source tree — files to create or touch

```
tests/Load/
├── cache-storm-drill.js                                    # NEW (k6 scenario)
└── cache-storm-tokens.json                                 # NEW (seeded; gitignored)

scripts/load-test/
├── seed-cache-storm-tokens.mjs                             # NEW
├── audit-cache-headers.mjs                                 # NEW
├── cache-replay-sequence.mjs                               # NEW
├── welch-ttest-cache-timing.mjs                            # NEW
└── post-results.mjs                                        # MODIFY (extend for cache-storm scenario)

.github/workflows/
└── load-test-release-candidate.yml                         # MODIFY (add 3 cache jobs)

docs/ops/
└── load-test-playbook.md                                   # MODIFY (cache-storm section)

backend/src/Faktuboh.Api/Slices/PublicStatements/
└── (verify rotation endpoint exists OR stub for staging — see T9)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Load + adversarial | 500 rps × 30s with rotated/unknown mix | k6 `constant-arrival-rate` |
| Header-spec | Cache-Control + Vary exact-match | Node script (CI gate) |
| Statistical | Welch's t-test on cross-cache-boundary timing | Node script with `simple-statistics` |
| Sequence | Generate→consume→rotate→replay | Node script |

**Forbidden:** running against prod, omitting bypass token, weakening `cache_leak_violations` threshold below `count==0`, using `Math.random()` for unknown-token generation.

### Project Structure Notes

This story extends the load-test infrastructure baseline from Story 4.13 (k6 + bypass token + seed script + CI workflow + dashboard wiring). It does NOT scaffold new backend slices — the only backend touch is verifying or stubbing the rotation endpoint (T9). All new files are under `tests/Load/`, `scripts/load-test/`, and CI workflow edits.

### Architecture compliance

- **§2.10 rehearsal #6** — cache-sizing storm drill is THIS story.
- **§4.6 D4.19** — cache-key spec `{token}:{accept-language}`; `Vary: Accept-Language` only.
- **§4.9 silent-decay SLOs** — cache-hit ratio drift detection.
- **§2.9 public-surface item #2** — constant-time invariant; this story validates across cache boundary.
- **AR-016 capability-token cache-key isolation** — the entire reason this drill exists.
- **AR-044 #6** — pre-launch operational rehearsal validation.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Load test | `k6` | Already installed per Story 4.13 |
| Stats | `simple-statistics` (npm) | Welch's t-test |
| HTTP | `node-fetch` or built-in `fetch` (Node 22) | Header-spec audit |
| Crypto | Node built-in `crypto.randomBytes` | Unknown-token generation |
| CI | GitHub Actions | Gate prod deploy |

### Testing requirements

- **Load + adversarial:** k6 cache-storm scenario per AC3, AC6.
- **Header-spec:** Audit script per AC2, AC5, AC8.
- **Sequence:** Replay sequence per AC1.
- **Statistical:** Welch's t-test per AC4.
- **CI integration:** Three new jobs in release-candidate workflow + one PR-trigger job for header-spec.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 4.1 — capability-token scheme + constant-time invariant (validated at app layer)
- Story 4.4 — statement generation + rotation endpoint
- Story 4.5 — public statement view
- Story 4.9 — rate-limit bypass token mechanism
- Story 4.11 — cost cap (staging override applies)
- Story 4.12 — application-layer constant-time test (timing math reused)
- Story 4.13 — k6 baseline + CI workflow + bypass token + seed script patterns
- Story 0.10 — staging Azure environment

**Coordination notes:**
- This story EXTENDS Story 4.13's CI workflow rather than creating a parallel one.
- The header-spec audit runs on every PR touching public-statement code or AFD infra (faster feedback than the release-candidate gate alone).
- If Story 4.4 hasn't shipped a rotation endpoint, T9 stubs one for staging only — production rotation flow is Epic 7.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.14
- Cache-sizing storm drill — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.10 rehearsal #6
- Cache-key spec — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.6 D4.19
- Constant-time invariant — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 public-surface item #2
- Silent-decay SLOs — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.9
- Story 4.1 capability-token scheme — [_bmad-output/implementation-artifacts/4-1-capability-token-scheme-with-constant-time-response.md](./4-1-capability-token-scheme-with-constant-time-response.md)
- Story 4.13 k6 baseline — [_bmad-output/implementation-artifacts/4-13-k6-public-statement-throughput-harness.md](./4-13-k6-public-statement-throughput-harness.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- This is the AR-044 #6 rehearsal validation — the silent-security-failure drill Murat called out specifically.
- Zero `cache_leak_violations` is a hard gate; one leak is a P0 incident.
- The Welch's t-test extension validates Story 4.12's invariant across the cache boundary, not just at the application layer.

### File List

_Populated during implementation._
