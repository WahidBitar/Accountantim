# Story 4.11: Epic 4 cost circuit-breaker (AR-050)

Status: ready-for-dev

## Story

As **Wahid**,
I want **a per-token day-view cap of 1000 views/token/day + daily €50 spend alert + hard kill-switch env flag + AFD/SWA 24h TTL on statement content**,
so that **one Twitter moment cannot bankrupt the free-tier runway** (AR-050, Victor's insistence).

**Covers requirements:** Epic 4 — AR-050 cost circuit-breaker; §7.4.1 €0 burn tolerance; Story 0.17 baseline extended per-token [Source: epics.md Epic 4 Story 4.11; architecture.md §7.4.1].

**Why this story now:** Story 4.4 opened the spigot. Without per-token cap + kill-switch, one viral statement can consume the SWA free tier bandwidth (100 GB/mo) in a weekend. Story 0.17 provided the baseline kill-switch; this story adds the per-token layer + CDN TTL discipline.

## Acceptance Criteria

### AC1 — Per-token day-view cap at 1000 views/24h rolling

**Given** a token has served views
**When** the 1001st view request arrives within a 24-hour rolling window
**Then** the backend returns a **static "high volume" HTML page** (not the statement content) directly from the CDN (no origin hit)
**And** the static page content is in both Arabic + English and reads:
- Arabic: `"هذا الكشف يستقبل زيارات كثيرة حاليًا، حاول لاحقًا"`
- English: `"This statement is receiving high volume — please try again later"`
**And** the cap is enforced at two layers:
1. **App-level counter** — Postgres table `token_daily_views { token_hash, day (date), view_count }` incremented per view; handler returns 429 + static page when `view_count > 1000`
2. **CDN cache hit count** — views served from CDN cache don't increment the DB counter (which is the point — cache isolates origin from bursts), but SWA/AFD cache-hit metrics feed the App Insights dashboard for visibility [Source: epics.md Story 4.11 AC1; architecture.md AR-050].

### AC2 — CDN cache-control 24h + cache-bust on owner edit

**Given** §4.6 D4.19 specifies cache strategy alignment with D4.6
**When** a statement is issued or updated
**Then** response `Cache-Control: private, max-age=86400` (24h) on `GET /v1/statements/t/{token}` — already added by Story 4.4 T8
**And** when the owner edits the contact or a debt (`PATCH /v1/contacts/{id}` or `PATCH /v1/debts/{id}`), the backend:
1. Looks up all non-revoked active statements for that contact
2. Issues a cache-purge request to SWA (or AFD in fallback) for each active token URL
**And** a Wolverine handler `PurgeStatementCacheOnContactEdit` consumes `ContactUpdated` and `DebtUpdated` integration events
**And** on SWA free tier where programmatic cache-bust is limited, the workaround is to **rotate the token** — revoke old token + issue new — and update the owner-side link list (Epic 7 feature); for Phase A (no edits common), document in `docs/ops/statement-cache-bust.md` that edits during active share periods trigger token rotation [Source: architecture.md §4.6 D4.19; delegated decisions — SWA primary].

### AC3 — Daily €50 spend alert routes to Wahid

**Given** Story 0.17 provisioned the €50 budget alert baseline
**When** App Insights + Azure Cost Management project monthly spend
**Then** at 50% of €50 (€25) → email to `wahid.wb@gmail.com` (already wired in Story 0.17)
**And** at 90% of €50 (€45) → email
**And** at 100% (€50) → email + Slack webhook fires (if Slack URL configured) + `FAKTUBOH_EMERGENCY_SHUTDOWN` is NOT auto-flipped (manual decision per AR-050 — alert, do not auto-shutdown)
**And** Epic 4 extends the alert with a specific query: `requests | where url contains "/statements/t/" | summarize count() by bin(timestamp, 1h)` — if count > 100,000 in any hour, immediate critical email (Phase A is ≤ 100 users; 100k/h is definitely a viral event) [Source: architecture.md AR-050; Story 0.17].

### AC4 — Kill-switch `FAKTUBOH_EMERGENCY_SHUTDOWN=true` disables `/statement.*/*` in 60s

**Given** Story 0.17 wired the middleware
**When** the ACA env var is set via `az containerapp update --set-env-vars FAKTUBOH_EMERGENCY_SHUTDOWN=true`
**Then** within 60s (ACA restart SLA), all requests to `/v1/statements/*` return HTTP 503 with a static "temporarily unavailable" HTML page
**And** owner-workspace routes (`/v1/contacts/*`, `/v1/debts/*`) continue functioning (per Story 0.17 AC3)
**And** `/health` returns 200 (operational health separate from user-facing)
**And** Epic 4 asserts via integration test that after flag flip, the view endpoint returns the generic 503 HTML within 60s — the response **does NOT** distinguish "rate-limited" from "emergency shutdown" from "unknown token" (constant-time invariant) [Source: Story 0.17; epics.md Story 4.11].

### AC5 — Kill-switch response does not reveal which statements existed

**Given** the 503 static page must be generic
**When** `FAKTUBOH_EMERGENCY_SHUTDOWN=true` is active
**Then** the 503 response body is identical for ANY `/v1/statements/t/{token}` request regardless of:
- Whether the token is valid
- Whether the token is revoked
- Whether the token is expired
- Whether the token was ever issued
**And** no database lookup happens during shutdown (cost-saving — no origin work at all)
**And** the shutdown middleware runs BEFORE the rate limiter, BEFORE auth, BEFORE the route handler [Source: Story 0.17 AC2; epics.md Story 4.11].

### AC6 — Static "high volume" HTML is served from origin `wwwroot`

**Given** cost circuit-breaker serves static HTML for 1001+ views
**When** a token's daily counter exceeds 1000
**Then** the handler short-circuits and returns `backend/src/Faktuboh.Api/wwwroot/statement-high-volume.html` with status 429
**And** the static HTML file is < 5 KB total (bilingual message + minimal styling inline)
**And** response carries `Cache-Control: public, max-age=3600` so CDN can absorb the burst pattern
**And** the per-token counter is NOT further incremented once it exceeds 1000 (preserves counter integrity for day-rollover) [Source: epics.md Story 4.11 AC1].

### AC7 — `token_daily_views` table migration

**Given** per-token counting requires persistence
**When** migration `YYYYMMDDHHMMSS_AddTokenDailyViewsTable.cs` lands
**Then** table `token_daily_views` has columns:
- `token_hash bytea NOT NULL`
- `day date NOT NULL`
- `view_count integer NOT NULL DEFAULT 0`
- `last_view_at timestamptz NOT NULL`
- PRIMARY KEY `(token_hash, day)`
**And** increment path uses `INSERT ... ON CONFLICT DO UPDATE SET view_count = view_count + 1` (atomic upsert; no race)
**And** daily cleanup: Wolverine scheduled job sweeps rows > 14 days old (keeps 2 weeks for trend analysis)
**And** the table is NOT bitemporal (operational counter, not audit surface) — documented exception to ADR-022 [Source: epics.md Story 4.11; architecture.md §4.3 D4.6].

### AC8 — Dashboard: per-token view counts + top-10 list

**Given** cost circuit-breaker must be observable
**When** the "AFD + WAF" App Insights dashboard (or SWA equivalent) is extended
**Then** new panels render:
1. Top-10 tokens by views (last 24h) — shows `contact_id_hash` not raw contact ID (PII-safe)
2. Cache-hit ratio for `/t/*` routes (silent-decay SLO from §4.9)
3. Per-hour view rate across all tokens
4. Rate-limit rejections trend (from Story 4.9)
**And** the Top-10 panel is the leading indicator of a viral moment [Source: architecture.md §4.6 D4.20; §4.9].

### AC9 — Operational runbook for viral-spike response

**Given** manual decision-making is required when the €50 alert fires
**When** `docs/ops/viral-spike-response.md` is created
**Then** the runbook documents:
1. Decision tree: is the spike legitimate (WhatsApp share chain) or adversarial?
2. Evidence check: does the top-10 token list show one dominant token? (likely legit)
3. Options: (a) let the 24h TTL absorb it (b) flip kill-switch (c) rotate the hot token + re-share
4. Cost-cap trigger: at €45 projected monthly spend, prefer flipping kill-switch to preserve runway
5. Post-incident: update `docs/architecture/ssr-fallback.md` with lessons learned [Source: epics.md Story 4.11; architecture.md §7.5.8].

### AC10 — Integration test: full cost-cap flow

**Given** the cost-cap must work end-to-end
**When** an integration test simulates the flow
**Then** the test:
1. Issues a token via `/v1/contacts/{id}/statements`
2. Calls `/v1/statements/t/{token}` 1000 times (with bypass token from Story 4.9)
3. Asserts call 1001 returns 429 with the static "high volume" HTML body
4. Flips `FAKTUBOH_EMERGENCY_SHUTDOWN=true` in test server config
5. Asserts subsequent calls return 503 with the emergency HTML
6. Resets flag; asserts 429 again (not back to 200 — the 1000-cap is still active)
7. Mocks `ISystemClock` advance by 24h; asserts counter resets [Source: epics.md Story 4.11 AC; Story 0.17 AC4].

## Tasks / Subtasks

### Backend — Database

- [ ] **T1 (AC: 7)** — Create migration `YYYYMMDDHHMMSS_AddTokenDailyViewsTable.cs`
  - [ ] Table + PK + ON CONFLICT upsert support verified
- [ ] **T2 (AC: 7)** — Repository `ITokenDailyViewRepository`
  - [ ] `IncrementAsync(byte[] tokenHash, DateOnly day)` returns updated `view_count`
  - [ ] Implementation uses raw SQL `INSERT ... ON CONFLICT DO UPDATE ... RETURNING view_count`

### Backend — Middleware

- [ ] **T3 (AC: 1, 6)** — Create `DailyViewCapMiddleware` or endpoint filter
  - [ ] Runs AFTER rate limiter, BEFORE the handler
  - [ ] Increments `token_daily_views`
  - [ ] If count > 1000, writes `wwwroot/statement-high-volume.html` with 429
  - [ ] Does NOT further increment once over 1000
- [ ] **T4 (AC: 6)** — Create `backend/src/Faktuboh.Api/wwwroot/statement-high-volume.html`
  - [ ] Bilingual AR + EN message
  - [ ] Inline styling; < 5 KB
  - [ ] `<html lang dir>` set based on `Accept-Language`; fallback to LTR English

### Backend — Cache invalidation

- [ ] **T5 (AC: 2)** — Create Wolverine handler `PurgeStatementCacheOnContactEdit`
  - [ ] Consumes `ContactUpdated` + `DebtUpdated` events
  - [ ] Looks up active statements for affected contact
  - [ ] For SWA: logs the purge intent (no programmatic purge on free tier); emits telemetry `cache_purge_requested`
  - [ ] For AFD fallback: calls `PUT /subscriptions/.../purgeContent` with token URLs
- [ ] **T6 (AC: 2)** — Document workaround in `docs/ops/statement-cache-bust.md`

### Backend — Scheduled cleanup

- [ ] **T7 (AC: 7)** — Wolverine scheduled job `CleanupOldTokenViewsJob`
  - [ ] Runs daily at 02:00 UTC
  - [ ] Deletes rows WHERE `day < CURRENT_DATE - INTERVAL '14 days'`

### Backend — Alerts

- [ ] **T8 (AC: 3)** — Extend App Insights alert rule
  - [ ] New alert: Kusto query `requests | where url contains "/statements/t/" | summarize count() by bin(timestamp, 1h) | where count_ > 100000`
  - [ ] Severity: critical
  - [ ] Action group: email + Slack webhook if `SLACK_WEBHOOK_URL` configured
- [ ] **T9 (AC: 3)** — Update Bicep/Terraform for the alert rule

### Backend — Dashboards

- [ ] **T10 (AC: 8)** — Extend App Insights workbook
  - [ ] Top-10 tokens by view count (24h rolling), showing `contact_id_hash`
  - [ ] Cache-hit ratio for `/t/*`
  - [ ] Per-hour view rate across tokens
  - [ ] Rate-limit rejections trend (from Story 4.9)

### Runbooks

- [ ] **T11 (AC: 9)** — Create `docs/ops/viral-spike-response.md`
  - [ ] Decision tree
  - [ ] Kill-switch command + rollback
  - [ ] Post-incident update checklist

### Tests

- [ ] **T12 (AC: 1, 6)** — Integration test `DailyViewCapTests`
  - [ ] 1000 views succeed; 1001 returns 429 + static HTML
  - [ ] Counter resets after mocked 24h advance
- [ ] **T13 (AC: 2)** — `CachePurgeOnEditTests`
  - [ ] Issue statement → edit contact → assert `cache_purge_requested` telemetry fires
- [ ] **T14 (AC: 4, 5)** — Kill-switch constant-time test
  - [ ] Flip flag; assert 503 for valid + invalid + revoked tokens
  - [ ] Assert body byte-equal across all cases
- [ ] **T15 (AC: 10)** — Full flow integration test per AC10

## Dev Notes

### Critical guardrails

1. **1000 views/token/24h is generous.** A typical debtor views a statement 1-3 times. 1000 is abuse OR a viral event. The cap is not a UX floor; it's a cost floor.
2. **Kill-switch generic response.** Story 0.17 already enforced this; Epic 4 extends with the constant-time invariant: no lookup during shutdown, body bytes identical across all paths.
3. **SWA free tier + cache-bust = token rotation, not programmatic purge.** Phase A assumption: edits during active shares are rare. When they do happen, rotating the token is the price of free-tier hosting.
4. **Per-token counter is operational, not audit.** `token_daily_views` is deliberately NOT bitemporal. Rolled 14 days and gone. Documented exception to ADR-022.
5. **Static HTML must be < 5 KB.** High-volume page is served a LOT during a viral spike. If it's 50 KB, the spike eats bandwidth too.
6. **Alert at €45, don't auto-shutdown.** Human decision per AR-050. Auto-shutdown risks closing on a legitimate viral moment that would convert 40% of visitors to signups (Phase A goal).

### Source tree

```
backend/
├── src/Faktuboh.Api/
│   ├── Infrastructure/CostCircuitBreaker/
│   │   └── DailyViewCapMiddleware.cs                        # NEW
│   ├── Slices/Statements/Projections/
│   │   └── PurgeStatementCacheOnContactEdit.cs              # NEW
│   ├── Jobs/
│   │   └── CleanupOldTokenViewsJob.cs                       # NEW
│   └── wwwroot/
│       └── statement-high-volume.html                       # NEW (< 5KB bilingual)
├── src/Faktuboh.Domain/TokenDailyViews/
│   └── ITokenDailyViewRepository.cs                         # NEW
├── src/Faktuboh.Infrastructure/
│   ├── Persistence/
│   │   └── Migrations/
│   │       └── YYYYMMDDHHMMSS_AddTokenDailyViewsTable.cs    # NEW
│   └── Repositories/TokenDailyViewRepository.cs             # NEW
└── tests/Faktuboh.Api.Tests/Infrastructure/CostCircuitBreaker/
    ├── DailyViewCapTests.cs                                 # NEW
    ├── CachePurgeOnEditTests.cs                             # NEW
    ├── KillSwitchConstantTimeTests.cs                       # NEW
    └── FullCostCapFlowTests.cs                              # NEW

docs/ops/
├── statement-cache-bust.md                                  # NEW
└── viral-spike-response.md                                  # NEW

infra/
└── bicep/alerts/viral-spike-alert.bicep                     # NEW
```

### Architecture compliance

- **AR-050** (cost circuit-breaker) — this story is the first full implementation.
- **§4.3 D4.6** (per-token cache key) — 24h TTL per-token.
- **§4.6 D4.19** (edge cache) — for AFD fallback, cache-bust command wired.
- **§4.9** (silent-decay SLOs — cache-hit ratio) — dashboard surfaces.
- **§7.4.1** (free-tier ceiling) — cap designed against 100 GB/mo SWA bandwidth.
- **Story 0.17 baseline** — extended per-token layer.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| EF Core | 10 | `ON CONFLICT` raw SQL for atomic upsert |
| Wolverine | latest | Scheduled job + event handlers |
| Alert | Azure Cost Management Budgets | Extended in Story 0.17; Kusto query added here |

### Testing requirements

- **Unit:** Static HTML content validation; counter increment logic.
- **Integration:** Full cost-cap flow with mocked clock + bypass token.
- **Operational:** Manual test — flip kill-switch in staging, verify 60s SLA.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.10 — Azure infrastructure + €50 budget alert
- Story 0.17 — Emergency shutdown middleware baseline
- Story 4.1 — capability-token scheme
- Story 4.4 — link generation + `Cache-Control` header filter
- Story 4.9 — rate limiting + bypass token

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.11
- AR-050 cost circuit-breaker — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md)
- Cache strategy — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.6 D4.19
- Free-tier burn tolerance — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §7.4.1
- Story 0.17 baseline — [_bmad-output/implementation-artifacts/0-17-cost-circuit-breaker-baseline.md](./0-17-cost-circuit-breaker-baseline.md)
- Story 4.9 rate limits — [_bmad-output/implementation-artifacts/4-9-token-rate-limiting-20-links-hour-user-afd-edge-rule.md](./4-9-token-rate-limiting-20-links-hour-user-afd-edge-rule.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- SWA programmatic cache-bust is not available on free tier — workaround documented via token rotation.
- `token_daily_views` is deliberately NOT bitemporal (operational, not audit) — documented exception to ADR-022.

### File List

_Populated during implementation._
