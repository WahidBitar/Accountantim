# Story 7.2: Real-time notification on counterparty confirmation

Status: ready-for-dev

## Story

As an **owner who shared a statement**,
I want to **receive a real-time push the moment the recipient taps "Confirm,"**
so that **I see "Last reconciled: [just now]" instantly rather than on next session** (FR50) [Source: epics.md Epic 7 Story 7.2; prd.md FR50, NFR-P9].

**Covers requirements:** Epic 7 — FR50 (real-time confirmation notification), NFR-P9 (1s push SLA), UX-DR12 ReconciliationMeta `pending` → `reconciled` reactive update [Source: epics.md Epic 7 Story 7.2; ux-design-specification.md UX-DR12].

**Why this story now:** Story 7.1 just shipped the hub. Story 4.6 already wires the recipient confirmation endpoint and emits a domain event. This story is the wire between the two: a Wolverine handler that consumes Story 4.6's event and broadcasts via the hub from Story 7.1.

## Acceptance Criteria

### AC1 — Wolverine handler publishes `StatementConfirmed` to owner's hub group within 1s

**Given** Story 4.6's `POST /v1/statements/t/{token}/confirm` writes the confirmation + emits a `StatementConfirmedEvent` integration event
**When** the event is consumed by `BroadcastStatementConfirmedToOwner` Wolverine handler
**Then** the handler resolves the owner's `subject_id` via the statement's `owner_subject_id` column
**And** calls `_hubContext.Clients.Group($"subject:{ownerSubjectId}").SendAsync("StatementConfirmed", payload)` where `payload = StatementConfirmedPushPayload(statementId, contactId, confirmedAt, denomination)`
**And** measured end-to-end (recipient POST → hub broadcast received by owner SPA), p95 < 1000ms per NFR-P9 against the Testcontainers Postgres + in-process Wolverine test bus
**And** the broadcast occurs only when at least one connection is registered for the owner's subject (skip silently if owner is offline) [Source: epics.md Story 7.2; prd.md NFR-P9].

### AC2 — Push payload schema is PII-safe

**Given** push payloads cross from server to browser via WebSocket
**When** `StatementConfirmedPushPayload` is defined
**Then** the record contains only:
- `statementId: Guid` — opaque
- `contactId: Guid` — opaque (owner's contact reference)
- `confirmedAt: DateTimeOffset` — ISO 8601
- `denomination: string` — ISO 4217 code or metal symbol
**And** does NOT contain: contact name, recipient IP, recipient user-agent, or any PII
**And** NetArchTest asserts no payload type used by `Hubs/NotificationsHub` includes a forbidden PII property name [Source: prd.md NFR-S8; architecture.md §2.9].

### AC3 — `ReconciliationMeta` updates reactively from `pending` to `reconciled`

**Given** a contact detail page or dashboard tile renders `ReconciliationMeta` for a statement currently in `pending` state
**When** `StatementConfirmed` arrives via the hub
**Then** an Angular signal `confirmationStateByStatementId: WritableSignal<Map<Guid, ConfirmationState>>` is mutated to set the matching statement to `{ state: 'reconciled', confirmedAt }`
**And** the `ReconciliationMeta` component (consumed unchanged from `libs/domain-ui` per Story 4.6 + UX-DR12) re-renders via signal subscription
**And** the icon flips from `clock-outline` (pending) to `checkmark-shield` (reconciled) with a 200ms ease-out fade — not a sudden swap
**And** an `aria-live="polite"` region announces "Statement with [contact name] confirmed just now" for screen readers
**And** the user sees no manual refresh; no toast; the row IS the feedback per silent-success UX [Source: ux-design-specification.md UX-DR12, UX-DR27].

### AC4 — Bilingual `aria-live` announcement via Transloco

**Given** the announcement is locale-aware
**When** the locale is `ar`
**Then** the live-region text is "تم تأكيد الكشف مع [اسم جهة الاتصال] الآن"
**And** when the locale is `en`, the text is "Statement with [contact name] confirmed just now"
**And** Transloco keys `realtime.statement_confirmed_announcement` cover both
**And** an Arabic name correctly bidi-renders inside an LTR app context (or vice versa) — verified via Story 0.12 RTL acceptance gate harness [Source: prd.md NFR-A9].

### AC5 — Idempotent — duplicate event delivery does not flicker UI

**Given** Wolverine guarantees at-least-once, not exactly-once
**When** a duplicate `StatementConfirmed` payload arrives for a statement already in `reconciled` state
**Then** the signal mutation is a no-op (compare confirmedAt; skip if equal)
**And** no animation re-fires; no aria-live re-announces; no observable side effect to the user
**And** a Vitest unit test asserts the no-op behavior on duplicate arrival [Source: epics.md Story 7.2 implicit; Wolverine semantics].

### AC6 — Offline owner falls back to passive display per Story 4.7

**Given** the owner's SPA has `connectionState !== 'connected'` when the recipient confirms
**When** the broadcast attempt finds no active connection in the registry
**Then** the broadcast is silently skipped (no error, no retry queue)
**And** on next owner session load, the contact list / dashboard reads `confirmedAt` from the persisted statement row per Story 4.7 passive display
**And** an integration test asserts: offline owner + recipient confirms → no error in logs → owner reload shows confirmation [Source: epics.md Story 7.2 AC fallback; Story 4.7].

### AC7 — Telemetry `realtime_confirmation_pushed` emits

**Given** AR-047 telemetry primitive
**When** the broadcast succeeds (i.e., at least one connection received it)
**Then** `TelemetryEmitter.Emit("realtime_confirmation_pushed", new RealtimeConfirmationPushedTelemetry(opaque_owner_id, contact_id_hash, latency_ms_bucket))` fires
**And** `latency_ms_bucket` is one of `"<100ms" | "<500ms" | "<1s" | ">1s"` — bucketed to avoid a high-cardinality metric
**And** if no connection received it (owner offline), `realtime_confirmation_skipped_offline` emits instead (telemetry still proves the event flow ran end-to-end) [Source: Story 0.14].

### AC8 — End-to-end integration test asserts the full chain

**Given** an integration test environment with Testcontainers Postgres + real Wolverine in-process bus + real SignalR test client
**When** the test:
1. Creates owner subject + contact + statement
2. Connects a SignalR client as the owner subject
3. Issues `POST /v1/statements/t/{token}/confirm` from a separate test client
**Then** the SignalR client receives `StatementConfirmed` with the matching `statementId` within 1000ms
**And** the test asserts payload shape matches `StatementConfirmedPushPayload`
**And** a second connection (multi-tab) for the same owner also receives the broadcast [Source: Story 4.6; Story 7.1 AC2].

## Tasks / Subtasks

### Backend — Wolverine handler

- [ ] **T1 (AC: 1, 2)** — Create `BroadcastStatementConfirmedToOwner` Wolverine handler in `backend/src/Faktuboh.Api/Slices/Statements/Projections/`
  - [ ] Method signature: `public async Task Handle(StatementConfirmedEvent evt, IHubContext<NotificationsHub> hubContext, IStatementRepository statements)`
  - [ ] Resolves owner subject_id via `statements.GetOwnerSubjectIdAsync(evt.StatementId)`
  - [ ] Constructs `StatementConfirmedPushPayload`
  - [ ] Calls `await hubContext.Clients.Group($"subject:{ownerSubjectId}").SendAsync("StatementConfirmed", payload)`
  - [ ] On handler exception, logs at WARN + does not throw (broadcast failure must not retry — at-least-once would amplify into spam)

### Backend — Payload contract

- [ ] **T2 (AC: 2)** — Create `StatementConfirmedPushPayload` record in `backend/src/Faktuboh.Application/Realtime/Payloads/`
  - [ ] Sealed record: `(Guid StatementId, Guid ContactId, DateTimeOffset ConfirmedAt, string Denomination)`
  - [ ] Stamped with attribute `[RealtimePushPayload]` (marker for NetArchTest scan)
- [ ] **T3 (AC: 2)** — NetArchTest in `tests/Faktuboh.ArchitectureTests/RealtimePayloadsTests.cs`
  - [ ] Scans every `[RealtimePushPayload]`-decorated type
  - [ ] Asserts no property name matches forbidden PII patterns from Story 0.14 `PiiGuard`

### Backend — Telemetry

- [ ] **T4 (AC: 7)** — Define `RealtimeConfirmationPushedTelemetry` + `RealtimeConfirmationSkippedOfflineTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T5 (AC: 7)** — Wire emission in the handler — measure latency from `evt.ConfirmedAt` to `now()` for `latency_ms_bucket`

### Backend — Owner-offline detection

- [ ] **T6 (AC: 6)** — Before broadcast, check `IConnectionRegistry.GetForSubjectAsync(ownerSubjectId)` — if empty, emit `realtime_confirmation_skipped_offline` and return without calling `SendAsync`

### Frontend — Hub event subscription

- [ ] **T7 (AC: 3, 5)** — Extend `notifications-hub.service.ts` (Story 7.1) to expose `statementConfirmed$: Observable<StatementConfirmedPushPayload>`
  - [ ] Subscribes via `hubConnection.on("StatementConfirmed", payload => subject.next(payload))`
  - [ ] Typed payload via generated TS contracts (NSwag) or hand-mirrored type
- [ ] **T8 (AC: 3, 5)** — Create `apps/owner-workspace/src/app/core/realtime/confirmation-state.service.ts`
  - [ ] Holds `confirmationStateByStatementId: WritableSignal<Map<string, { state: 'reconciled' | 'pending'; confirmedAt: string | null }>>`
  - [ ] Subscribes to `notificationsHub.statementConfirmed$`
  - [ ] On event, calls `update()` to set the entry — guards against duplicate (compare `confirmedAt`)
  - [ ] Exposes `getStateFor(statementId): Signal<ConfirmationState>` derived signal for component consumption

### Frontend — Component integration

- [ ] **T9 (AC: 3)** — Update existing `ReconciliationMeta` consumer (contact detail page + dashboard tile) to read state from `confirmation-state.service.getStateFor(statementId)`
  - [ ] Animation: 200ms ease-out fade between icon states (Tailwind `transition-opacity duration-200`)
  - [ ] No manual subscription — pure signal consumption
- [ ] **T10 (AC: 4)** — Create `apps/owner-workspace/src/app/core/realtime/aria-live-announcer.component.ts`
  - [ ] Standalone, OnPush
  - [ ] Renders a visually-hidden `<div aria-live="polite" aria-atomic="true">{{message()}}</div>`
  - [ ] Subscribes to `confirmation-state.service` events
  - [ ] Composes the announcement via Transloco key `realtime.statement_confirmed_announcement` with `{ contactName }` interpolation

### Tests — Backend

- [ ] **T11 (AC: 1, 6, 8)** — Integration test in `tests/Faktuboh.Api.Tests/Slices/Statements/RealtimeConfirmationTests.cs`
  - [ ] Setup: owner subject + contact + statement + SignalR client connected as owner
  - [ ] Trigger: separate HTTP client posts to confirm endpoint
  - [ ] Assert: SignalR client receives `StatementConfirmed` within 1000ms
  - [ ] Multi-tab variant: 2 SignalR clients for same subject; both receive
  - [ ] Offline variant: no SignalR client; assert `realtime_confirmation_skipped_offline` event emitted; no exception
- [ ] **T12 (AC: 5)** — Unit test asserting duplicate event delivery does NOT cause two broadcasts (handler is idempotent — natural-key on `statementId` + `confirmedAt`)
- [ ] **T13 (AC: 7)** — Telemetry assertion via `AssertEventEmitted("realtime_confirmation_pushed")` in the integration test

### Tests — Frontend

- [ ] **T14 (AC: 3, 5)** — Vitest unit test for `confirmation-state.service.ts`
  - [ ] Emit a `StatementConfirmedPushPayload`; assert signal updates
  - [ ] Emit duplicate; assert no second mutation
- [ ] **T15 (AC: 3)** — Component test for `ReconciliationMeta` consumer using mocked `confirmation-state.service`
  - [ ] Initial state pending; assert clock-outline icon
  - [ ] Service emits reconciled state; assert checkmark-shield icon after 200ms
- [ ] **T16 (AC: 4)** — RTL parity test (Arabic locale) for `aria-live-announcer` — assert correct localized message renders
- [ ] **T17 (AC: 1)** — Playwright E2E `apps/owner-workspace/e2e/realtime/confirmation-push.spec.ts`
  - [ ] Owner SPA loaded; recipient (separate browser context) confirms statement
  - [ ] Assert ReconciliationMeta on owner side updates within 1s without page reload

## Dev Notes

### Critical guardrails (do not violate)

1. **Broadcast failure does NOT retry.** SignalR push is best-effort; if a connection drops mid-broadcast, the next session load picks up the persisted state via Story 4.7 passive display. Wolverine retry on the handler exception would amplify into spam during outages.
2. **No PII in push payloads.** `contactName` is conspicuously absent — the SPA already has it from its own context (the contact list). Pushing it would leak across tabs that may be shared (e.g., desktop screen-sharing). NetArchTest enforces.
3. **Idempotent UI mutation.** Duplicate `StatementConfirmed` events would re-fire animation + aria-live. Compare `confirmedAt` before mutating; skip on equal.
4. **Owner-offline path emits its own telemetry.** Without `realtime_confirmation_skipped_offline`, we cannot distinguish "real-time worked" from "owner was offline" in App Insights — both look like event-not-fired bugs.
5. **Broadcast is owner-scoped only.** Never broadcast to contact / recipient / any other subject. The recipient already has their own UX (the public statement page transitions on its own client-side state per Story 4.6).
6. **Push payload contract is the wire format.** Generated TS contracts (NSwag) or hand-mirrored types must stay in sync with `StatementConfirmedPushPayload`. Schema drift is a silent UI break — consider a snapshot test pinning the JSON shape.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/
│   │       └── Statements/
│   │           └── Projections/
│   │               └── BroadcastStatementConfirmedToOwner.cs    # NEW (Wolverine handler)
│   ├── Faktuboh.Application/
│   │   ├── Realtime/Payloads/
│   │   │   └── StatementConfirmedPushPayload.cs                 # NEW
│   │   ├── Realtime/Attributes/
│   │   │   └── RealtimePushPayloadAttribute.cs                  # NEW (marker)
│   │   └── Telemetry/Events/
│   │       ├── RealtimeConfirmationPushedTelemetry.cs           # NEW
│   │       └── RealtimeConfirmationSkippedOfflineTelemetry.cs   # NEW
└── tests/
    ├── Faktuboh.Api.Tests/Slices/Statements/
    │   └── RealtimeConfirmationTests.cs                         # NEW (Integration)
    └── Faktuboh.ArchitectureTests/
        └── RealtimePayloadsTests.cs                             # NEW

frontend/
└── apps/owner-workspace/src/app/core/realtime/
    ├── confirmation-state.service.ts                            # NEW
    ├── confirmation-state.service.spec.ts                       # NEW
    ├── aria-live-announcer.component.ts                         # NEW
    ├── aria-live-announcer.component.html                       # NEW
    └── notifications-hub.service.ts                             # EDIT (add statementConfirmed$ observable)
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + NSubstitute (handler resolution) |
| Integration (BE) | `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine + `Microsoft.AspNetCore.SignalR.Client` |
| Unit (FE) | Vitest + Angular signal testing utilities |
| E2E | Playwright multi-context (owner page + recipient page) |

### Project Structure Notes

The Wolverine handler lives under `Slices/Statements/Projections/` because it consumes a Statements-domain event. Per slice-isolation discipline, the handler may inject `IHubContext<NotificationsHub>` (a transport, not a slice type) but may NOT reference any other slice's domain types. The push payload uses raw `Guid` for `StatementId` + `ContactId` — no `Contact` aggregate import.

### Architecture compliance

- [epics.md Epic 7 Story 7.2](../planning-artifacts/epics.md) — story definition
- [architecture.md §4.5 D4.13](../planning-artifacts/architecture.md) — Signals + rxResource
- [ux-design-specification.md UX-DR12](../planning-artifacts/ux-design-specification.md) — ReconciliationMeta state
- [ux-design-specification.md UX-DR27](../planning-artifacts/ux-design-specification.md) — silent success
- [prd.md FR50, NFR-P9, NFR-S8](../planning-artifacts/prd.md)
- [Story 7.1](./7-1-signalr-hub-infrastructure.md) — hub infrastructure
- [Story 4.6](./4-6-recipient-confirm-button-timestamps-agreement.md) — confirmation event source
- [Story 4.7](./4-7-owner-sees-passive-last-confirmation-date-per-contact.md) — fallback display

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Microsoft.AspNetCore.SignalR (`IHubContext`) | Inject into Wolverine handler |
| Frontend | @microsoft/signalr | Already added in Story 7.1 |

### Testing requirements

- **End-to-end latency assertion (< 1000ms p95).** Measured at the SignalR client `on("StatementConfirmed")` callback inside the integration test using `WebApplicationFactory`. If the test fails on a CI runner under load, raise the assertion threshold to 1500ms but log a regression flag.
- **Multi-tab broadcast** — confirms Story 7.1's group-based dispatch works in practice for this story's payload shape.
- **Offline owner path** — without this test, the offline fallback could regress silently (handler throws, retries, eventually DLQs — all invisible to the user but visible in DLQ inspection). Story 8.7's daily integrity job is too coarse-grained to catch this.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 4.6 — recipient confirmation endpoint + `StatementConfirmedEvent` integration event
- Story 4.7 — passive last-confirmation date display (offline fallback)
- Story 7.1 — SignalR hub + `IConnectionRegistry`
- Story 0.14 — `ITelemetryEmitter`

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.2
- [_bmad-output/planning-artifacts/architecture.md §4.5](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/ux-design-specification.md UX-DR12, UX-DR27](../planning-artifacts/ux-design-specification.md)
- [_bmad-output/planning-artifacts/prd.md FR50, NFR-P9, NFR-S8, NFR-A9](../planning-artifacts/prd.md)
- [Story 7.1 SignalR hub](./7-1-signalr-hub-infrastructure.md)
- [Story 4.6 confirmation endpoint](./4-6-recipient-confirm-button-timestamps-agreement.md)
- [Story 4.7 passive display](./4-7-owner-sees-passive-last-confirmation-date-per-contact.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
