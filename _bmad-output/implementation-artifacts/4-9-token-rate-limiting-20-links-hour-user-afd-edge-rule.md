# Story 4.9: Token rate limiting — 20 links/hour/user + AFD edge rule

Status: ready-for-dev

## Story

As **Wahid**,
I want **link generation capped at 20 per user per hour + AFD edge rate-limit of 600 req/min per `/statement.*/t/*` + 60 views/minute per link**,
so that **statement-link abuse is rate-limited at two layers** (FR42, NFR-S5, AR-017).

**Covers requirements:** Epic 4 — FR42 (token rate limiting), NFR-S5 (rate limits), AR-017 (two-layer rate limit), §4.3 D4.7 app-level rate limiting, §4.6 D4.19 edge ruleset [Source: epics.md Epic 4 Story 4.9].

**Why this story now:** Stories 4.1 + 4.4 expose the issuance + read surfaces. Without rate-limiting, the public surface is abuse-prone the moment it ships. This story wires both app-level and edge-level (SWA via AFD-equivalent CDN) controls.

## Acceptance Criteria

### AC1 — App-level rate limit: 20 link-generations per user per hour

**Given** `Microsoft.AspNetCore.RateLimiting` middleware is registered
**When** `POST /v1/contacts/{contactId}/statements` is called
**Then** a named policy `statement-link-gen` applies:
- Partition key: `owner_subject_id` from the authenticated principal
- Fixed window: 20 requests per 60 minutes, queue limit 0 (no queueing)
**And** the 21st request in the window returns HTTP 429 with RFC 9457 body `faktuboh.statements.link_generation_rate_limit_exceeded`
**And** the 429 includes `Retry-After: <seconds>` + `X-RateLimit-Remaining: 0` + `X-RateLimit-Limit: 20` + `X-RateLimit-Reset: <unix-epoch>` headers [Source: architecture.md §4.3 D4.7].

### AC2 — Per-token view rate limit: 60 views/minute

**Given** `GET /v1/statements/t/{token}` is the public read surface
**When** a single token is requested more than 60 times in any rolling 60-second window
**Then** the 61st request within the window returns HTTP 429 with RFC 9457 body `faktuboh.statements.view_rate_limit_exceeded` (distinct code from the link-gen limit)
**And** the partition key is the `token_hash` (not IP — multiple debtors may share IP; partitioning by token prevents one noisy viewer from locking out others)
**And** the 429 response is the same **SIZE AND SHAPE** as the "token invalid" constant-time response (Story 4.1) so that rate-limit hits do not leak timing about token validity — the 429 is always served AFTER the full validator sequence runs [Source: epics.md Story 4.9; architecture.md §4.3 D4.7].

### AC3 — Per-IP-hash view rate limit: 120 views/minute

**Given** IP-based rate limiting at the app level is a second axis
**When** a single IP hashes to more than 120 views across any tokens in a 60-second rolling window
**Then** 429 returns with code `faktuboh.statements.view_rate_limit_exceeded`
**And** the partition key is `SHA256(request.RemoteIpAddress.ToString() + daily_salt)` — daily salt rotates at 00:00 UTC to prevent long-term re-identification
**And** the IP itself is never logged or emitted to telemetry (NetArchTest guard) [Source: architecture.md §2.9 data-lifecycle hardening; §4.3 D4.7].

### AC4 — Edge (SWA/AFD) rate limit rule: 600 req/min per `/t/*`

**Given** per the delegated decision, SWA's built-in CDN is primary topology; AFD Standard is documented revisit-trigger alternative
**When** edge rate limits are configured
**Then** for SWA primary path: the `staticwebapp.config.json` route configuration defines `/t/*` under the global forward-without-auth rule; rate-limiting at SWA free tier is limited (DoS protection is included, no custom rate-limit rules) — app-level limits (AC1-AC3) carry the full rate-limit burden
**And** for AFD fallback (documented in `docs/ops/afd-fallback-rate-limits.md`): WAF Standard custom rule `rate_limit_statement_path` with `matchConditions: [{ path starts_with: "/t/" }], action: Block, rateLimitThreshold: 600, rateLimitDurationInMinutes: 1`
**And** when the revisit trigger fires (sustained bot traffic > 1000 req/hr per §4.6 D4.19), the operations runbook directs migration from SWA → AFD Premium with the documented rule [Source: architecture.md §4.6 D4.19; §7.4.1 SWA cascade; delegated decisions].

### AC5 — Rate-limit policy names exposed for tests and alerts

**Given** rate-limit breaches are operational events worth alerting on
**When** the middleware rejects a request
**Then** an OTel span event fires with name `rate_limit.rejected` + attributes `{ policy: "statement-link-gen" | "statement-view-per-token" | "statement-view-per-ip" , partition_hash: SHA256(…) }`
**And** App Insights dashboard "Golden signals" adds a panel: rate-limit rejections per policy per minute
**And** alert: > 100 rejections across all policies in 5 minutes → email [Source: architecture.md §4.6 D4.20].

### AC6 — RFC 9457 error codes registered

**Given** `ErrorCatalog` is the source of truth for codes
**When** this story registers codes
**Then** two codes exist:
- `faktuboh.statements.link_generation_rate_limit_exceeded` (429) — Story 4.4 pre-registered its schema; this story wires the behavior
- `faktuboh.statements.view_rate_limit_exceeded` (429)
**And** each carries Transloco key for UX-DR32 friendly inline error rendering [Source: architecture.md §4.4 D4.9].

### AC7 — UX friendly error rendering per UX-DR32

**Given** the user tests "generate link" repeatedly
**When** the 429 returns on the owner workspace
**Then** the owner UI displays an inline `<p-message severity="warn">` below the Share button:
- Arabic: `"تم تجاوز حد إنشاء الروابط. حاول بعد ساعة."`
- English: `"Link generation limit reached. Try again in an hour."`
**And** the Share button is disabled for the remainder of the window (computed client-side from `Retry-After` header)
**And** no toast — inline only per UX-DR27 + UX-DR32 [Source: ux-design-specification.md UX-DR32; epics.md Story 4.9].

### AC8 — Internal testing exemption via env var

**Given** dogfood and load testing may exceed these limits legitimately
**When** the `FAKTUBOH_RATE_LIMIT_BYPASS_TOKEN` env var is set
**Then** requests carrying header `X-Faktuboh-Bypass: {match-token}` skip all rate-limit policies
**And** the bypass token is generated via `openssl rand -base64 32` and stored in ACA env vars (never checked in)
**And** in production, this token is rotated monthly and used only by the k6 harness (Story 4.13) [Source: architecture.md §2.10 throughput fitness test; Story 4.13].

### AC9 — Rate-limit middleware order

**Given** middleware order affects behavior
**When** `Faktuboh.Api/Program.cs` assembles the pipeline
**Then** the order is:
1. `UseForwardedHeaders` (to get true client IP behind AFD/SWA)
2. `UseRouting`
3. `UseAuthentication`
4. `UseAuthorization`
5. `UseRateLimiter` (AFTER auth so per-user policies can use the principal)
6. Endpoint dispatch
**And** a middleware-order test asserts this sequence via `TestServer` [Source: architecture.md §3.5].

### AC10 — Response-body shape equivalence for the view-rate-limit (constant-time)

**Given** the constant-time invariant from Story 4.1 AC3 must hold
**When** the view endpoint returns 429 for per-token or per-IP-hash overflow
**Then** the response **size** matches the `token_invalid` 404 response to the nearest padded multiple of 64 bytes
**And** response headers `X-RateLimit-*` ARE present (informational only — rate-limit existence is not a secret; timing isn't leaked)
**And** an integration test asserts byte-length difference ≤ 64 bytes between 404 and 429 response bodies [Source: epics.md Story 4.9; architecture.md §2.9 #2].

## Tasks / Subtasks

### Backend — Rate-limit middleware

- [ ] **T1 (AC: 1, 9)** — Register `AddRateLimiter` in `Program.cs` with policy `statement-link-gen`
  - [ ] `FixedWindowRateLimiter` — permitLimit: 20, window: TimeSpan.FromHours(1), queueLimit: 0
  - [ ] Partition: `owner_subject_id` from `HttpContext.User.FindFirst("sub").Value`
- [ ] **T2 (AC: 2)** — Policy `statement-view-per-token`
  - [ ] `SlidingWindowRateLimiter` — permitLimit: 60, window: TimeSpan.FromMinutes(1)
  - [ ] Partition: `token_hash` hex (parsed from route param)
- [ ] **T3 (AC: 3)** — Policy `statement-view-per-ip`
  - [ ] `SlidingWindowRateLimiter` — permitLimit: 120, window: TimeSpan.FromMinutes(1)
  - [ ] Partition: `SHA256(RemoteIpAddress + DailySalt)` — daily salt stored in-memory, rotates at 00:00 UTC
  - [ ] Background service refreshes salt at midnight
- [ ] **T4 (AC: 1, 2)** — Wire policies to endpoints
  - [ ] `POST /v1/contacts/{contactId}/statements` → `.RequireRateLimiting("statement-link-gen")`
  - [ ] `GET /v1/statements/t/{token}` → `.RequireRateLimiting("statement-view-per-token").RequireRateLimiting("statement-view-per-ip")` (stacked)
- [ ] **T5 (AC: 1, 2, 6)** — Customize rejection response
  - [ ] `options.OnRejected = async (ctx, cancellationToken) => { ctx.HttpContext.Response.StatusCode = 429; write problem+json with code per policy name; include X-RateLimit-* headers }`
  - [ ] The rejection writer uses `ErrorCatalog` to resolve code → problem details

### Backend — Daily salt rotation

- [ ] **T6 (AC: 3)** — Create `DailyIpSaltService : BackgroundService`
  - [ ] Holds `_currentSalt: byte[32]` in-memory
  - [ ] `GenerateSalt()` via `RandomNumberGenerator`
  - [ ] Rotates at 00:00 UTC
  - [ ] Exposed via `IDailyIpSalt.ComputeHash(IPAddress)` singleton

### Backend — Bypass token

- [ ] **T7 (AC: 8)** — Create `RateLimitBypassMiddleware`
  - [ ] Reads `FAKTUBOH_RATE_LIMIT_BYPASS_TOKEN` env var on startup
  - [ ] If request header `X-Faktuboh-Bypass` matches (constant-time compare), marks `HttpContext.Items["bypass"] = true`
  - [ ] Rate-limit `PartitionedRateLimiter` filter checks the item and returns `RateLimitPartition.GetNoLimiter` when bypassed

### Backend — Telemetry

- [ ] **T8 (AC: 5)** — OTel span events on rejection
  - [ ] `Activity.Current?.AddEvent(new ActivityEvent("rate_limit.rejected", tags: ...))`
  - [ ] App Insights dashboard query: `customEvents | where name == "rate_limit.rejected" | summarize count() by policy, bin(timestamp, 1m)`
  - [ ] Alert rule: `count > 100 in 5 min`

### Backend — Error catalog

- [ ] **T9 (AC: 6)** — Register codes
  - [ ] `faktuboh.statements.link_generation_rate_limit_exceeded` — already declared in 4.4, wire behavior here
  - [ ] `faktuboh.statements.view_rate_limit_exceeded` — NEW

### Backend — Constant-time equivalence

- [ ] **T10 (AC: 10)** — Pad 429 response body to match 404 body length ±64 bytes
  - [ ] Problem Details body includes a `debug` field with constant-length padding: `"padding": "<64-char base64url>"` — injected in the rejection writer
  - [ ] Integration test: fire a 429 and a 404; compare body lengths

### Edge (SWA/AFD) configuration

- [ ] **T11 (AC: 4)** — Update `staticwebapp.config.json` for public-statement SWA app
  - [ ] Document in comments that per-route rate-limiting is NOT configurable on SWA free tier
  - [ ] Routes section: `{ "route": "/t/*", "rewrite": "/index.html" }` (SPA routing)
- [ ] **T12 (AC: 4)** — Create `docs/ops/afd-fallback-rate-limits.md`
  - [ ] AFD WAF Standard custom rule JSON
  - [ ] Migration runbook SWA → AFD when revisit trigger fires
  - [ ] ARM/Bicep snippet for the WAF rule

### Frontend — UX

- [ ] **T13 (AC: 7)** — Handle 429 on owner Share button
  - [ ] Catch 429 response in `statements.client.ts`
  - [ ] Parse `Retry-After` header
  - [ ] Emit disabled state + inline `<p-message severity="warn">` with computed countdown
  - [ ] Transloco keys: `rate_limit.statement_link_gen_exceeded.ar/en`

### Tests

- [ ] **T14 (AC: 1, 9)** — Integration test `LinkGenerationRateLimitTests`
  - [ ] Submit 20 link-generation requests → all succeed (201)
  - [ ] 21st request → 429 with `X-RateLimit-*` headers
  - [ ] After 1h (mock `ISystemClock`) → window resets, request succeeds
- [ ] **T15 (AC: 2)** — `ViewPerTokenRateLimitTests`
  - [ ] 60 GETs to same token → succeed
  - [ ] 61st → 429 with correct code
- [ ] **T16 (AC: 3)** — `ViewPerIpRateLimitTests`
  - [ ] 120 GETs from same synthetic IP across many tokens → succeed
  - [ ] 121st → 429
- [ ] **T17 (AC: 8)** — Bypass-token integration test
  - [ ] Bypass header attached → no rate-limit applied across 100 rapid requests
- [ ] **T18 (AC: 10)** — Response-size equivalence test (byte-length delta ≤ 64)
- [ ] **T19 (AC: 9)** — Middleware-order test
- [ ] **T20 (AC: 5)** — Telemetry emission test — assert `rate_limit.rejected` event captured
- [ ] **T21 (AC: 7)** — Vitest component test — 429 response renders inline error + disables button for countdown

## Dev Notes

### Critical guardrails

1. **Partition by token hash for view rate limit, not by IP.** Multi-debtor IP sharing (office Wi-Fi) would otherwise lock out legitimate traffic from one greedy IP. IP-axis is separate (AC3) and tighter (120/min total).
2. **Daily IP salt.** Hashing IP without a salt is reversible (rainbow tables). Daily rotation prevents long-term re-identification of debtors from their IPs in audit logs.
3. **`UseRateLimiter` AFTER auth.** Middleware order matters — a pre-auth rate-limiter can't partition on user principal.
4. **Response size equivalence for 429 vs 404.** Without this, the timing/size oracle leaks whether a token is "valid but rate-limited" vs "invalid." Body padding to 64-byte alignment is sufficient for the practical threat model.
5. **Bypass token is a one-way door.** Once leaked, rotate immediately — monthly default rotation prevents accidental leak persistence.
6. **SWA free tier can't do per-route rate limiting.** App-level controls carry the full burden in MVP. The AFD fallback doc is pre-committed so the upgrade path is mechanical.

### Source tree

```
backend/
├── src/Faktuboh.Api/
│   ├── Program.cs                                              # MODIFY (rate-limiter wiring)
│   └── Infrastructure/RateLimiting/
│       ├── DailyIpSaltService.cs                               # NEW
│       ├── RateLimitBypassMiddleware.cs                        # NEW
│       └── RateLimitRejectionWriter.cs                         # NEW (produces problem+json)
├── src/Faktuboh.Application/Errors/
│   └── ErrorCatalog.cs                                         # MODIFY (+1 code)
└── tests/Faktuboh.Api.Tests/Infrastructure/RateLimiting/
    ├── LinkGenerationRateLimitTests.cs                         # NEW
    ├── ViewPerTokenRateLimitTests.cs                           # NEW
    ├── ViewPerIpRateLimitTests.cs                              # NEW
    ├── BypassTokenTests.cs                                     # NEW
    ├── ResponseSizeEquivalenceTests.cs                         # NEW
    └── MiddlewareOrderTests.cs                                 # NEW

docs/ops/
└── afd-fallback-rate-limits.md                                 # NEW

apps/public-statement/
└── staticwebapp.config.json                                    # NEW (SWA routing config)

frontend/
├── apps/owner-workspace/src/app/features/contacts/detail/
│   └── contact-detail.component.ts                             # MODIFY (429 handling)
└── libs/core/i18n/transloco/{en,ar}.json                       # MODIFY
```

### Architecture compliance

- **§4.3 D4.7** (app-level rate limiting, WAF deferred)
- **§4.6 D4.19** (edge WAF rule documented for AFD fallback)
- **§7.4.1** (SWA free tier cascade — app-level does the heavy lifting)
- **§2.9 #2** (constant-time response — 429 and 404 body equivalence)
- **§4.4 D4.9** (RFC 9457 with code taxonomy)
- **FR42, NFR-S5** (two-layer rate limiting)

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| ASP.NET Core | `Microsoft.AspNetCore.RateLimiting` | Built-in on .NET 10 |
| Partitioned rate limiters | `System.Threading.RateLimiting` | First-party |
| Tests | xUnit v3 + Testcontainers | Use `WebApplicationFactory<Program>` |

### Testing requirements

- **Unit:** Rejection writer produces correct problem+json shape per policy.
- **Integration:** Full middleware pipeline with 429 + headers.
- **Middleware order:** Explicit test asserts auth runs before rate limiter.
- **Equivalence:** Byte-length delta ≤ 64 between 404 and 429 bodies.
- **Load:** Story 4.13 (k6 harness) validates rate limits under sustained load.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.4 — RFC 9457
- Story 0.11 — App Insights
- Story 4.1 — constant-time response baseline
- Story 4.4 — link generation endpoint (client of `statement-link-gen` policy)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.9
- App-level rate limiting — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.3 D4.7
- Edge WAF rules — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.6 D4.19
- Constant-time invariant — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 #2
- FR42, NFR-S5, AR-017 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- UX-DR32 error recovery — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- SWA free tier limitation: per-route rate-limiting is not available; app-level controls carry full burden. AFD fallback is documented in `docs/ops/afd-fallback-rate-limits.md` for the revisit trigger.
- Bypass token is required for Story 4.13 k6 harness to hit 166 rps without being self-throttled.

### File List

_Populated during implementation._
