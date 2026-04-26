# Story 0.7: ProcessingActivity ambient context

Status: ready-for-dev

## Story

As a **developer**,
I want **`ProcessingActivity` (`OwnerLedger` / `DebtorConfirmation` / `SystemBackground`) propagated via `AsyncLocal<T>` from HTTP entry point through every layer**,
so that **every audit row, log record, OTel span, and Wolverine message carries unambiguous authorization + intent context (ADR-004)**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. ADR-004 compliance posture pinned day-one. Story 2.1 (first slice) depends on this — the Contacts `MapGroup` endpoint filter sets `ProcessingActivity.OwnerLedger`, which appears in logs/spans/audit rows/messages.

**Why this story now:** After Story 0.6 (bitemporal interceptor) so the audit-row `ProcessingActivity` column exists. Before Story 0.8 (NetArchTest fitness tests) — one of the tests asserts every endpoint/handler sets or inherits a ProcessingActivity. Before Story 2.1 — first consumer.

## Acceptance Criteria

### AC1 — `ProcessingActivity` enum + `AsyncLocal<T>` container

**Given** ADR-004 specifies `ProcessingActivity` as an ambient context value
**When** this story scaffolds the type + container
**Then** `backend/src/Faktuboh.Domain/Compliance/ProcessingActivity.cs`:

```csharp
public enum ProcessingActivity
{
    OwnerLedger,           // authenticated owner actions on their own data
    DebtorConfirmation,    // public-statement surface, token-scoped, non-authenticated
    SystemBackground       // outbox processors, scheduled jobs, re-materialization
}
```

**And** `backend/src/Faktuboh.Application/Compliance/ProcessingActivityContext.cs`:

```csharp
public static class ProcessingActivityContext
{
    private static readonly AsyncLocal<ProcessingActivity?> _current = new();
    public static ProcessingActivity Current =>
        _current.Value ?? throw new InvalidOperationException("ProcessingActivity not set — every entry point must establish one.");
    public static bool IsSet => _current.Value.HasValue;
    public static IDisposable Enter(ProcessingActivity activity)
    {
        var previous = _current.Value;
        _current.Value = activity;
        return new Scope(() => _current.Value = previous);
    }
    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
```

**And** uninitialized access throws — no silent default (per §3.5 + ADR-004) [Source: architecture.md §3.5]

### AC2 — Endpoint filter sets `ProcessingActivity` per `MapGroup`

**Given** slices declare their activity at the `MapGroup` level
**When** a request enters a slice
**Then** an endpoint filter `ProcessingActivityEndpointFilter(ProcessingActivity activity)` calls `ProcessingActivityContext.Enter(activity)` before invoking the next filter + disposes at end
**And** the filter is registered via extension method:

```csharp
// backend/src/Faktuboh.Api/Infrastructure/Compliance/ProcessingActivityExtensions.cs
public static RouteGroupBuilder WithProcessingActivity(this RouteGroupBuilder group, ProcessingActivity activity)
{
    group.AddEndpointFilter(async (ctx, next) =>
    {
        using var scope = ProcessingActivityContext.Enter(activity);
        return await next(ctx);
    });
    return group;
}
```

**And** slices consume it: `app.MapGroup("/v1/contacts").RequireAuthorization().WithProcessingActivity(ProcessingActivity.OwnerLedger)` [Source: architecture.md §3.5 + adrs/adr-006]

### AC3 — Serilog structured property stamping

**Given** Story 0.11 wired Serilog via Aspire ServiceDefaults
**When** any log entry is emitted inside a request scope
**Then** the entry includes `ProcessingActivity` as a structured property (visible in App Insights `customDimensions.ProcessingActivity`)
**And** the stamping mechanism is a Serilog enricher at `backend/aspire/Faktuboh.ServiceDefaults/Logging/ProcessingActivityEnricher.cs`:

```csharp
public sealed class ProcessingActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        if (ProcessingActivityContext.IsSet)
            logEvent.AddOrUpdateProperty(factory.CreateProperty("ProcessingActivity", ProcessingActivityContext.Current.ToString()));
    }
}
```

**And** the enricher is registered in `ServiceDefaults` Serilog configuration [Source: architecture.md §3.5]

### AC4 — OTel span attribute stamping

**Given** Story 0.11 wired OpenTelemetry auto-instrumentation
**When** any Activity (span) is created inside a request scope
**Then** the activity has a tag `faktuboh.processing_activity` with the current value
**And** stamping lives in a `Activity.Current.SetTag(...)` call from the `ProcessingActivityEndpointFilter` entry path (set at scope enter; propagates to child spans automatically via ActivityContext) [Source: architecture.md §3.5]

### AC5 — Audit-log column non-null enforcement

**Given** Story 2.1+ aggregates emit audit rows
**When** an audit row is written
**Then** the `processing_activity` column is NOT NULL (enum stored as text via EF value converter)
**And** the EF configuration for audit tables uses a ValueConverter that reads `ProcessingActivityContext.Current` at write time (via the `SaveChangesInterceptor` chain — audit-row interceptor wires in Epic 3 Story)
**And** this story scaffolds the `processing_activity text NOT NULL` column on `audit_log_events` table stub (migration name `AddAuditLogEventsTable`); the table is otherwise empty at MVP until first audit slice ships [Source: architecture.md §3.5 + §4 D4.20 audit-log seam]

### AC6 — Wolverine message header

**Given** Wolverine outbox ships with Story 2.1
**When** a message is published while `ProcessingActivity.IsSet == true`
**Then** the outgoing Wolverine envelope has a header `x-processing-activity: <value>`
**And** on the receiving side, a Wolverine middleware `ProcessingActivityInboundMiddleware` re-establishes the context via `ProcessingActivityContext.Enter(...)` before invoking the handler
**And** the middleware lives at `backend/src/Faktuboh.Infrastructure/Messaging/Middleware/ProcessingActivityInboundMiddleware.cs` (stub registered; full Wolverine wire-up in Story 2.1 when Wolverine ships) [Source: architecture.md §3.5]

### AC7 — NetArchTest assertion

**Given** Story 0.8 lands the fitness test framework
**When** a fitness test scans endpoints + handlers
**Then** every Minimal API endpoint group that matches pattern `app.MapGroup("/v1/...")` must have either:
- A `.WithProcessingActivity(...)` call in the same chain, OR
- An upstream group that has it (inherited)
**And** every Wolverine handler method must either have `ProcessingActivityContext.Enter` in its stack (enforced by the inbound middleware per AC6), OR explicitly declare `[AllowDefaultProcessingActivity(ProcessingActivity.SystemBackground)]`
**And** the test `backend/tests/Faktuboh.ArchitectureTests/ProcessingActivityEnforcementTests.cs` asserts this via Roslyn source analysis (captures the syntax tree + scans for MapGroup calls missing the extension method) [Source: architecture.md §3.5 + §7.5.3]

### AC8 — System background tasks explicitly set `SystemBackground`

**Given** `MigrationService` (Story 0.6) + `IdempotencyKeyCleanupJob` (Story 0.5) + future scheduled jobs
**When** each runs
**Then** the service wraps its work in `using var _ = ProcessingActivityContext.Enter(ProcessingActivity.SystemBackground);`
**And** `backend/tests/Faktuboh.ArchitectureTests/ProcessingActivityEnforcementTests.cs` additionally asserts: every `IHostedService` implementation opens a `ProcessingActivityContext.Enter` scope in `StartAsync` OR `ExecuteAsync`

## Tasks / Subtasks

### T1 — Scaffold enum + ambient container (AC: 1)

- [ ] Create `backend/src/Faktuboh.Domain/Compliance/ProcessingActivity.cs` per AC1 snippet
- [ ] Create `backend/src/Faktuboh.Application/Compliance/ProcessingActivityContext.cs` per AC1 snippet
- [ ] Unit test `ProcessingActivityContextTests.cs`:
  - `Enter` sets + disposal restores
  - Uninitialized `Current` throws
  - Nested `Enter` calls stack correctly

### T2 — Endpoint filter extension (AC: 2)

- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/Compliance/ProcessingActivityExtensions.cs` per AC2 snippet
- [ ] Update `Program.cs` `/health` endpoint: `app.MapGet("/health", ...).WithProcessingActivity(ProcessingActivity.SystemBackground);` (health check is system-background by convention)
- [ ] Integration test: `ProcessingActivityEndpointFilterTests.cs` — mock endpoint asserts context is set during invocation

### T3 — Serilog enricher (AC: 3)

- [ ] Create `backend/aspire/Faktuboh.ServiceDefaults/Logging/ProcessingActivityEnricher.cs` per AC3 snippet
- [ ] Edit `Faktuboh.ServiceDefaults/Extensions.cs` Serilog config to register the enricher via `.Enrich.With<ProcessingActivityEnricher>()`
- [ ] Integration test: post a request to an endpoint with `ProcessingActivity.OwnerLedger`, query captured log events, assert `ProcessingActivity: "OwnerLedger"` structured property present

### T4 — OTel span tag (AC: 4)

- [ ] In `ProcessingActivityEndpointFilter`, add `Activity.Current?.SetTag("faktuboh.processing_activity", activity.ToString());`
- [ ] Verify via OTel diagnostic console exporter that the tag propagates to downstream spans (e.g., EF Core query spans inherit it)

### T5 — Audit-log table + EF ValueConverter (AC: 5)

- [ ] Create `backend/src/Faktuboh.Domain/Audit/AuditLogEvent.cs` (stub aggregate; full fields land in Epic 3 audit slice)
- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/Configurations/AuditLogEventConfiguration.cs` with `processing_activity` text NOT NULL column + enum ValueConverter
- [ ] Run `dotnet ef migrations add AddAuditLogEventsTable`

### T6 — Wolverine header middleware (AC: 6)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Messaging/Middleware/ProcessingActivityOutboundMiddleware.cs` — sets `envelope.Headers["x-processing-activity"] = current.ToString()` on outbound
- [ ] Create `backend/src/Faktuboh.Infrastructure/Messaging/Middleware/ProcessingActivityInboundMiddleware.cs` — reads header + calls `Enter` + invokes handler
- [ ] Register in Wolverine configuration (stub — full Wolverine lands Story 2.1)

### T7 — NetArchTest enforcement (AC: 7, 8)

- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/ProcessingActivityEnforcementTests.cs`:
  - Test 1 (via Roslyn syntax scan): every `app.MapGroup("/v1/...")` call has `.WithProcessingActivity(...)` in the chain
  - Test 2: every `IHostedService.StartAsync` contains `ProcessingActivityContext.Enter(...)` call
  - Test 3: every Wolverine `[Handler]`-attributed method (discovered via convention) passes through `ProcessingActivityInboundMiddleware` (verified by registration test against Wolverine's handler registry)
- [ ] Wire into Story 0.9's `ci.yml` `backend-architecture-tests` job

### T8 — Background service wrapping (AC: 8)

- [ ] Edit `backend/src/Faktuboh.Infrastructure/Persistence/MigrationService.cs` (Story 0.6): wrap `StartAsync` body in `using var _ = ProcessingActivityContext.Enter(ProcessingActivity.SystemBackground);`
- [ ] Edit `backend/src/Faktuboh.Infrastructure/Idempotency/IdempotencyKeyCleanupJob.cs` (Story 0.5): same wrap

## Dev Notes

### Critical guardrails (do not violate)

1. **No ambient default.** Uninitialized access MUST throw. Silent defaults would let a developer ship a slice that logs empty `ProcessingActivity`, breaking the audit guarantee. [Source: architecture.md §3.5 + epics.md Story 0.7 AC]
2. **`AsyncLocal<T>`, not `ThreadLocal<T>`.** ASP.NET Core requests span threads. `AsyncLocal` survives async/await boundaries; `ThreadLocal` does not. [Source: architecture.md §3.5]
3. **Set ONCE per entry point.** Slices establish their activity at the `MapGroup` filter, not inside handlers. Re-setting mid-handler is suspicious and should require explicit `Enter` scoping.
4. **Re-establish on receiving side of Wolverine** — the outbound middleware writes the header; the inbound middleware reads + `Enter`s. Forgetting this means handlers run without context. [Source: architecture.md §3.5]
5. **Audit-row column is NOT NULL** — application-layer invariant enforced by the interceptor + DB constraint. A null `processing_activity` is a bug that surfaces at write time, not review time.
6. **System-background tasks use `SystemBackground` explicitly.** Do not omit the `Enter` wrap and hope the interceptor derives it.

### Source tree — files to create

```
backend/src/
├── Faktuboh.Domain/Compliance/
│   └── ProcessingActivity.cs                           # NEW (enum)
├── Faktuboh.Domain/Audit/
│   └── AuditLogEvent.cs                                # NEW (stub aggregate)
├── Faktuboh.Application/Compliance/
│   └── ProcessingActivityContext.cs                    # NEW (AsyncLocal)
├── Faktuboh.Api/Infrastructure/Compliance/
│   └── ProcessingActivityExtensions.cs                 # NEW (MapGroup filter)
├── Faktuboh.Infrastructure/Persistence/Configurations/
│   └── AuditLogEventConfiguration.cs                   # NEW
├── Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDDHHMMSS_AddAuditLogEventsTable.cs        # NEW
├── Faktuboh.Infrastructure/Messaging/Middleware/
│   ├── ProcessingActivityOutboundMiddleware.cs         # NEW (Wolverine stub)
│   └── ProcessingActivityInboundMiddleware.cs          # NEW
└── aspire/Faktuboh.ServiceDefaults/Logging/
    └── ProcessingActivityEnricher.cs                   # NEW

backend/tests/
├── Faktuboh.Application.Tests/Compliance/
│   └── ProcessingActivityContextTests.cs               # NEW
├── Faktuboh.Api.Tests/Compliance/
│   └── ProcessingActivityEndpointFilterTests.cs        # NEW
└── Faktuboh.ArchitectureTests/
    └── ProcessingActivityEnforcementTests.cs           # NEW (Roslyn scan)
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 | AsyncLocal semantics (set / dispose / nested / uninitialized throws) |
| Integration | `WebApplicationFactory<Program>` | Serilog enricher emission + OTel tag propagation |
| Architectural | NetArchTest + Roslyn | AC7 + AC8 — every MapGroup + every IHostedService sets context |

### Project Structure Notes

- `AuditLogEvent` stub lives in Domain per Story 2.1+ convention; fields remain minimal until Epic 3 audit slice fills them.
- Wolverine middleware is registered as a stub in this story; Wolverine's full wire-up (Postgres transport + outbox) lands Story 2.1. The middleware classes exist now so fitness tests can assert their presence; activation is deferred.

### Architecture compliance

- [architecture.md §3.5](../planning-artifacts/architecture.md) — ambient context
- [architecture.md §4 D4.20](../planning-artifacts/architecture.md) — audit log seam
- [architecture.md §7.5.3](../planning-artifacts/architecture.md) — fitness tests (covered by Story 0.8)
- [architecture.md ADR-004](../planning-artifacts/architecture.md) — compliance posture day-one

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| ASP.NET Core | Microsoft.AspNetCore.Http.Extensions | `IEndpointFilter` |
| Logging | Serilog | `ILogEventEnricher` |
| OTel | System.Diagnostics (Activity) | Span tag setting |
| Messaging | Wolverine | Header middleware (stub) |
| Tests | NetArchTest.Rules + Microsoft.CodeAnalysis.CSharp.Workspaces | Roslyn syntax-tree scan |

### Testing requirements

- **AC7 Roslyn test** is the load-bearing fitness test for this story. A false-negative (test passes while a slice is missing context) is worse than a false-positive. Run the test against a deliberately-broken slice (MapGroup without WithProcessingActivity) during development to validate the assertion.
- **Integration test captures log events** via `TestCorrelator` (Serilog's test sink) — do not rely on App Insights ingestion for correctness tests.

### Previous Story Intelligence

- **Story 0.1 + 0.3** complete: `Faktuboh.Api/Program.cs` exists with JWT bearer + `/health` endpoint. Update `/health` to use `WithProcessingActivity(SystemBackground)`.
- **Story 0.4 + 0.5** complete: `ErrorCatalog` + idempotency filter exist; idempotency filter's work is system-side (wrap in `SystemBackground`).
- **Story 0.6 (MigrationService)** complete: `MigrationService.StartAsync` needs the `Enter(SystemBackground)` wrap per AC8.
- **Story 0.11 (OTel)** complete: tag propagation requires OTel TracerProvider initialized.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.7 definition
- [_bmad-output/planning-artifacts/architecture.md §3.5](../planning-artifacts/architecture.md) — ambient context
- [_bmad-output/planning-artifacts/architecture.md §7.5.3](../planning-artifacts/architecture.md) — fitness test #3 + ProcessingActivity assertion
- [_bmad-output/planning-artifacts/architecture.md ADR-004](../planning-artifacts/architecture.md) — compliance posture

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
