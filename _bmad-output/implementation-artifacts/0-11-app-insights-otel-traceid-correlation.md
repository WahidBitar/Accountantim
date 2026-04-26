# Story 0.11: App Insights + OTel traceId correlation

Status: ready-for-dev

## Story

As a **developer**,
I want **Aspire 13 OTel auto-instrumentation wired to App Insights with W3C traceparent correlation between Angular frontend and backend (D4.20)**,
so that **one Log Analytics KQL query joins FE + BE events by `traceId` — the operational reason the D4.9 domain code taxonomy pays off**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. Wires the observability substrate that D4.9's RFC 9457 `traceId` extension and D4.20's four dashboards/alerts depend on.

**Why this story now:** Per architecture §4.7 step 11, observability lands after the API contract + Wolverine + capability tokens + crypto. In our Epic 0 sequence this maps to post-0.10 (Azure infra provisioned with App Insights resource) and before 0.12+0.13 (acceptance gates that consume logs). Cannot ship until `APPLICATIONINSIGHTS_CONNECTION_STRING` from Story 0.10 is available.

## Acceptance Criteria

### AC1 — Backend Aspire 13 OTel auto-instrumentation wired

**Given** `Faktuboh.Api` is the HTTP host per Story 0.1
**When** the app boots
**Then** `Faktuboh.ServiceDefaults/Extensions.cs` registers OTel providers via `builder.AddServiceDefaults()`
**And** the OTel pipeline includes `.AddOtlpExporter()` configured to send to App Insights via the connection string from `APPLICATIONINSIGHTS_CONNECTION_STRING` env var
**And** auto-instrumentation covers: HTTP requests/responses, EF Core queries, Wolverine messages (post-Story 2.1), HTTP client calls (downstream)
**And** `Resource` attributes include `service.name: "faktuboh-api"`, `service.namespace: "faktuboh"`, `deployment.environment: <env>` [Source: architecture.md §4 D4.20]

### AC2 — Sampling discipline enforced

**Given** cost ceiling is €25/mo on App Insights per §4.8
**When** telemetry is emitted at production load
**Then** adaptive sampling caps at `5 items/sec` per telemetry type (Aspire default)
**And** App Insights resource-level ingestion sampling is at `20%` (Story 0.10 AC6 sets this)
**And** force-keep rules never sample:
- all exceptions
- all 5xx responses
- all requests with upstream `traceparent Sampled=1`
- all Wolverine outbox failures
- all auth failures (401 + 403)
**And** `ServiceDefaults/Extensions.cs` configures the OTel `TracerProvider` with a custom `Sampler` that implements the force-keep rules via `SamplingResult.RecordAndSample` override:

```csharp
// backend/aspire/Faktuboh.ServiceDefaults/Telemetry/ForceKeepSampler.cs
public sealed class ForceKeepSampler(double fallbackRate) : Sampler
{
    private readonly ParentBasedSampler _parentBased =
        new ParentBasedSampler(new TraceIdRatioBasedSampler(fallbackRate));

    public override SamplingResult ShouldSample(in SamplingParameters p)
    {
        // Force-keep: upstream Sampled=1
        if (p.ParentContext.TraceFlags.HasFlag(ActivityTraceFlags.Recorded))
            return new SamplingResult(SamplingDecision.RecordAndSample);
        // Force-keep: exception tag
        if (p.Tags is not null && p.Tags.Any(kv => kv.Key == "exception.type"))
            return new SamplingResult(SamplingDecision.RecordAndSample);
        // Fallback: parent-based ratio
        return _parentBased.ShouldSample(p);
    }
}
```

[Source: architecture.md §4 D4.20]

### AC3 — RFC 9457 `traceId` extension populated

**Given** Story 0.4 wired `AddProblemDetails()` with a `CustomizeProblemDetails` callback
**When** an endpoint returns a 4xx or 5xx via `TypedResults.Problem` or via `IExceptionHandler`
**Then** the response body's `extensions.traceId` field contains the W3C trace ID from `Activity.Current?.TraceId.ToString()` (32-char lowercase hex)
**And** the response includes a `traceparent` HTTP header reflecting the same trace
**And** an integration test `CreateContactEndpointTests.cs` (Story 2.1) asserts `traceId` matches the `traceparent` header [Source: architecture.md §4 D4.9 + D4.20]

### AC4 — Angular HTTP interceptor propagates traceparent + logs FE events

**Given** `libs/core/src/lib/http/correlation-id.interceptor.ts` exists from Story 0.2 placeholder
**When** an HTTP request fires from any Angular feature
**Then** the interceptor:
1. Generates a W3C `traceparent` header value for new root traces (`00-<traceId>-<spanId>-01`)
2. If the response is 4xx/5xx, logs a `{ traceId, statusCode, path, errorCode, timestamp }` record to App Insights via `@microsoft/applicationinsights-web`
3. If the response is 2xx, no per-request FE log (only client errors are correlated)
**And** the FE App Insights is initialized with the connection string from `environment.appInsightsConnectionString` (env-replacement at build time)
**And** the Chrome DevTools network tab shows matching `traceparent` headers on request + response [Source: architecture.md §4 D4.20 + D4.9]

### AC5 — KQL query joins FE + BE events within 5 seconds

**Given** App Insights ingests both FE + BE events
**When** a user triggers a failing request (e.g., 400 validation error)
**Then** the following KQL query returns matched FE + BE events within 5 seconds of the event occurring:

```kusto
// tests/kql/traceid-correlation.kql
let traceTarget = "<specific_trace_id_from_test>";
union customEvents, exceptions, requests
| where operation_Id == traceTarget
| extend source = case(
    name == "api.error_response", "backend",
    name == "ui.http_error", "frontend",
    "other")
| project timestamp, source, name, operation_Id, operation_ParentId, customDimensions
| order by timestamp asc
```

**And** the query returns at least 2 rows — one FE row + one BE row — with matching `operation_Id` (= W3C `traceId`)
**And** `tests/Faktuboh.Api.Tests/Observability/TraceIdCorrelationIntegrationTests.cs` asserts this end-to-end by fetching via Azure Log Analytics REST API in a staging environment [Source: architecture.md §4 D4.20]

### AC6 — Four dashboards provisioned via ARM/Bicep

**Given** D4.20 requires four dashboards
**When** `infra/azure/modules/dashboards.bicep` provisions them
**Then** four dashboards exist in Azure Portal:
1. **Golden signals** — p50/p95/p99 latency + error rate + throughput + saturation for both `app.*` and `statement.*` surfaces
2. **Wolverine outbox health** — enqueued / processed / failed counts + dead-letter table size + in-flight duration p95 (populated post-Story 2.1 when Wolverine transport ships)
3. **AFD/WAF** — Since AFD is dissolved per §7.4.1 and ASWA built-in CDN is adopted, rename to **ASWA + rate-limit** dashboard tracking SWA request throughput + 4xx-at-edge rate
4. **Business liveness** — signups/day + statement-views/day + idempotency-key-conflicts/day (populated via Story 0.14 telemetry primitive events)
**And** each dashboard has a JSON template committed at `infra/azure/modules/dashboards/*.json` [Source: architecture.md §4 D4.20 four dashboards]

### AC7 — Four alerts wired

**Given** D4.20 requires four alerts
**When** `infra/azure/modules/alerts.bicep` provisions them
**Then** four alert rules exist:
- `alert-backend-latency-p95` — p95 backend latency >1s for 10 min → warning severity
- `alert-error-rate` — error rate >2% for 5 min → warning severity
- `alert-outbox-dlq` — dead-letter table >50 rows → critical severity (fires post-Story 2.1)
- `alert-aswa-4xx` — ASWA 4xx-at-edge rate >10% for 1 hour → warning severity (substituting original AFD cache-hit alert)
**And** all four alerts route to `wahid.wb@gmail.com` via Action Group `ag-faktuboh-email` [Source: architecture.md §4 D4.20 four alerts]

### AC8 — Log retention 30 days

**Given** Story 0.10 provisioned Log Analytics Workspace with `retentionInDays: 30`
**When** AC8 is verified
**Then** the setting persists
**And** `docs/runbooks/observability.md` documents the distinction: operational telemetry = 30 days; audit log = 7 years separate retention (audit log is a separate concern per D4.20 audit-log seam — deferred to Epic 3)

## Tasks / Subtasks

### T1 — Wire Aspire OTel providers (AC: 1, 2)

- [ ] Edit `backend/aspire/Faktuboh.ServiceDefaults/Extensions.cs` to chain:
  ```csharp
  public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
  {
      builder.ConfigureOpenTelemetry();
      builder.AddDefaultHealthChecks();
      builder.Services.AddServiceDiscovery();
      return builder;
  }

  static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
  {
      builder.Logging.AddOpenTelemetry(logging =>
      {
          logging.IncludeFormattedMessage = true;
          logging.IncludeScopes = true;
      });

      builder.Services.AddOpenTelemetry()
          .ConfigureResource(r => r.AddService(serviceName: "faktuboh-api", serviceNamespace: "faktuboh"))
          .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation())
          .WithTracing(t =>
          {
              t.SetSampler(new ForceKeepSampler(fallbackRate: 0.2));
              t.AddAspNetCoreInstrumentation(o => o.RecordException = true);
              t.AddHttpClientInstrumentation();
              t.AddEntityFrameworkCoreInstrumentation();
              // Wolverine instrumentation added in Story 2.1 when Wolverine ships
          });

      builder.AddOpenTelemetryExporters();
      return builder;
  }

  static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
  {
      var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
      if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
      {
          builder.Services.AddOpenTelemetry()
              .UseAzureMonitor(o => o.ConnectionString = appInsightsConnectionString);
      }
      return builder;
  }
  ```
- [ ] Add NuGet packages: `Azure.Monitor.OpenTelemetry.AspNetCore`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Instrumentation.Runtime`
- [ ] Create `ForceKeepSampler.cs` in `ServiceDefaults/Telemetry/` per AC2 snippet

### T2 — RFC 9457 traceId extension (AC: 3)

- [ ] Confirm Story 0.4's `CustomizeProblemDetails` callback emits `traceId` — if not, add:
  ```csharp
  options.CustomizeProblemDetails = ctx =>
  {
      ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
      if (ctx.ProblemDetails.Extensions.TryGetValue("code", out var code) && code is string c)
          ctx.ProblemDetails.Type = $"https://faktuboh.com/errors/{c}";
  };
  ```
- [ ] Add `backend/tests/Faktuboh.Api.Tests/Observability/TraceIdInProblemDetailsTests.cs` asserting `traceId` is a 32-char lowercase hex string matching `Activity.Current?.TraceId` on a forced-4xx response

### T3 — Angular HTTP interceptor + App Insights FE (AC: 4)

- [ ] Install `@microsoft/applicationinsights-web` in `frontend/package.json`
- [ ] Edit `libs/core/src/lib/http/correlation-id.interceptor.ts` (placeholder from Story 0.2):
  ```typescript
  import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
  import { inject } from '@angular/core';
  import { ApplicationInsights } from '@microsoft/applicationinsights-web';
  import { tap } from 'rxjs';

  export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
    const ai = inject(ApplicationInsights, { optional: true });
    const traceId = crypto.randomUUID().replace(/-/g, '').slice(0, 32);
    const spanId = crypto.randomUUID().replace(/-/g, '').slice(0, 16);
    const cloned = req.clone({ setHeaders: { traceparent: `00-${traceId}-${spanId}-01` } });
    return next(cloned).pipe(
      tap({
        error: (err) => {
          if (err instanceof HttpErrorResponse) {
            const backendTraceId = err.error?.traceId ?? traceId;
            ai?.trackEvent({ name: 'ui.http_error', properties: { traceId: backendTraceId, statusCode: err.status, path: req.url, errorCode: err.error?.code } });
          }
        }
      })
    );
  };
  ```
- [ ] Initialize App Insights in `libs/core/src/lib/observability/app-insights.provider.ts` — factory returns `new ApplicationInsights({ config: { connectionString: environment.appInsightsConnectionString } })` + `loadAppInsights()`
- [ ] Register via `provideAppInsights()` function in both apps' `app.config.ts`

### T4 — Dashboards + alerts (AC: 6, 7)

- [ ] Create `infra/azure/modules/dashboards.bicep` with `Microsoft.Portal/dashboards@2020-09-01-preview` resources for the four dashboards
- [ ] Create dashboard JSON templates at `infra/azure/modules/dashboards/*.json` — can start from Azure Portal's "export" feature on a manually-authored dashboard
- [ ] Create `infra/azure/modules/alerts.bicep` with `Microsoft.Insights/metricAlerts@2018-03-01` + `Microsoft.Insights/scheduledQueryRules@2023-03-15-preview` for the four alerts
- [ ] Create `infra/azure/modules/action-groups.bicep` with `ag-faktuboh-email` action group routing to `wahid.wb@gmail.com`

### T5 — KQL correlation test (AC: 5)

- [ ] Create `tests/kql/traceid-correlation.kql` per AC5 snippet
- [ ] Write `backend/tests/Faktuboh.Api.Tests/Observability/TraceIdCorrelationIntegrationTests.cs`:
  - Uses `Azure.Monitor.Query.LogsQueryClient` to hit Log Analytics
  - Triggers a known-failing request to staging
  - Polls Log Analytics for up to 30 seconds (App Insights has ~15-30s ingestion delay)
  - Asserts both FE + BE rows appear with matching `operation_Id`
- [ ] Test runs in `[Trait("Category", "Integration")]` + requires staging credentials — CI-gated to `deploy-staging.yml` post-deploy verification job

### T6 — Runbook (AC: 8)

- [ ] Create `docs/runbooks/observability.md` covering:
  - OTel auto-instrumentation scope + sampling rates
  - RFC 9457 `traceId` wire format
  - Four dashboards + four alerts descriptions
  - KQL query examples for common triage scenarios
  - Audit log separation (7-year retention, distinct infra — deferred to Epic 3)

## Dev Notes

### Critical guardrails (do not violate)

1. **Force-keep rules are not negotiable.** Exceptions, 5xx, upstream-sampled traces, outbox failures, auth failures — all sampled at 100%. Ratio sampling applies only to non-error traffic. [Source: architecture.md §4 D4.20]
2. **`traceId` matches `Activity.Current?.TraceId`**, not `HttpContext.TraceIdentifier`. The latter is an ASP.NET Core identifier unrelated to W3C traceparent. [Source: architecture.md §4 D4.9]
3. **App Insights budget alert at €25/mo** is wired via Story 0.10's `budget-faktuboh-*` — telemetry sprawl is the most common cause of budget overrun.
4. **Angular interceptor logs ONLY client errors (4xx/5xx).** Logging every request doubles telemetry volume for zero incremental diagnostic value at MVP. [Source: architecture.md §4 D4.20]
5. **Connection string lives in Key Vault, not in appsettings.** ACA env var `APPLICATIONINSIGHTS_CONNECTION_STRING` is populated from KV per Story 0.10 AC5.
6. **Audit log is NOT in App Insights.** D4.20 explicitly separates audit (append-only Postgres table per Epic 3) from operational telemetry (App Insights, 30-day retention).

### Source tree — files to create

```
backend/
├── aspire/Faktuboh.ServiceDefaults/
│   ├── Extensions.cs                            # EDIT (wire OTel + Azure Monitor)
│   └── Telemetry/
│       └── ForceKeepSampler.cs                  # NEW
└── tests/Faktuboh.Api.Tests/
    └── Observability/
        ├── TraceIdInProblemDetailsTests.cs      # NEW
        └── TraceIdCorrelationIntegrationTests.cs # NEW (staging-gated)

frontend/
├── libs/core/src/lib/
│   ├── http/correlation-id.interceptor.ts       # EDIT (from placeholder)
│   └── observability/
│       └── app-insights.provider.ts             # NEW
└── apps/*/src/app/app.config.ts                 # EDIT (register provideAppInsights)

infra/
└── azure/modules/
    ├── dashboards.bicep                         # NEW
    ├── dashboards/
    │   ├── golden-signals.json                  # NEW
    │   ├── outbox-health.json                   # NEW
    │   ├── aswa-edge.json                       # NEW
    │   └── business-liveness.json               # NEW
    ├── alerts.bicep                             # NEW
    └── action-groups.bicep                      # NEW

tests/
└── kql/
    └── traceid-correlation.kql                  # NEW

docs/runbooks/
└── observability.md                             # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 | `ForceKeepSampler` behavior per sampling decision rule |
| Integration (backend) | `WebApplicationFactory<Program>` | AC3 `traceId` matches `Activity.Current?.TraceId` |
| Integration (FE) | Playwright + App Insights ingestion wait | AC5 KQL correlation within 5s of event |
| Staging-deploy smoke | `TraceIdCorrelationIntegrationTests.cs` + staging creds | Part of `deploy-staging.yml` post-deploy verification |

### Project Structure Notes

- **Dashboards/alerts in `infra/azure/modules/` not `infra/azure/dashboards/`** — follows the Bicep module pattern from Story 0.10.
- **AFD rename**: architecture.md §4 D4.20 lists "AFD + WAF" as dashboard #3, but §7.4.1 dissolved AFD in favor of ASWA. This story renames dashboard #3 to "ASWA + rate-limit" and cache-hit alert to "4xx-at-edge" to match the actual topology. If AFD is added later (RT-AFD), rename back.

### Architecture compliance

- [architecture.md §4 D4.20](../planning-artifacts/architecture.md) — observability decision
- [architecture.md §4 D4.9](../planning-artifacts/architecture.md) — traceId in Problem Details
- [architecture.md §4.7 step 11](../planning-artifacts/architecture.md) — observability implementation sequence
- [architecture.md §4.8](../planning-artifacts/architecture.md) — App Insights $25/mo budget
- [architecture.md §7.4.1](../planning-artifacts/architecture.md) — AFD dissolved → ASWA

### Library/framework requirements

| Layer | Package | Version | Purpose |
|---|---|---|---|
| OTel (backend) | Azure.Monitor.OpenTelemetry.AspNetCore | latest | Azure Monitor export |
| OTel (backend) | OpenTelemetry.Extensions.Hosting | latest | Hosting integration |
| OTel (backend) | OpenTelemetry.Instrumentation.AspNetCore | latest | HTTP instrumentation |
| OTel (backend) | OpenTelemetry.Instrumentation.Http | latest | HttpClient instrumentation |
| OTel (backend) | OpenTelemetry.Instrumentation.EntityFrameworkCore | latest | EF Core instrumentation |
| OTel (backend) | OpenTelemetry.Instrumentation.Runtime | latest | Process metrics |
| App Insights (FE) | @microsoft/applicationinsights-web | 3.x | FE event emission |
| Log Analytics query | Azure.Monitor.Query | latest | Integration test KQL execution |

### Testing requirements

- **`TraceIdCorrelationIntegrationTests.cs` is staging-gated.** Do not run on PR — it needs real App Insights + ~30s ingestion wait. Runs as a post-deploy verification step in `deploy-staging.yml`.
- **Unit tests for `ForceKeepSampler`** cover every branch in `ShouldSample` (parent sampled, exception tag, fallback ratio, fallback below ratio, fallback above ratio).
- **Manual test path:** deploy, trigger a known-failing request, observe FE + BE events in App Insights portal (join-by-operation-Id), verify within 5s of occurrence.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: `Faktuboh.ServiceDefaults/Extensions.cs` exists as Aspire template output.
- **Story 0.2 (FE workspace)** complete: `libs/core/src/lib/http/correlation-id.interceptor.ts` exists as a placeholder from the core lib bootstrap.
- **Story 0.4 (RFC 9457 middleware)** will set up the base `AddProblemDetails()` call — this story extends the `CustomizeProblemDetails` callback with `traceId`.
- **Story 0.10 (Azure infra)** provides the App Insights resource + Key Vault `ai-connection` secret.
- **Story 2.1 (first slice)** will validate the full observability loop end-to-end.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.11 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.20](../planning-artifacts/architecture.md) — observability
- [_bmad-output/planning-artifacts/architecture.md §4 D4.9](../planning-artifacts/architecture.md) — RFC 9457 traceId
- [_bmad-output/planning-artifacts/architecture.md §4.7](../planning-artifacts/architecture.md) — implementation sequence

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
