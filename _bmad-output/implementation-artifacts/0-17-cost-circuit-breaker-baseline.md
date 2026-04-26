# Story 0.17: Cost circuit-breaker baseline

Status: ready-for-dev

## Story

As **Wahid**,
I want **a daily spend alert at €50/month trigger (50% of free-tier ceiling) + a hard kill-switch env flag (`FAKTUBOH_EMERGENCY_SHUTDOWN`) that disables public-facing routes in 60 seconds**,
so that **a misconfiguration or viral overshoot can't silently burn the free-tier runway (AR-030)**.

**Covers requirements:** Epic 0 — AR-030. Cost circuit-breaker. Complements Story 0.10's €50/mo budget alert with an active kill switch.

**Why this story now:** Epic 0 close. Story 0.10 already wired the budget alert; this story adds the kill switch. Must exist before any public traffic lands (Epic 4 public-statement surface).

## Acceptance Criteria

### AC1 — Daily spend alert at €50/mo — confirmed from Story 0.10

**Given** Story 0.10 already provisioned a budget alert at 50% (€25) and 90% (€45) of €50/mo
**When** this story verifies the alert
**Then** Azure Cost Management → Budgets → `budget-faktuboh-prod` shows amount = €50 / monthly
**And** notifications route to `wahid.wb@gmail.com`
**And** a test-fire scenario is executed: manually lower budget to €1 for 1 day, verify alert email within 10 minutes, restore to €50
**And** the alert mechanism reuses Story 0.10's Bicep — no new resources [Source: Story 0.10 AC7]

### AC2 — `FAKTUBOH_EMERGENCY_SHUTDOWN` env flag

**Given** ACA env vars can be flipped via `az containerapp update --set-env-vars`
**When** the env flag is set to `true`
**Then** `backend/src/Faktuboh.Api/Infrastructure/EmergencyShutdown/EmergencyShutdownMiddleware.cs`:

```csharp
public sealed class EmergencyShutdownMiddleware(RequestDelegate next, IConfiguration config)
{
    private readonly bool _enabled = bool.TryParse(config["FAKTUBOH_EMERGENCY_SHUTDOWN"], out var v) && v;

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled && IsPublicStatementRoute(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync("wwwroot/emergency-shutdown.html");
            return;
        }
        await next(context);
    }

    private static bool IsPublicStatementRoute(PathString path)
        => path.StartsWithSegments("/v1/public");
}
```

**And** middleware registers via `app.UseMiddleware<EmergencyShutdownMiddleware>()` early in the pipeline (before routing)
**And** the static HTML file `backend/src/Faktuboh.Api/wwwroot/emergency-shutdown.html` renders a bilingual (AR + EN) "Temporarily unavailable — please try again later" message with Faktuboh branding

### AC3 — Kill-switch scoped to public surface only

**Given** owner-workspace routes (`/v1/contacts`, `/v1/debts`, etc.) must continue functioning during emergency
**When** `FAKTUBOH_EMERGENCY_SHUTDOWN=true` is active
**Then** requests to `/v1/contacts/*`, `/v1/debts/*`, `/v1/statements/*` (non-public) return normal responses
**And** requests to `/v1/public/statement/*` return the 503 emergency HTML
**And** the `/health` endpoint returns normal `200 OK` (operational health separate from user-facing availability)
**And** an integration test `EmergencyShutdownMiddlewareTests.cs` asserts:
- Flag off + public route → 200 OK
- Flag off + owner route → 200 OK
- Flag on + public route → 503 with emergency HTML
- Flag on + owner route → 200 OK (continues serving)
- Flag on + `/health` → 200 OK

### AC4 — 60-second activation

**Given** ACA restarts on env var change
**When** `az containerapp update --name aca-faktuboh-api-prod --set-env-vars FAKTUBOH_EMERGENCY_SHUTDOWN=true` runs
**Then** within 60 seconds of the command completing, public routes begin returning 503
**And** the 60-second SLA is verified via an operational test:
1. Issue baseline GET to `/v1/public/statement/<token>` → 200
2. Run `az containerapp update` command
3. Poll `/v1/public/statement/<token>` every 5s
4. First 503 response appears within 60s
**And** the test is documented in `docs/runbooks/emergency-shutdown.md` (operational test, not CI test)

### AC5 — Emergency-shutdown runbook

**Given** AR-030 requires a documented procedure
**When** `docs/runbooks/emergency-shutdown.md` exists
**Then** it documents:
1. **Trigger conditions** — budget alert fires at €45 (90%) OR abnormal traffic pattern OR manual decision
2. **Activation procedure**:
   ```bash
   az containerapp update \
     --name aca-faktuboh-api-prod \
     --resource-group rg-faktuboh-prod \
     --set-env-vars FAKTUBOH_EMERGENCY_SHUTDOWN=true
   ```
3. **Verification steps** — confirm 503 on public; confirm 200 on owner; confirm `/health` OK
4. **Root cause investigation** — while emergency is active, investigate cost / traffic / error spike
5. **Deactivation procedure** — same `az` command with `=false`; confirm 200 restores on public
6. **Post-incident review** — author a retrospective; update RTs if new trigger condition emerges

### AC6 — Emergency HTML is accessible + bilingual

**Given** the 503 response serves an HTML file
**When** `backend/src/Faktuboh.Api/wwwroot/emergency-shutdown.html` is inspected
**Then** the page:
- Has `<html lang="ar" dir="rtl">` (primary — Arabic is the product's primary locale)
- Includes English translation in a secondary block with `dir="ltr"`
- Renders a Faktuboh "write it down" branded heading
- Uses logical CSS properties (no `padding-left` — use `padding-inline-start`)
- Loads < 5KB total (self-contained, no external CSS/JS, no fonts)
- Meets WCAG 2.1 AA: semantic `<h1>`, sufficient contrast, no blocked zoom
- An automated accessibility check with axe-core passes on the file (run as part of Story 0.12 NFR-A9 harness or a standalone test) [Source: architecture.md §5.4 FE conventions + prd.md NFR-A9]

### AC7 — App Insights daily-spend metric check

**Given** D4.20 wires App Insights for cost visibility
**When** App Insights ingestion cost exceeds projected €25/mo (70% of €60 soft ceiling per §4 D4.20)
**Then** an App Insights alert fires (Story 0.10 wired the equivalent for overall Azure budget; this AC extends to App Insights specifically if ingestion sprawl outpaces overall Azure spend)
**And** the alert routes to `ag-faktuboh-email` action group (from Story 0.11)
**And** `infra/azure/modules/alerts.bicep` (Story 0.11) includes an `alert-appinsights-cost` rule querying `azure.applicationinsights/components` `billingUsage` metric [Source: architecture.md §4 D4.20 budget alert]

## Tasks / Subtasks

### T1 — EmergencyShutdownMiddleware (AC: 2, 3)

- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/EmergencyShutdown/EmergencyShutdownMiddleware.cs` per AC2 snippet
- [ ] Register in `Program.cs` via `app.UseMiddleware<EmergencyShutdownMiddleware>();` — placement: after `UseExceptionHandler` and before `UseAuthentication` so exceptions still return RFC 9457 even during shutdown

### T2 — Emergency HTML (AC: 2, 6)

- [ ] Create `backend/src/Faktuboh.Api/wwwroot/emergency-shutdown.html`:
  - Bilingual (AR-first RTL + EN-secondary LTR)
  - Faktuboh brand header
  - "Temporarily unavailable — please try again later" message
  - <5KB total size
  - Logical CSS properties
  - WCAG 2.1 AA compliant
- [ ] Register static-file serving via `app.UseStaticFiles()` in `Program.cs` (enable if not already)

### T3 — Integration tests (AC: 3)

- [ ] Create `backend/tests/Faktuboh.Api.Tests/Infrastructure/EmergencyShutdown/EmergencyShutdownMiddlewareTests.cs` with the 5 test cases from AC3
- [ ] Uses `WebApplicationFactory<Program>` + `ConfigureAppConfiguration` to set `FAKTUBOH_EMERGENCY_SHUTDOWN` per test

### T4 — Runbook (AC: 4, 5)

- [ ] Create `docs/runbooks/emergency-shutdown.md` with the 6 sections from AC5
- [ ] Document the 60-second SLA + verification procedure per AC4

### T5 — App Insights cost alert (AC: 7)

- [ ] Edit `infra/azure/modules/alerts.bicep` (Story 0.11) to add `alert-appinsights-cost` rule querying App Insights billing usage
- [ ] Route to `ag-faktuboh-email` action group

### T6 — Test-fire budget alert (AC: 1)

- [ ] In a scheduled maintenance window: manually reduce budget to €1 via Azure Portal
- [ ] Wait for spike (if daily spend exceeds €1)
- [ ] Verify email alert arrives within 10 min
- [ ] Restore budget to €50
- [ ] Document the successful test-fire in `docs/runbooks/emergency-shutdown.md` with date + screenshot

## Dev Notes

### Critical guardrails (do not violate)

1. **Kill switch is scoped to public surface, NOT owner surface.** The owner may have active work in progress — interrupting them for a cost event breaks trust. Only `/v1/public/*` routes are shut down. [Source: epics.md Story 0.17 AC]
2. **60-second activation is non-negotiable.** Slower activation = longer exposure to cost leak. ACA's restart cycle is the bottleneck; if it exceeds 60s, revisit (e.g., use an in-process config-refresh pattern). [Source: epics.md Story 0.17]
3. **Emergency HTML must work without any backend.** Static file served directly — do not call into auth, DB, or any service. If the shutdown is from cost overrun, backend services may already be stressed.
4. **Don't log every emergency-shutdown hit.** If public traffic is high, logging each 503 compounds the cost problem. Log once per minute via log-level throttling.
5. **Test-fire the budget alert at least once** per AC6 — an untested alert is not really wired.

### Source tree — files to create

```
backend/src/Faktuboh.Api/
├── Infrastructure/EmergencyShutdown/
│   └── EmergencyShutdownMiddleware.cs                   # NEW
├── wwwroot/
│   └── emergency-shutdown.html                          # NEW (bilingual, <5KB)
└── Program.cs                                           # EDIT (UseMiddleware + UseStaticFiles)

backend/tests/Faktuboh.Api.Tests/Infrastructure/EmergencyShutdown/
└── EmergencyShutdownMiddlewareTests.cs                  # NEW (5 scenarios)

infra/azure/modules/
└── alerts.bicep                                         # EDIT (add App Insights cost alert)

docs/runbooks/
└── emergency-shutdown.md                                # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 | Middleware scope (public vs owner routes) |
| Integration | `WebApplicationFactory<Program>` | AC3 — 5 flag × route combinations |
| Operational | Manual `az containerapp update` | AC4 — 60-second activation SLA |
| Accessibility | axe-core | AC6 — emergency HTML meets WCAG 2.1 AA |

### Project Structure Notes

- `wwwroot/` is a standard ASP.NET Core convention; enabling via `app.UseStaticFiles()` is minimal risk.
- Middleware placement matters: BEFORE auth (so emergency serves unauthenticated users) + AFTER exception handler (so exceptions inside middleware still return RFC 9457 — unlikely but possible).

### Architecture compliance

- [epics.md Story 0.17 + AR-030](../planning-artifacts/epics.md) — definition
- [architecture.md §4 D4.20 + §4.8 cost ceiling](../planning-artifacts/architecture.md) — cost control
- [architecture.md §7.4.1](../planning-artifacts/architecture.md) — free-tier stack
- [prd.md NFR-A9](../planning-artifacts/prd.md) — RTL/a11y (emergency HTML must comply)

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| ASP.NET Core | Microsoft.AspNetCore.StaticFiles | Serve `wwwroot/emergency-shutdown.html` |
| Tests | axe-core (via Playwright) | Accessibility check on emergency HTML |

### Testing requirements

- **All 5 AC3 scenarios must pass.** Missing the `/health` case (flag-on → `/health` returns 200) would mean liveness probes fail during emergency shutdown, causing ACA to restart the container indefinitely.
- **Test-fire from AC6 produces an artifact** (email screenshot or Azure Activity Log entry) committed to the runbook as evidence.

### Previous Story Intelligence

- **Story 0.10 (Azure infra)** complete: budget alert at €50/mo wired + Key Vault + ACA running.
- **Story 0.11 (App Insights)** complete: alerts.bicep exists; this story adds `alert-appinsights-cost` rule.
- **Story 0.9 (CI/CD)** complete: deploy workflows exist; env var changes via `az containerapp update` are operator-run, not CI-driven (operational control).

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.17 + AR-030
- [_bmad-output/planning-artifacts/architecture.md §4.8 + §7.4.1](../planning-artifacts/architecture.md) — cost stack
- [_bmad-output/planning-artifacts/prd.md NFR-A9](../planning-artifacts/prd.md) — accessibility compliance

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
