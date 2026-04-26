# Story 6.3: View current system rate pre-confirmation

Status: ready-for-dev

## Story

As **a user about to execute an exchange**,
I want **to see the current system rate between the two denominations before I confirm**,
so that **I can sanity-check the rate or agree on an overridden rate** (FR23).

**Covers requirements:** Epic 6 Story 6.3 — FR23 (view current rate pre-confirmation); NFR-I3 (rate response includes `as_of` timestamp); < 100ms response time [Source: epics.md Epic 6 Story 6.3; prd.md FR23].

**Why this story now:** Story 6.4 (Exchange operation) needs this endpoint for the rate-display step BEFORE the user confirms an override. Story 6.8 (freshness banner) reads the same `as_of` timestamp this endpoint returns. Shipping this small read-side endpoint first decouples 6.4's UI from the read-side API shape.

## Acceptance Criteria

### AC1 — `GET /v1/rates/current?from=XAU&to=USD` returns latest rate

**Given** Story 6.1's `rates` table is populated
**When** the user (authenticated) calls `GET /v1/rates/current?from=XAU&to=USD`
**Then** the response is `200 OK` with body:
```json
{
  "denominationFrom": "XAU",
  "denominationTo": "USD",
  "rate": "92.50",
  "displayRate": "1g gold = $92.50",
  "asOf": "2026-04-26T14:00:00Z",
  "sourceProvider": "metals-api.com"
}
```
**And** `rate` is a JSON string (not number) per architecture §5.2.5
**And** `displayRate` is a pre-formatted string for direct UI rendering (locale-aware on the frontend; this is the EN representation; the FE re-formats per locale)
**And** if no rate exists for the pair, returns `404 Not Found` with RFC 9457 code `faktuboh.rates.pair_not_found`
**And** if `from` or `to` is not in `CurrencyRegistry`, returns `400 Bad Request` with code `faktuboh.rates.unsupported_denomination` [Source: epics.md Epic 6 Story 6.3 AC; architecture.md §5.2.5].

### AC2 — Rate retrieved via indexed lookup

**Given** the index `ix_rates_pair_validfrom` from Story 6.1
**When** the endpoint queries
**Then** the SQL is `SELECT * FROM rates WHERE denomination_from = $1 AND denomination_to = $2 ORDER BY valid_from DESC LIMIT 1`
**And** the index is used (asserted via integration test EXPLAIN ANALYZE) [Source: Story 6.1 AC1 index].

### AC3 — Response time < 100ms (NFR commitment)

**Given** epics.md AC4 specifies < 100ms response time
**When** measured against Testcontainers Postgres with realistic data volume
**Then** p95 response time < 100ms
**And** if the rate is cached at the application layer (in-memory, 60s TTL), even faster
**And** integration test asserts via timing measurement [Source: epics.md Epic 6 Story 6.3 AC4].

### AC4 — Cross-currency derivation via gold base

**Given** the table only stores `XAU → currency` rows (Story 6.1 gold-base normalization)
**When** the user requests a non-gold pair (e.g., `from=USD&to=EUR`)
**Then** the endpoint computes the cross-rate via gold pivot:
- Look up `XAU → USD` (= ratio_us)
- Look up `XAU → EUR` (= ratio_eur)
- Cross-rate `USD → EUR` = `ratio_eur / ratio_us`
**And** the cross-rate is computed at `numeric(28, 8)` precision (no intermediate `(19, 4)` collapse per AR-035)
**And** `as_of` returned is the OLDER of the two underlying `valid_from` timestamps (most-conservative freshness)
**And** the `sourceProvider` returned is the one that fed BOTH underlying rates (if mixed providers, returns "mixed" + warning header `X-Faktuboh-Rate-Source: mixed`)
**And** an integration test asserts cross-rate accuracy against known-good fixtures [Source: epics.md Epic 6 Story 6.1 gold-base; architecture.md §5.2.5].

### AC5 — XAU → XAU returns 1.0

**Given** edge case: same denomination on both sides
**When** the user requests `from=XAU&to=XAU` (or any same-denom pair)
**Then** the response is immediate `{ rate: "1.0000", displayRate: "1g gold = 1g gold", asOf: "{now}", sourceProvider: "system" }`
**And** no DB query is made
**And** the response is still < 100ms

### AC6 — `as_of` is the rate's `valid_from`, NOT `now()` or `recorded_at`

**Given** NFR-I3 specifies the freshness indicator references when the rate was VALID, not when fetched
**When** the response is built
**Then** `asOf` = the row's `valid_from` column value
**And** Story 6.8's freshness check (4h/24h SLA) uses the SAME timestamp [Source: prd.md NFR-I3; Story 6.8].

### AC7 — Caching layer (60s in-memory)

**Given** the same rate is requested many times within a single hour (cron tick is hourly, no point re-querying for sub-second freshness)
**When** the endpoint receives a request
**Then** it consults an in-memory cache keyed by `(from, to)`:
- Cache hit (< 60s old) → return cached rate
- Cache miss → query DB + cache result for 60s
**And** cache invalidation: the `RatesIngestedEvent` (Story 6.1 AC7) handler clears the cache for ingested pairs
**And** integration test asserts: post-ingest, the next request reflects the new rate (not the stale cache) [Source: epics.md Epic 6 Story 6.3 AC4].

### AC8 — Authorization required

**Given** rate viewing is owner-context only
**When** the endpoint is called
**Then** `.RequireAuthorization()` is set on the route group
**And** unauthenticated requests return 401
**And** the endpoint does NOT leak rates to anonymous callers (rates are not PII, but they're also not public — keep boundary clean)

### AC9 — Telemetry: `rate_query_current` event

**Given** AR-047 telemetry primitive
**When** the endpoint serves a request
**Then** `TelemetryEmitter.Emit("rate_query_current", { opaque_user_id, denomination_pair_hash, cache_hit: bool, response_ms })` fires
**And** PII guard validates [Source: Story 0.14].

### AC10 — Frontend client method

**Given** Story 6.4 (Exchange UI) needs this rate
**When** the NSwag-generated TS client is updated
**Then** `libs/core/api/rates.client.ts` exposes `getCurrentRate(from, to): Observable<CurrentRateResponse>`
**And** the response type is generated from the C# DTO unchanged
**And** the OpenAPI snapshot updates (Story 3.11 drift gate validates)

## Tasks / Subtasks

### Backend — Slice + endpoint

- [ ] **T1 (AC: 1, 8)** — Create `backend/src/Faktuboh.Api/Slices/Rates/Rates.Endpoints.cs`
  - [ ] `MapGroup("/v1/rates").RequireAuthorization()` + `ProcessingActivity.OwnerLedger`
  - [ ] `GET /v1/rates/current` endpoint
- [ ] **T2 (AC: 1)** — Create `GetCurrentRateRequest` (query params: from, to)
  - [ ] FluentValidation: from/to in `CurrencyRegistry`, from != to (or handled by AC5 case)
- [ ] **T3 (AC: 1)** — Create `CurrentRateResponse` per AC1 shape
- [ ] **T4 (AC: 1, 4, 5)** — Create `GetCurrentRateHandler`
  - [ ] Same-denom AC5 short-circuit
  - [ ] Direct lookup via `IRateRepository.GetLatestAsync(from, to)`
  - [ ] Cross-currency derivation per AC4
  - [ ] RFC 9457 errors for missing pair / unsupported denomination

### Backend — Caching

- [ ] **T5 (AC: 7)** — Add `IMemoryCache` (Microsoft.Extensions.Caching.Memory) dependency
  - [ ] 60s TTL per cache entry
  - [ ] Cache key: `$"rate:{from}:{to}"`
- [ ] **T6 (AC: 7)** — Add cache invalidation handler
  - [ ] Wolverine handler `RatesIngestedCacheInvalidator` consuming `RatesIngestedEvent` (Story 6.1)
  - [ ] Removes cache entries for all currencies in the event payload

### Backend — Error catalog

- [ ] **T7 (AC: 1)** — Register error codes
  - [ ] `faktuboh.rates.pair_not_found` (404)
  - [ ] `faktuboh.rates.unsupported_denomination` (400)

### Backend — Telemetry

- [ ] **T8 (AC: 9)** — Wire `rate_query_current` event

### Frontend — Generated client

- [ ] **T9 (AC: 10)** — Generate NSwag client method
  - [ ] `libs/core/api/rates.client.ts` with `getCurrentRate(from, to)`
  - [ ] OpenAPI snapshot updated

### Tests

- [ ] **T10 (AC: 1, 2, 3, 4, 5, 6)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/Rates/`
  - [ ] `GetCurrentRateEndpointTests` — happy path, missing pair, unsupported denom, same-denom
  - [ ] Cross-currency derivation correctness against fixtures
  - [ ] Performance: p95 < 100ms
  - [ ] Index usage assertion via EXPLAIN ANALYZE
- [ ] **T11 (AC: 7)** — Cache integration tests
  - [ ] First call DB-hit, second call cache-hit
  - [ ] Post-ingest event clears cache
- [ ] **T12 (AC: 8)** — Auth test: unauthenticated returns 401
- [ ] **T13 (AC: 9)** — Telemetry assertion

## Dev Notes

### Critical guardrails (do not violate)

1. **`numeric(28, 8)` precision in cross-currency derivation.** Don't collapse to `(19, 4)` in the intermediate `ratio_eur / ratio_us` division — that's the dual-precision rule from architecture §5.2.5. Only the user-display layer collapses.
2. **`as_of` = `valid_from`, NEVER `now()`.** This is the freshness signal Story 6.8 reads.
3. **Cache invalidation via outbox event, not TTL alone.** A 60s cache TTL is fine for steady-state, but post-ingest the cache MUST clear immediately so users see fresh rates.
4. **Same-denom returns 1.0 without DB hit.** Edge case worth short-circuiting.
5. **Authorization required.** Rates aren't PII but they're not public. Keep the boundary clean.
6. **Cross-rate `as_of` is the OLDER of the two underlying timestamps.** Conservative freshness — don't pretend the derived rate is fresher than its weakest input.
7. **`sourceProvider: "mixed"`** when underlying rates are from different providers — signals provenance ambiguity.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Api/Slices/Rates/
│   ├── Rates.Endpoints.cs                                  # NEW
│   ├── GetCurrentRate/
│   │   ├── GetCurrentRateRequest.cs                        # NEW
│   │   ├── GetCurrentRateResponse.cs                       # NEW (CurrentRateResponse)
│   │   ├── GetCurrentRateRequestValidator.cs               # NEW
│   │   └── GetCurrentRateHandler.cs                        # NEW
│   └── Projections/
│       └── RatesIngestedCacheInvalidator.cs                # NEW
└── Faktuboh.Application/Errors/
    └── ErrorCatalog.cs                                     # MODIFY (+2 codes)

backend/tests/Faktuboh.Api.Tests/Slices/Rates/
├── GetCurrentRateEndpointTests.cs                          # NEW
└── RateCacheTests.cs                                       # NEW

libs/core/api/
└── rates.client.ts                                         # NEW (NSwag-generated)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Integration | Endpoint shapes + cross-rate + perf | Testcontainers Postgres + WebApplicationFactory |
| Cache | Hit/miss + invalidation | Integration |
| Auth | 401 on anonymous | Integration |

### Architecture compliance

- **FR23** — view current rate pre-confirmation.
- **NFR-I3** — `as_of` timestamp.
- **§5.2.5** — `numeric(28,8)` precision in cross-currency derivation.
- **§4.4 D4.9** — RFC 9457.
- **§4.4 D4.12** — outbox-based cache invalidation.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Caching | Microsoft.Extensions.Caching.Memory | 60s in-memory cache |
| Validation | FluentValidation | |
| TS client | NSwag | |

### Testing requirements

- **Integration:** endpoint + cross-rate accuracy + perf < 100ms.
- **Cache:** hit/miss/invalidation.
- **Auth:** 401 boundary.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.4 — RFC 9457 + ErrorCatalog
- Story 0.14 — telemetry primitive
- Story 6.1 — `rates` table + `IRateRepository.GetLatestAsync` + `RatesIngestedEvent`

**Downstream consumers:**
- Story 6.4 — Exchange UI shows current rate before confirm
- Story 6.8 — Freshness banner reads `asOf`

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.3
- FR23, NFR-I3 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Dual-precision — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- Story 6.1 rate-feed — [_bmad-output/implementation-artifacts/6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md](./6-1-hourly-rate-feed-ingestion-with-gold-as-base-unit.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Cross-rate via gold pivot keeps `numeric(28,8)` precision throughout.
- Cache invalidation via outbox event ensures freshness post-ingest.
- `as_of` = `valid_from` (consistent with Story 6.8 freshness logic).

### File List

_Populated during implementation._
