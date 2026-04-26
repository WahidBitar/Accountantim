# Story 8.1: Admin dashboard with platform metrics

Status: ready-for-dev

## Story

As **Wahid (operations)**,
I want **an admin dashboard showing active users + new registrations + statement links generated + confirmation rates + viral coefficient**,
so that **I can monitor platform health and Phase-A success metrics** (FR43) [Source: epics.md Epic 8 Story 8.1; prd.md FR43].

**Covers requirements:** Epic 8 — FR43 (admin dashboard), NFR-S10 (admin elevated auth + audit logging), AR-043 (minimum-k privacy policy), reuse of Story 0.11 KQL dashboard primitives [Source: epics.md Story 8.1; architecture.md §4.6 D4.20].

**Why this story first in Epic 8:** Admin surfaces unblock Wahid's operational visibility. The dashboard is read-only (no destructive actions), the lowest-risk admin story to land first. It validates the admin-role authorization + elevated re-auth + audit logging patterns that all other Epic 8 stories inherit.

## Acceptance Criteria

### AC1 — Admin role + elevated re-authentication

**Given** the Auth0 tenant is configured with a custom claim `roles: ["admin"]` set via Auth0 Action for Wahid's account
**When** the user navigates to `/admin/dashboard`
**Then** the route guard `AdminRoleGuard` checks `user.roles.includes("admin")` — non-admin → 403 with RFC 9457 `faktuboh.admin.role_required`
**And** within the past 30 minutes the admin must have completed an elevated re-authentication step (Auth0 step-up via `acr_values=mfa` claim) — if not, route guard redirects to Auth0's step-up flow per NFR-S10
**And** elevated-auth state is persisted as a session-scoped flag with `expiresAt` timestamp; expiry triggers re-prompt
**And** the `acr_values=mfa` requirement is configured via Auth0 Authorization API; backend verifies the JWT contains `acr=mfa` claim with `auth_time` within 30 minutes
**And** an integration test simulates a non-elevated session attempting `/admin/dashboard` → redirected to step-up; an elevated session → 200 with dashboard data [Source: prd.md NFR-S10; adr-021 Auth0 step-up].

### AC2 — Active-user count (7-day rolling) via App Insights KQL

**Given** Story 0.11 wired App Insights with `traceId` correlation + four base dashboards
**When** the dashboard renders
**Then** the active-user count is sourced from a KQL query against App Insights:
```kql
customEvents
| where timestamp > ago(7d)
| where name in ("contact_created", "debt_created", "statement_generated", "statement_confirmed")
| summarize dcount(tostring(customDimensions.OpaqueUserId))
```
**And** the query returns a single integer (active users in last 7 days)
**And** the query executes server-side via `IApplicationInsightsQueryClient` (Azure Monitor Query SDK)
**And** results are cached for 5 minutes (server-side memory cache) — the dashboard does not need real-time refresh; reduces App Insights query cost [Source: epics.md Story 8.1 AC; Story 0.11].

### AC3 — New registrations (rolling-week) via App Insights KQL

**Given** Story 1.2 emits `user_registered` telemetry on signup
**When** the dashboard renders
**Then** the count is sourced from:
```kql
customEvents
| where timestamp > ago(7d)
| where name == "user_registered"
| count
```
**And** the dashboard tile shows the count + a sparkline of daily registrations over the last 30 days (separate KQL query)
**And** if `user_registered` events have not been emitted yet (Story 1.2 not landed), the tile shows "n/a" with footnote "Awaiting Story 1.2" — graceful degradation [Source: epics.md Story 8.1 AC; Story 1.2].

### AC4 — Statement-links-generated (rolling-week)

**Given** Stories 4.4 + 4.10 emit `capability_token_issued` telemetry
**When** the dashboard renders
**Then** the count derives from:
```kql
customEvents
| where timestamp > ago(7d)
| where name == "capability_token_issued"
| count
```
**And** the tile additionally breaks down by `Scope` dimension (`statement.read` only at MVP) [Source: Story 4.10].

### AC5 — Confirmation rate

**Given** Story 4.6 emits `statement_confirmed`
**When** the dashboard renders
**Then** confirmation rate = `count(statement_confirmed) / count(capability_token_issued)` over the last 30 days:
```kql
let issued = customEvents | where timestamp > ago(30d) | where name == "capability_token_issued" | count;
let confirmed = customEvents | where timestamp > ago(30d) | where name == "statement_confirmed" | count;
print confirmation_rate = todouble(toscalar(confirmed)) / todouble(toscalar(issued))
```
**And** displayed as a percentage to 1 decimal place
**And** if denominator is 0, display "n/a" [Source: Story 4.6, Story 4.10].

### AC6 — Viral coefficient (signups-from-shares / views)

**Given** Story 4.8 emits `signup_from_share` telemetry on signups originating from a shared statement page
**When** the dashboard renders
**Then** viral coefficient = `count(signup_from_share) / count(capability_token_viewed)` over the last 30 days
**And** the metric is the Phase-A success-trigger leading indicator (PRD target: ≥ 0.3)
**And** displayed as a decimal to 2 places (e.g., "0.27")
**And** color-coded: red < 0.20, yellow 0.20–0.30, green ≥ 0.30 [Source: prd.md Phase-A activation gate; Story 4.8].

### AC7 — Phase-A activation rate (40% target)

**Given** PRD defines Phase-A activation as "user creates contact + records ≥ 1 debt within first 7 days of signup"
**When** the dashboard renders
**Then** activation rate KQL query joins `user_registered` with `debt_created` for the same `OpaqueUserId` within 7 days:
```kql
let signups = customEvents
    | where timestamp > ago(60d) and timestamp < ago(7d)
    | where name == "user_registered"
    | extend uid = tostring(customDimensions.OpaqueUserId), signup_at = timestamp;
let firstdebts = customEvents
    | where timestamp > ago(60d)
    | where name == "debt_created"
    | extend uid = tostring(customDimensions.OpaqueUserId)
    | summarize first_debt_at = min(timestamp) by uid;
signups
    | join kind=leftouter (firstdebts) on uid
    | extend activated = iif(isnotnull(first_debt_at) and first_debt_at < signup_at + 7d, 1, 0)
    | summarize activation_rate = todouble(sum(activated)) / count()
```
**And** displayed with the 40% target band as background reference [Source: prd.md Phase-A activation 40%].

### AC8 — Minimum-k policy enforced per AR-043

**Given** AR-043 requires aggregate rows with < 5 underlying natural persons to be suppressed
**When** any chart segment / breakdown row has fewer than 5 distinct `OpaqueUserId` values
**Then** the row is suppressed and replaced with "<5 users (suppressed for privacy)"
**And** sparklines + scalar tiles are not subject to k-suppression (they are population-level, not segment-level)
**And** the breakdown tables (registrations by day, confirmations by contact-locale) ARE subject — each row is checked
**And** an integration test seeds < 5 users in a category, asserts row is suppressed in the response [Source: architecture.md AR-043].

### AC9 — Admin operations logged in audit trail

**Given** NFR-S10 requires admin operations to be audit-logged
**When** the admin loads the dashboard or any admin endpoint is called
**Then** an entry is written to `audit_log` table with:
- `actor_subject_id = admin's subject_id`
- `action = 'admin.dashboard.viewed'` (or specific action name)
- `processing_activity = 'AdminInvestigation'` (new activity)
- `target_resource = NULL` for read-only views
- `payload = JSONB { route: '/admin/dashboard' }`
- `created_at = now()`
**And** the `audit_log` table from Epic 3 Story 3.8 is used (no new table)
**And** the new `ProcessingActivity.AdminInvestigation` enum value is added to `Faktuboh.Domain/Primitives/ProcessingActivity.cs`
**And** an integration test loads the dashboard + asserts audit row created [Source: prd.md NFR-S10; Story 3.8].

### AC10 — Admin dashboard frontend at `/admin/dashboard`

**Given** the admin role + elevated auth check passes
**When** the page renders
**Then** the layout uses PrimeNG `<p-card>` tiles in a responsive grid:
- Active users (7d) — large number + delta vs prior 7d
- New registrations (7d) — number + 30d sparkline
- Links generated (7d) — number
- Confirmation rate (30d) — percentage + delta
- Viral coefficient (30d) — number + color band
- Phase-A activation rate — percentage + 40% target band
**And** the page is bilingual (AR + EN)
**And** Axe-core: zero WCAG 2.1 AA violations
**And** mobile responsive (admin may check on phone) — tiles stack vertically < 768px
**And** all data fetched via `GET /v1/admin/dashboard/metrics` returning a single composite payload (one round-trip) [Source: epics.md Story 8.1 AC; ux-design-specification.md responsive].

### AC11 — Cost-aware: dashboard is rate-limited to admin only

**Given** Phase-A KQL queries cost App Insights ingestion + query units
**When** the admin loads `/admin/dashboard`
**Then** the page-level rate limit is 10 loads per minute per admin subject (prevents accidental refresh-storm)
**And** the 5-minute server-side cache (AC2) further reduces App Insights query cost
**And** non-admin users hit the 403 response BEFORE any KQL query runs (auth check is first) [Source: AR-050 cost ceiling].

## Tasks / Subtasks

### Backend — Admin slice scaffold

- [ ] **T1 (AC: 1, 9)** — Create `Slices/Admin/` slice scaffold
  - [ ] `Admin.Endpoints.cs` with `MapGroup("/v1/admin").WithTags("Admin").RequireAuthorization()`
  - [ ] Endpoint filter `RequireAdminRoleAndElevatedAuth` checks `user.roles.includes("admin")` + `acr=mfa` + `auth_time` within 30 minutes
  - [ ] On failure, returns RFC 9457 `faktuboh.admin.role_required` (403) or `faktuboh.admin.elevated_auth_required` (401 with redirect hint)

### Backend — Auth0 step-up integration

- [ ] **T2 (AC: 1)** — Add JWT validation extension that reads `acr` + `auth_time` claims
  - [ ] `JwtBearerOptions.TokenValidationParameters` already validates standard claims; add custom validation in `OnTokenValidated` event
  - [ ] If route is under `/v1/admin/`, require `acr == "mfa"` + `auth_time` within 30 min
- [ ] **T3 (AC: 1)** — Configure Auth0 Action that adds `roles` custom claim from Auth0 user metadata
  - [ ] Action JS in `infra/auth0/actions/add-roles-claim.js` (committed for IaC parity)
  - [ ] Documents that Wahid's user has `app_metadata.roles = ["admin"]`

### Backend — Dashboard endpoint

- [ ] **T4 (AC: 2-7, 10, 11)** — Create `Slices/Admin/Dashboard/`
  - [ ] `DashboardMetricsResponse.cs` — composite payload with all 6 metrics
  - [ ] `DashboardMetricsHandler.cs` — orchestrates the 6 KQL queries (parallel execution via `Task.WhenAll`)
  - [ ] Endpoint `MapGet("/v1/admin/dashboard/metrics", ...)` with rate-limit policy `AdminDashboardPolicy` (10/min/admin)
  - [ ] Server-side memory cache via `IMemoryCache` with 5-min TTL keyed by metric name

### Backend — App Insights query client

- [ ] **T5 (AC: 2-7)** — Create `IAppInsightsQueryClient` + `AppInsightsQueryClient` in `Faktuboh.Infrastructure/Telemetry/`
  - [ ] Wraps `Azure.Monitor.Query.LogsQueryClient`
  - [ ] Method `Task<long> ExecuteScalarAsync(string kql, TimeSpan timeRange)`
  - [ ] Method `Task<List<KqlRow>> ExecuteAsync(string kql, TimeSpan timeRange)`
  - [ ] Authenticates via managed identity (no secrets)

### Backend — Minimum-k policy

- [ ] **T6 (AC: 8)** — Create `MinimumKPolicyApplicator` in `Faktuboh.Application/Admin/`
  - [ ] Method `ApplySuppression<T>(IEnumerable<T> rows, Func<T, int> kSelector, int minimum = 5)`
  - [ ] Replaces rows with k < 5 with a sentinel "suppressed" row
- [ ] **T7 (AC: 8)** — Apply in `DashboardMetricsHandler` to all breakdown queries

### Backend — Audit logging

- [ ] **T8 (AC: 9)** — Add `ProcessingActivity.AdminInvestigation` enum value
  - [ ] Update `Faktuboh.Domain/Primitives/ProcessingActivity.cs`
- [ ] **T9 (AC: 9)** — Wire audit-log write in admin endpoint filter
  - [ ] After successful auth check, write `audit_log` row with action name + payload
  - [ ] Reuses `IAuditLogService` from Story 3.8

### Backend — Error catalog

- [ ] **T10 (AC: 1)** — Register `faktuboh.admin.role_required` (403), `faktuboh.admin.elevated_auth_required` (401), `faktuboh.admin.rate_limit_exceeded` (429) in `ErrorCatalog`

### Frontend — Admin route + guard

- [ ] **T11 (AC: 1, 10)** — Create `apps/owner-workspace/src/app/features/admin/` feature folder
  - [ ] `admin.routes.ts` — lazy route `/admin/*`
  - [ ] `admin-role.guard.ts` — checks `authService.user.roles.includes('admin')` + elevated-auth flag
  - [ ] `admin-shell.component.ts` — admin-specific layout wrapper
  - [ ] On guard fail (no role), redirects to `/`; on guard fail (no elevated auth), triggers Auth0 step-up flow

### Frontend — Dashboard page

- [ ] **T12 (AC: 10)** — Create `dashboard.component.ts` + `dashboard.component.html`
  - [ ] Standalone, OnPush, signals
  - [ ] Renders 6 PrimeNG `<p-card>` tiles in responsive grid (Tailwind `grid-cols-1 md:grid-cols-2 lg:grid-cols-3`)
  - [ ] Each tile receives data from rxResource subscribed to `getDashboardMetrics()`
  - [ ] Sparklines via PrimeNG `<p-chart type="line">` (zero-config, lightweight)
  - [ ] Bilingual labels via Transloco
- [ ] **T13 (AC: 6)** — Color-band utility for viral coefficient
  - [ ] `getViralBandColor(coefficient: number): 'red' | 'yellow' | 'green'`
  - [ ] Applied via Tailwind class binding

### Frontend — API client

- [ ] **T14 (AC: 10)** — Create `apps/owner-workspace/src/app/core/api/admin.client.ts`
  - [ ] `getDashboardMetrics(): rxResource<DashboardMetricsResponse>`
  - [ ] NSwag-generated

### Tests — Backend

- [ ] **T15 (AC: 1)** — Integration test for auth flows
  - [ ] Non-admin → 403 `faktuboh.admin.role_required`
  - [ ] Admin without elevated auth → 401 `faktuboh.admin.elevated_auth_required`
  - [ ] Admin with elevated auth (mock JWT with `acr=mfa` + recent `auth_time`) → 200
- [ ] **T16 (AC: 2-7)** — Integration test with mocked App Insights query client
  - [ ] Mock returns canned KQL results
  - [ ] Assert response payload shape
  - [ ] Assert 5-min cache: second call within 5 min returns cached value (mock asserts only called once)
- [ ] **T17 (AC: 8)** — Minimum-k policy test
  - [ ] Seed breakdown with rows of (k=3, k=4, k=5, k=6); assert first two suppressed, last two visible
- [ ] **T18 (AC: 9)** — Audit-log assertion
  - [ ] Load dashboard + assert `audit_log` row inserted with `action='admin.dashboard.viewed'`
- [ ] **T19 (AC: 11)** — Rate-limit test: 11th request in 1 min returns 429

### Tests — Frontend

- [ ] **T20 (AC: 10)** — Vitest component tests for dashboard tiles
  - [ ] Renders correctly with mocked rxResource data
  - [ ] Sparkline renders with correct data series
  - [ ] Color-band logic for viral coefficient
- [ ] **T21 (AC: 1)** — Vitest test for `admin-role.guard.ts`
  - [ ] Non-admin user → returns false + navigates to `/`
  - [ ] Admin user without elevated auth → returns false + triggers step-up
  - [ ] Admin user with elevated auth → returns true
- [ ] **T22 (AC: 10)** — Playwright E2E `apps/owner-workspace/e2e/admin/dashboard.spec.ts`
  - [ ] Mocked admin login + step-up; navigate to /admin/dashboard; assert tiles render
  - [ ] Axe-core: zero violations
  - [ ] Mobile viewport: tiles stack
  - [ ] RTL parity: dashboard renders in Arabic locale

## Dev Notes

### Critical guardrails (do not violate)

1. **Admin role check + elevated auth are non-negotiable per NFR-S10.** Skipping either is a CRITICAL security gap. Both validations happen BEFORE any KQL query runs.
2. **5-minute server-side cache.** App Insights queries cost real money. Without the cache, refresh-spam from a single admin tab burns query units. Cache key is per-metric, not per-request.
3. **Minimum-k = 5 per AR-043.** Without it, a small-population segment (e.g., one city with 2 users) leaks individual identifiability. The suppression message must be visually obvious so admin doesn't mistake it for a 0-count.
4. **Audit log every admin op.** Without it, NFR-S10 is unmet. The endpoint filter writes the row — no per-handler boilerplate.
5. **Read-only at MVP.** Per delegated decisions: this story has zero destructive actions. No `<p-confirmDialog>` needed. Stories 8.4 + 8.5 add destructive actions and use the pattern.
6. **`acr=mfa` + `auth_time` within 30 min.** Auth0's step-up returns these claims. Without checking BOTH, a user could authenticate-with-MFA once and stay elevated forever — defeats the 30-minute re-auth policy.
7. **KQL queries are pinned in source (not Azure Portal saved queries).** Pinning in source enables PR review + drift detection + reproducibility. Saved queries in the portal are easy to "tune" silently.
8. **Graceful degradation if upstream telemetry events haven't shipped.** A KQL query returning 0 should display as 0 with footnote, not crash the dashboard.
9. **Cost cap on dashboard load: 10/min/admin.** Without it, an open browser tab on auto-refresh could spike App Insights costs.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/Admin/
│   │       ├── Admin.Endpoints.cs                              # NEW
│   │       ├── RequireAdminRoleAndElevatedAuth.cs              # NEW (endpoint filter)
│   │       └── Dashboard/
│   │           ├── DashboardMetricsResponse.cs                 # NEW
│   │           └── DashboardMetricsHandler.cs                  # NEW
│   ├── Faktuboh.Application/
│   │   ├── Admin/
│   │   │   └── MinimumKPolicyApplicator.cs                     # NEW
│   │   └── Errors/ErrorCatalog.cs                              # MODIFY (+3 codes)
│   ├── Faktuboh.Infrastructure/
│   │   └── Telemetry/
│   │       ├── IAppInsightsQueryClient.cs                      # NEW
│   │       └── AppInsightsQueryClient.cs                       # NEW
│   └── Faktuboh.Domain/
│       └── Primitives/ProcessingActivity.cs                    # MODIFY (+AdminInvestigation)
├── tests/Faktuboh.Api.Tests/Slices/Admin/
│   └── DashboardTests.cs                                       # NEW (Integration)
└── infra/auth0/actions/
    └── add-roles-claim.js                                      # NEW

frontend/
└── apps/owner-workspace/src/app/
    ├── features/admin/
    │   ├── admin.routes.ts                                     # NEW
    │   ├── admin-role.guard.ts                                 # NEW
    │   ├── admin-shell.component.ts                            # NEW
    │   └── dashboard/
    │       ├── dashboard.component.ts                          # NEW
    │       ├── dashboard.component.html                        # NEW
    │       ├── dashboard.component.scss                        # NEW
    │       └── dashboard.component.spec.ts                     # NEW
    └── core/api/
        └── admin.client.ts                                     # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3, FluentAssertions, NSubstitute (mock `IAppInsightsQueryClient`) |
| Integration (BE) | `WebApplicationFactory<Program>` + Testcontainers Postgres + mocked Azure Monitor Query SDK |
| Unit (FE) | Vitest + Angular Testing Library |
| E2E | Playwright; mocked admin login + step-up |

### Project Structure Notes

This story scaffolds a new `Slices/Admin/` slice that all subsequent Epic 8 admin stories (8.2–8.6) extend. The slice is per-architecture-discipline isolated — it imports no other slice's domain types. The `audit_log` table from Story 3.8 is the only cross-cutting infrastructure concern; reused via `IAuditLogService` (not by importing aggregates).

### Architecture compliance

- [epics.md Epic 8 Story 8.1](../planning-artifacts/epics.md)
- [architecture.md §3.5, §4.6 D4.20](../planning-artifacts/architecture.md) — App Insights + audit log
- [architecture.md AR-043](../planning-artifacts/architecture.md) — minimum-k policy
- [adr-021 Auth0](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md) — step-up authentication
- [Story 0.11](./0-11-app-insights-otel-traceid-correlation.md) — App Insights base dashboards
- [Story 0.14](./0-14-phase-a-telemetry-primitive.md) — telemetry events
- [Story 3.8](./3-8-user-visible-audit-trail-surface.md) — audit_log table
- [prd.md FR43, NFR-S10](../planning-artifacts/prd.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Azure.Monitor.Query | KQL queries against App Insights |
| Backend | Microsoft.Extensions.Caching.Memory | 5-min cache |
| Backend | Auth0 Authorization API | Step-up via `acr_values=mfa` |
| Frontend | PrimeNG (Card, Chart) | Dashboard tiles + sparklines |
| Frontend | @auth0/auth0-angular | Step-up flow |

### Testing requirements

- **Mocked `IAppInsightsQueryClient`.** Real KQL queries cost money + are slow + flaky in CI. Mock returns canned `KqlRow` lists.
- **Auth-flow integration tests** are mandatory — the security boundary is the highest-risk surface in this story.
- **Audit-log write assertion** — without it, NFR-S10 compliance silently regresses.
- **Cache-hit assertion** — second request within 5 min should NOT call mocked client.
- **Minimum-k boundary test (k=4 vs k=5)** — off-by-one bugs here leak privacy.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 0.3 — Auth0 tenant
- Story 0.11 — App Insights + base KQL dashboards
- Story 0.14 — telemetry primitive
- Story 1.2 — `user_registered` event source
- Story 3.8 — `audit_log` table + service
- Story 4.6 — `statement_confirmed` event source
- Story 4.8 — `signup_from_share` event source
- Story 4.10 — `capability_token_issued` event source

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.1
- [_bmad-output/planning-artifacts/architecture.md §4.6 D4.20, AR-043](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/prd.md FR43, NFR-S10, Phase-A activation](../planning-artifacts/prd.md)
- [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- [Story 0.11 App Insights](./0-11-app-insights-otel-traceid-correlation.md)
- [Story 0.14 telemetry primitive](./0-14-phase-a-telemetry-primitive.md)
- [Story 3.8 audit log](./3-8-user-visible-audit-trail-surface.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
