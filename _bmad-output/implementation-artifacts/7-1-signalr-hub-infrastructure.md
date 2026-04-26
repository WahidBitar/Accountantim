# Story 7.1: SignalR hub infrastructure

Status: ready-for-dev

## Story

As a **developer**,
I want **a SignalR hub wired at `/hubs/notifications` with auth context on the socket + automatic reconnection within 5 seconds**,
so that **Epic 7's remaining stories have real-time push primitives to consume** (FR51, NFR-I4) [Source: epics.md Epic 7 Story 7.1; prd.md FR51, NFR-I4].

**Covers requirements:** Epic 7 — FR51 (real-time owner notifications), NFR-I4 (auto-reconnect within 5s), AR-050 cost-circuit-breaker extension (concurrent-connection cap) [Source: epics.md Epic 7 Story 7.1; architecture.md §4.8 cost ceiling].

**Why this story first in Epic 7:** Stories 7.2 (real-time confirmation push) and 7.4–7.6 (nudge triggers if pushed live) all depend on the hub being live with auth + reconnection + cost-cap discipline. The hub is the foundation; getting auth on the handshake + the cap right before any push event ships avoids retrofitting under load.

## Acceptance Criteria

### AC1 — SignalR hub registered at `/hubs/notifications` with Bearer auth on handshake

**Given** the owner-workspace SPA is authenticated against Auth0
**When** the SPA opens a SignalR connection to `wss://api.faktuboh.com/hubs/notifications?access_token=<jwt>`
**Then** the hub at `backend/src/Faktuboh.Api/Hubs/NotificationsHub.cs` registers via `app.MapHub<NotificationsHub>("/hubs/notifications").RequireAuthorization()`
**And** the JWT Bearer authentication scheme is configured to read the token from the `access_token` query string for paths starting with `/hubs/` (SignalR WebSocket cannot send `Authorization` headers from browsers)
**And** invalid / missing / expired tokens reject the connection with HTTP 401 during handshake before the WebSocket upgrade completes
**And** the connection is closed within 30s of token expiration via a server-side timer that calls `Context.Abort()` on each connection mapped to that subject
**And** `ProcessingActivity.OwnerLedger` flows as a hub feature (`Context.Items["ProcessingActivity"]`) restored on every hub method invocation [Source: epics.md Epic 7 Story 7.1 AC; architecture.md §3.5 ProcessingActivity].

### AC2 — Connection-tracking + per-subject group registration

**Given** the hub must dispatch messages to a specific owner's connections (potentially multiple — desktop + mobile tabs)
**When** a connection is established
**Then** `OnConnectedAsync` extracts the `subject_id` from `Context.User.FindFirstValue(ClaimTypes.NameIdentifier)`
**And** the connection is added to the SignalR group named `subject:{subject_id}` via `Groups.AddToGroupAsync(Context.ConnectionId, $"subject:{subjectId}")`
**And** an entry is recorded in `signalr_connections` Redis cache (or in-memory `IConnectionRegistry`) keyed by `subject_id` with values `{ connection_id, connected_at, user_agent_hash }`
**And** `OnDisconnectedAsync` removes the entry within 5 seconds of the disconnect
**And** server-side broadcast: `await _hubContext.Clients.Group($"subject:{subjectId}").SendAsync("StatementConfirmed", payload)` is the only push pattern allowed (no per-`ConnectionId` direct sends — multi-tab support requires group)
**And** an integration test asserts a publish to `subject:{X}` reaches all of subject X's connected tabs [Source: epics.md Epic 7 Story 7.1].

### AC3 — Auto-reconnection within 5s per NFR-I4

**Given** the Angular service `apps/owner-workspace/src/app/core/realtime/notifications-hub.service.ts` wraps the hub
**When** the WebSocket disconnects unexpectedly (network blip, server restart, token refresh)
**Then** the service uses `HubConnectionBuilder().withAutomaticReconnect([0, 1000, 2000, 4000])` — first retry immediate, then 1s, 2s, 4s
**And** total reconnect window < 5s for the first 4 attempts per NFR-I4
**And** on reconnect, the service replays group subscriptions (rebinding `subject:{subjectId}` is automatic via the hub's `OnConnectedAsync`)
**And** an Angular signal `connectionState: signal<'connected' | 'connecting' | 'disconnected'>` exposes the current state for UI consumption
**And** an integration test (Playwright) drops the WebSocket via DevTools Protocol and asserts state transitions to `connecting` then `connected` within 5s [Source: prd.md NFR-I4; epics.md Story 7.1].

### AC4 — Disconnect banner appears after 10s of disconnect

**Given** transient network blips < 10s should not cause UI churn
**When** `connectionState` has been `disconnected` or `connecting` for ≥ 10 seconds
**Then** a non-blocking banner renders at the top of the owner-workspace shell: "Reconnecting…" (Arabic + English via Transloco)
**And** the banner clears within 1 second of `connectionState` returning to `connected`
**And** the banner uses `<p-message severity="warn">` with `aria-live="polite"`
**And** the banner does NOT block any other UI interaction — it is informational only
**And** a Vitest unit test asserts the 10s debounce + clear behavior [Source: epics.md Story 7.1 AC banner].

### AC5 — Outage fallback after 60s falls back to passive display

**Given** the hub may be unreachable for an extended period (server outage, mobile in tunnel)
**When** `connectionState` has been `disconnected` for ≥ 60 seconds
**Then** the service stops attempting reconnection (per the `withAutomaticReconnect` array bound)
**And** the SPA continues to function — all rxResource-backed views work normally; the `ReconciliationMeta` confirmations fall back to next-session passive display per Story 4.7
**And** a "Manual reconnect" button appears in the banner; tapping it calls `hubConnection.start()` to retry
**And** no requests fail; no errors surface to the user beyond the banner
**And** an integration test asserts a 60s+ outage does NOT block any other functionality [Source: epics.md Story 7.1 AC outage fallback].

### AC6 — Concurrent-connection cap per AR-050 cost circuit-breaker

**Given** SignalR connections each consume a small but non-zero amount of memory + the Azure Container Apps free tier has finite headroom
**When** the cap is configured at `SignalR:MaxConcurrentConnections = 500` (MVP scale per delegated decision)
**Then** the hub middleware `SignalRConcurrencyLimiter` checks the current connection count from `IConnectionRegistry` on `OnConnectedAsync`
**And** if the count is at or above the cap, the new connection is rejected with HTTP 503 + the connection is aborted
**And** the rejected user's SPA falls back to the 60s passive-display path (AC5) automatically
**And** an App Insights metric `signalr.active_connections` emits every 60 seconds
**And** an alert fires when active-connections crosses 80% of the cap (400) so the cap can be raised before genuine cap-hit incidents
**And** the cap value extends Story 0.17's `FAKTUBOH_EMERGENCY_SHUTDOWN` env flag — when shutdown is on, the cap is forced to 0 (no new connections) [Source: architecture.md §4.8 cost ceiling AR-050; Story 0.17].

### AC7 — Telemetry `signalr_connection_opened` + `signalr_connection_closed`

**Given** AR-047 telemetry primitive exists (Story 0.14)
**When** a connection opens or closes
**Then** `TelemetryEmitter.Emit("signalr_connection_opened", new SignalrConnectionOpenedTelemetry(opaque_subject_id, ua_hash, connected_via: "ws"))` fires
**And** `signalr_connection_closed` fires on disconnect with `duration_seconds_bucket` (`"<10s" | "<1m" | "<10m" | ">10m"`) — not raw seconds (PII-adjacent)
**And** PiiGuard validates no raw subject_id, no IP address, no user-agent string in the payload [Source: Story 0.14].

### AC8 — Hub never broadcasts to all connections (no fan-out global)

**Given** broadcast-to-all is a cost-amplification + privacy risk
**When** the codebase is scanned
**Then** NetArchTest asserts no call to `Clients.All.SendAsync(...)` exists in the codebase under `Hubs/`
**And** only `Clients.Group($"subject:{subjectId}").SendAsync(...)` is permitted
**And** the test fails CI on violation [Source: architecture.md §2.9; AR-050 cost discipline].

## Tasks / Subtasks

### Backend — Hub registration + auth

- [ ] **T1 (AC: 1)** — Create `backend/src/Faktuboh.Api/Hubs/NotificationsHub.cs`
  - [ ] Inherits `Hub`
  - [ ] `OnConnectedAsync` adds connection to `subject:{subjectId}` group + records in `IConnectionRegistry`
  - [ ] `OnDisconnectedAsync` removes from registry within 5s
  - [ ] `[Authorize]` attribute at class level
- [ ] **T2 (AC: 1)** — Configure JWT Bearer to read query-string token for `/hubs/*` paths in `Program.cs`
  - [ ] `options.Events.OnMessageReceived = ctx => { if (ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = ctx.Request.Query["access_token"]; return Task.CompletedTask; }`
- [ ] **T3 (AC: 1)** — Register hub in `Program.cs` via `app.MapHub<NotificationsHub>("/hubs/notifications").RequireAuthorization()`
- [ ] **T4 (AC: 1)** — `ProcessingActivity` propagation
  - [ ] `OnConnectedAsync` stamps `Context.Items["ProcessingActivity"] = ProcessingActivity.OwnerLedger`
  - [ ] Hub method filter restores this on each invocation

### Backend — Connection registry + cap

- [ ] **T5 (AC: 2, 6)** — Create `IConnectionRegistry` + `InMemoryConnectionRegistry` in `backend/src/Faktuboh.Infrastructure/Realtime/`
  - [ ] Methods: `RegisterAsync(subjectId, connectionId, uaHash)`, `UnregisterAsync(connectionId)`, `GetActiveCountAsync()`, `GetForSubjectAsync(subjectId)`
  - [ ] At MVP, in-memory dictionary keyed by `connection_id` is sufficient (single ACA replica); design admits Redis swap when ACA scales horizontally
- [ ] **T6 (AC: 6)** — Create `SignalRConcurrencyLimiter` middleware
  - [ ] Reads `SignalR:MaxConcurrentConnections` from config (default 500)
  - [ ] Reads `FAKTUBOH_EMERGENCY_SHUTDOWN` env flag — forces cap to 0 if set
  - [ ] Rejects new connections at-or-above cap with HTTP 503
  - [ ] Hooked into `NotificationsHub.OnConnectedAsync` as the first step

### Backend — Token-expiration auto-disconnect

- [ ] **T7 (AC: 1)** — Create `TokenExpirationMonitor` background service
  - [ ] Periodically (every 30s) iterates `IConnectionRegistry` connections
  - [ ] For each, reads the token expiration from claims; if `now() > exp`, calls `_hubContext.Clients.Client(connectionId).SendAsync("force_disconnect")` + `Context.Abort()` server-side
  - [ ] Connection registry tracks the original token's `exp` claim alongside the connection record

### Backend — Telemetry

- [ ] **T8 (AC: 7)** — Define `SignalrConnectionOpenedTelemetry` + `SignalrConnectionClosedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
  - [ ] Fields: `OpaqueSubjectId` (SHA256), `UaHash`, `ConnectedVia: "ws"` (constant; for future SSE swap-out)
  - [ ] `SignalrConnectionClosedTelemetry` adds `DurationSecondsBucket: "<10s" | "<1m" | "<10m" | ">10m"`
- [ ] **T9 (AC: 7)** — Wire emission in `NotificationsHub.OnConnectedAsync` + `OnDisconnectedAsync`

### Backend — App Insights metric + alert

- [ ] **T10 (AC: 6)** — Emit `signalr.active_connections` metric every 60s via `IHostedService`
  - [ ] Reads count from `IConnectionRegistry`
  - [ ] Tracks via `TelemetryClient.GetMetric("signalr.active_connections").TrackValue(count)`
- [ ] **T11 (AC: 6)** — Add Bicep alert rule in `infra/azure/modules/alerts.bicep`
  - [ ] `alert-signalr-cap-approaching` — threshold > 400, action group `ag-faktuboh-email`

### Backend — Architecture tests

- [ ] **T12 (AC: 8)** — Add NetArchTest in `tests/Faktuboh.ArchitectureTests/SignalRTests.cs`
  - [ ] Asserts no method under `Hubs/` calls `IClientProxy.SendAsync` via `Clients.All`
  - [ ] Asserts every `SendAsync` call originates from `Clients.Group("subject:...")`

### Frontend — Hub service + reconnection

- [ ] **T13 (AC: 3, 4, 5)** — Create `apps/owner-workspace/src/app/core/realtime/notifications-hub.service.ts`
  - [ ] `HubConnectionBuilder().withUrl('/hubs/notifications', { accessTokenFactory: () => authService.getAccessToken() }).withAutomaticReconnect([0, 1000, 2000, 4000]).build()`
  - [ ] Exposes `connectionState: Signal<'connected' | 'connecting' | 'disconnected'>`
  - [ ] Exposes typed event streams: `statementConfirmed$: Observable<StatementConfirmedPayload>` (consumed by Story 7.2)
  - [ ] On `onreconnecting`/`onreconnected`/`onclose` updates `connectionState`
  - [ ] After 60s in `disconnected`, stops reconnecting + emits `outageFallback$` event
- [ ] **T14 (AC: 3)** — Add `@microsoft/signalr` to `package.json` dependencies
  - [ ] Pin to latest stable matching server-side `Microsoft.AspNetCore.SignalR` package version
- [ ] **T15 (AC: 4)** — Create `apps/owner-workspace/src/app/core/realtime/connection-banner.component.ts`
  - [ ] Standalone, OnPush, signal-based
  - [ ] Reads `connectionState` from injected service
  - [ ] 10s debounce before showing banner; clears within 1s on reconnect
  - [ ] Renders `<p-message severity="warn">` with `aria-live="polite"`
  - [ ] Bilingual via Transloco keys `realtime.reconnecting` + `realtime.manual_reconnect`
- [ ] **T16 (AC: 5)** — Wire "Manual reconnect" button in banner
  - [ ] Calls `hubConnection.start()` from the service
  - [ ] Visible only after 60s outage threshold

### Tests — Backend

- [ ] **T17 (AC: 1, 2, 6)** — Integration tests in `tests/Faktuboh.Api.Tests/Hubs/NotificationsHubTests.cs`
  - [ ] Bearer token in query string accepted on handshake (valid token → 101 Switching Protocols)
  - [ ] Missing token → 401 before WebSocket upgrade
  - [ ] Expired token → 401
  - [ ] Multi-tab: two connections for same subject both receive group broadcast
  - [ ] Concurrency cap: connections beyond `MaxConcurrentConnections` rejected with 503
  - [ ] `FAKTUBOH_EMERGENCY_SHUTDOWN=true` rejects all new connections
- [ ] **T18 (AC: 7)** — Telemetry assertion via `AssertEventEmitted("signalr_connection_opened")`
- [ ] **T19 (AC: 8)** — Architecture test for forbidden `Clients.All`

### Tests — Frontend

- [ ] **T20 (AC: 3, 4)** — Vitest unit tests for `notifications-hub.service.ts`
  - [ ] Mock `HubConnection` — assert reconnection retry intervals match `[0, 1000, 2000, 4000]`
  - [ ] Assert `connectionState` transitions on lifecycle events
  - [ ] Assert 60s outage triggers `outageFallback$` emission + stops reconnection
- [ ] **T21 (AC: 4)** — Vitest unit tests for `connection-banner.component.ts`
  - [ ] 10s debounce verified via `fakeAsync` + `tick(10000)`
  - [ ] Banner clears < 1s after `connected`
- [ ] **T22 (AC: 3)** — Playwright E2E in `apps/owner-workspace/e2e/realtime/reconnection.spec.ts`
  - [ ] Drop WebSocket via CDP `Network.emulateNetworkConditions(offline=true)`
  - [ ] Assert banner appears after 10s
  - [ ] Restore network; assert banner clears within 1s + `connectionState=connected` within 5s

## Dev Notes

### Critical guardrails (do not violate)

1. **No `Clients.All.SendAsync`.** Broadcast-to-all is a cost-amplification + privacy bug. NetArchTest enforces. The only allowed pattern is `Clients.Group($"subject:{X}")`.
2. **Bearer token in query string is SignalR-specific.** Browsers can't add `Authorization` headers to WebSocket handshakes. The `OnMessageReceived` extraction is scoped to `/hubs/*` paths only — owner-workspace API endpoints continue using `Authorization: Bearer` headers per ADR-021.
3. **Cost cap is non-negotiable.** AR-050 ceiling at €100/mo would shred fast under unbounded SignalR connections (memory + CPU). The 500-connection MVP cap + 80%-warning alert + emergency-shutdown integration is the cost discipline.
4. **In-memory connection registry is single-replica only.** When ACA scales horizontally (post-MVP), swap to Redis-backed registry. Design `IConnectionRegistry` interface accordingly — no leak of `Dictionary` semantics through the interface.
5. **5s reconnection ceiling per NFR-I4.** The retry array `[0, 1000, 2000, 4000]` sums to 7s wall-clock for 4 attempts but the FIRST reconnect is immediate. Per NFR-I4 the SLA is "first successful reconnect within 5s under typical mobile conditions" — measured at the third retry attempt at the latest.
6. **Banner only after 10s.** Showing a banner for every transient < 1s blip would be visual noise. The 10s debounce is a UX-DR commitment — confirm with Sally if challenged.
7. **60s outage falls back to passive display.** Story 4.7's passive last-confirmation date pattern is the safety net. The SPA must continue functioning without the hub; only the real-time push capability degrades.
8. **`ProcessingActivity` flows through hub method invocations.** Without it, audit log entries from hub-initiated paths would be missing this required column. Hub method filter restores it.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Hubs/
│   │       └── NotificationsHub.cs                          # NEW
│   ├── Faktuboh.Infrastructure/
│   │   ├── Realtime/
│   │   │   ├── IConnectionRegistry.cs                       # NEW
│   │   │   ├── InMemoryConnectionRegistry.cs                # NEW
│   │   │   ├── SignalRConcurrencyLimiter.cs                 # NEW
│   │   │   ├── TokenExpirationMonitor.cs                    # NEW (HostedService)
│   │   │   └── ActiveConnectionsMetricEmitter.cs            # NEW (HostedService)
│   │   └── Telemetry/Events/
│   │       ├── SignalrConnectionOpenedTelemetry.cs          # NEW
│   │       └── SignalrConnectionClosedTelemetry.cs          # NEW
│   └── Faktuboh.Api/Program.cs                              # EDIT (auth query-string + MapHub)
└── tests/
    ├── Faktuboh.Api.Tests/Hubs/
    │   └── NotificationsHubTests.cs                         # NEW (Integration)
    └── Faktuboh.ArchitectureTests/
        └── SignalRTests.cs                                  # NEW

frontend/
└── apps/owner-workspace/src/app/core/realtime/
    ├── notifications-hub.service.ts                         # NEW
    ├── notifications-hub.service.spec.ts                    # NEW
    ├── connection-banner.component.ts                       # NEW
    ├── connection-banner.component.html                     # NEW
    ├── connection-banner.component.spec.ts                  # NEW
    └── realtime-payloads.ts                                 # NEW (typed event payload contracts)

infra/azure/modules/
└── alerts.bicep                                             # EDIT (alert-signalr-cap-approaching)
```

### Testing standards (per architecture §5.3.3)

| Level | Coverage | Tools |
|---|---|---|
| Unit (BE) | Connection registry, concurrency limiter | xUnit v3 + NSubstitute |
| Integration (BE) | Hub handshake, group broadcast, cap enforcement | `WebApplicationFactory<Program>` + Testcontainers Postgres + real SignalR client (`Microsoft.AspNetCore.SignalR.Client`) |
| Unit (FE) | Service reconnection logic, banner debounce | Vitest + Angular Testing Library |
| E2E | Reconnection flow under simulated network drop | Playwright + CDP `Network.emulateNetworkConditions` |

### Project Structure Notes

This story scaffolds a new infrastructure concern (real-time push) that crosses the slice boundary by design — it is a transport, not a slice. Slices publish through `IHubContext<NotificationsHub>` indirection (Story 7.2 onward), keeping slices unaware of SignalR plumbing. NetArchTest from Story 4.6 (no `Microsoft.AspNetCore.SignalR` reference under `Slices/PublicStatements/`) remains green; this story registers the reference under `Hubs/` only.

### Architecture compliance

- [epics.md Epic 7 Story 7.1](../planning-artifacts/epics.md) — story definition
- [architecture.md §3.5](../planning-artifacts/architecture.md) — `ProcessingActivity` ambient context
- [architecture.md §4.5 D4.13](../planning-artifacts/architecture.md) — Signals + rxResource (consumed by Story 7.2)
- [architecture.md §4.8](../planning-artifacts/architecture.md) — AR-050 cost ceiling extension
- [Story 0.17](./0-17-cost-circuit-breaker-baseline.md) — emergency-shutdown env flag integration
- [Story 0.14](./0-14-phase-a-telemetry-primitive.md) — `ITelemetryEmitter`
- [prd.md FR51, NFR-I4](../planning-artifacts/prd.md) — real-time + reconnection requirements

### Library / framework requirements

| Layer | Package | Version | Notes |
|---|---|---|---|
| Backend | Microsoft.AspNetCore.SignalR | bundled with .NET 10 | First-party; no third-party hub library |
| Frontend | @microsoft/signalr | latest stable | Pinned to server version |
| Tests | Microsoft.AspNetCore.SignalR.Client | latest stable | Used in `WebApplicationFactory`-based integration tests |

### Testing requirements

- **Multi-tab broadcast assertion** — two SignalR client connections opened from the same `subject_id`; broadcast to `subject:{X}` group reaches both. This catches the regression where someone "optimizes" by tracking a single connection per subject.
- **Concurrent-cap test** — open `MaxConcurrentConnections` connections; assert next one rejected with 503. Exercise the emergency-shutdown env flag forcing the cap to 0.
- **Reconnection timing test (Playwright)** — DevTools Protocol drop the WebSocket; measure time-to-`connected`. NFR-I4 requires < 5s under typical conditions.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 0.3 — Auth0 tenant live (Bearer tokens issued)
- Story 0.7 — `ProcessingActivity` ambient context
- Story 0.14 — `ITelemetryEmitter` primitive
- Story 0.17 — `FAKTUBOH_EMERGENCY_SHUTDOWN` env flag
- Story 4.7 — Passive last-confirmation date display (the 60s outage fallback)

**Not yet landed (Story 7.1 tolerates absence):**
- Stories 7.2–7.6 — these consume the hub. Story 7.1 ships with no domain-event publishers wired (just the hub + auth + cap + reconnection). Story 7.2 wires the first real publisher.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.1
- [_bmad-output/planning-artifacts/architecture.md §3.5, §4.5, §4.8](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/prd.md FR51, NFR-I4](../planning-artifacts/prd.md)
- [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- [Story 0.17 emergency-shutdown](./0-17-cost-circuit-breaker-baseline.md)
- [Story 0.14 telemetry](./0-14-phase-a-telemetry-primitive.md)
- [Story 4.7 passive last-confirmation](./4-7-owner-sees-passive-last-confirmation-date-per-contact.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
