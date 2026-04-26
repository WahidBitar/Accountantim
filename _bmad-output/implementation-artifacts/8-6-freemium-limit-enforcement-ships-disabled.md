# Story 8.6: Freemium limit enforcement (ships disabled; activates via Revenue-readiness)

Status: ready-for-dev

## Story

As **Wahid**,
I want **the freemium enforcement code path implemented and tested but dormant (`enforcement_enabled=false`) until the Revenue-readiness trigger fires**,
so that **viral growth is not throttled pre-revenue** (FR48) [Source: epics.md Epic 8 Story 8.6; prd.md FR48, freemium enforcement timing].

**Covers requirements:** Epic 8 — FR48 (freemium enforcement), Critical Behavioral Flow #3 (paywall gates creation, not access), Phase-A revenue-readiness trigger seam, integration with Story 8.2 thresholds + Story 8.3 grandfathering [Source: epics.md Story 8.6; prd.md].

**Why this story now:** Lands the enforcement plumbing BEFORE the configuration UI (Story 8.2) + grandfathering UI (Story 8.3). The enforcement path must exist + be tested first; the UIs that toggle it come after. Sequencing this before 8.2/8.3 means those stories can hit a real, tested gate.

## Acceptance Criteria

### AC1 — `freemium_config` table

**Given** thresholds + master toggle are runtime-configurable per delegated decisions
**When** migration `YYYYMMDDHHMMSS_AddFreemiumConfigTable.cs` lands
**Then** the table:
```sql
CREATE TABLE freemium_config (
    id smallint PRIMARY KEY DEFAULT 1 CHECK (id = 1),  -- single-row table
    enforcement_enabled boolean NOT NULL DEFAULT false,
    transactions_per_user_limit integer NOT NULL DEFAULT 500,
    contacts_per_user_limit integer NOT NULL DEFAULT 10,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_subject_id uuid NULL
);
INSERT INTO freemium_config (id, enforcement_enabled, transactions_per_user_limit, contacts_per_user_limit)
    VALUES (1, false, 500, 10);
```
**And** the `CHECK (id = 1)` constraint enforces single-row semantics — there's only one global config
**And** the seed INSERT runs at migration time so the row always exists
**And** the table is bitemporal (`IBitemporal`) — every config change has a history row [Source: delegated decisions; ADR-022].

### AC2 — `IFreemiumConfigProvider` runtime config reader

**Given** the gate must read the config on every gated request without per-request DB hit
**When** `IFreemiumConfigProvider` is implemented
**Then** the provider:
- Reads the row on application startup
- Caches in memory with `IMemoryCache` (10-minute TTL)
- Exposes typed properties: `EnforcementEnabled: bool`, `TransactionsPerUserLimit: int`, `ContactsPerUserLimit: int`
- Provides `InvalidateAsync()` method called by Story 8.2 PATCH endpoint after config update
**And** the cache miss is rare (only every 10 min) — DB load is negligible [Source: epics.md Story 8.6 AC enforcement runtime; Story 8.2].

### AC3 — Contact-creation gate

**Given** Story 2.1 endpoint `POST /v1/contacts` creates a contact
**When** the gate is invoked at the start of the handler
**Then** the gate:
1. Reads `enforcement_enabled` from `IFreemiumConfigProvider`
2. If `false`, no-op — request continues
3. If `true`, checks user's grandfathering status from Story 8.3 — if grandfathered, no-op
4. If `true` AND not grandfathered, counts `contacts WHERE owner_subject_id = ?` 
5. If count >= `ContactsPerUserLimit`, returns 403 RFC 9457 `faktuboh.freemium.contacts_limit_exceeded` with body containing the limit + upgrade prompt
**And** the gate query is fast (count uses index on `contacts(owner_subject_id)`)
**And** integration test covers both `enforcement_enabled=true` AND `enforcement_enabled=false` paths per AC requirement [Source: epics.md Story 8.6 AC; Story 8.3].

### AC4 — Debt-recording gate

**Given** Story 3.1 endpoint `POST /v1/contacts/{id}/debts` records a debt
**When** the gate is invoked
**Then** the same enforcement-disabled / grandfathered / count-and-block flow applies
**And** the limit is `TransactionsPerUserLimit` (default 500); count is over `journal_entries WHERE owner_subject_id = ?` (a "transaction" = a journal entry)
**And** error code: `faktuboh.freemium.transactions_limit_exceeded` (403) [Source: epics.md Story 8.6 AC; Story 3.1].

### AC5 — Non-blocking 90% banner

**Given** the user is approaching the limit
**When** the user's count is at 90% or higher of the configured limit (e.g., 450/500 transactions)
**Then** a non-blocking banner renders at the top of the dashboard: "You're approaching your free tier limit (450/500 transactions). Upgrade to Pro for unlimited."
**And** the banner uses `<p-message severity="info">` — informational, not warning
**And** the banner is visible regardless of `enforcement_enabled` value (the UI is implemented + tested even though enforcement is dormant)
**And** when `enforcement_enabled=false`, the banner is technically visible but de-prioritized — at MVP, we suppress the banner entirely while disabled (UI tested but silent — per AC explicit requirement)
**And** when `enforcement_enabled=true`, the banner shows; tapping a link opens an upgrade-prompt modal (Phase-2 content; MVP shows static "Upgrade coming soon" copy) [Source: epics.md Story 8.6 AC banner; PRD freemium UX].

### AC6 — Upgrade-prompt modal on hard-block

**Given** enforcement is enabled + the user hit the limit
**When** the gate returns 403
**Then** the SPA's error handler interprets `faktuboh.freemium.{contacts|transactions}_limit_exceeded` and opens an `<p-dialog>` with:
- Header: "You've reached the free tier limit"
- Body: "Upgrade to Pro ($2.50/month or $20/year) to unlock unlimited [contacts|transactions]." (Transloco)
- Primary button: "Upgrade to Pro" — at MVP, navigates to a "coming soon" page; Phase-2 wires Stripe checkout
- Secondary button: "Maybe later" — closes the dialog
**And** the user remains on their current page; no destructive nav
**And** if user dismisses, the failed action does NOT auto-retry (intentional — they need to upgrade first)
**And** the dialog is bilingual + axe-core compliant [Source: epics.md Story 8.6 AC upgrade prompt].

### AC7 — Viewing/sharing/exporting NEVER restricted

**Given** PRD Critical Behavioral Flow #3: paywall gates creation, not access
**When** any read or share endpoint is called (GET contacts, GET debts, POST statements, POST CSV exports)
**Then** the freemium gate is NOT invoked
**And** an integration test asserts: user above limit can still view all contacts, list debts, generate statement links, export CSVs
**And** NetArchTest verifies: only `POST /v1/contacts` + `POST /v1/contacts/{id}/debts` import `IFreemiumGate`; no read endpoints do [Source: prd.md Critical Behavioral Flow #3; epics.md Story 8.6 AC].

### AC8 — `IFreemiumGate` interface

**Given** the gate must be invokable from any creation endpoint
**When** `IFreemiumGate` is defined in `Faktuboh.Application/Freemium/`
**Then** the interface:
```csharp
public interface IFreemiumGate
{
    Task<FreemiumGateOutcome> CheckContactCreationAsync(Guid ownerSubjectId, CancellationToken ct);
    Task<FreemiumGateOutcome> CheckDebtCreationAsync(Guid ownerSubjectId, CancellationToken ct);
}

public abstract record FreemiumGateOutcome
{
    public sealed record Allowed : FreemiumGateOutcome;
    public sealed record Blocked(int CurrentCount, int Limit, string LimitKind) : FreemiumGateOutcome;
    public sealed record Approaching(int CurrentCount, int Limit, string LimitKind) : FreemiumGateOutcome;
}
```
**And** the discriminated union enables endpoints to handle 90%-banner trigger + hard-block uniformly [Source: design decision aligned with vertical-slice patterns].

### AC9 — Telemetry — `freemium_gate_checked` + `freemium_limit_hit`

**Given** AR-047 telemetry primitive
**When** events fire
**Then** events emit:
- `freemium_gate_checked` (every check, whether passes or blocks) — `{ opaque_user_id, gate_kind: "contact" | "debt", outcome: "allowed" | "approaching" | "blocked", enforcement_enabled }`
- `freemium_limit_hit` (only on blocks) — `{ opaque_user_id, limit_kind, current_count_bucket: "<10" | "<100" | "<500" | ">500" }`
**And** PiiGuard validates [Source: Story 0.14].

### AC10 — Both enforcement-states integration-tested

**Given** the AC explicit requirement
**When** integration tests run
**Then** every gate-related test variant covers BOTH `enforcement_enabled=true` AND `enforcement_enabled=false`
**And** the test fixture has a helper `WithEnforcementEnabled(bool enabled, Action act)` that sets the config + invalidates the provider cache + runs the test body + restores
**And** test coverage includes:
- Disabled + over-limit → contact created + telemetry shows `outcome: "blocked"` would be returned but `enforcement_enabled: false` (informational)
- Wait, this needs reconciliation — when disabled, the gate returns `Allowed` regardless. Telemetry fires `freemium_gate_checked` with `outcome: "allowed"` always, but `freemium_limit_hit` would fire on the simulated block-condition for telemetry visibility into "would have blocked" counts. Document this as an explicit design choice.
- Enabled + under-limit → contact created
- Enabled + over-limit → 403 + telemetry
- Grandfathered + over-limit → contact created (grandfathered users exempt) [Source: epics.md Story 8.6 AC integration tests both paths].

## Tasks / Subtasks

### Backend — Migration + table

- [ ] **T1 (AC: 1)** — Create migration `YYYYMMDDHHMMSS_AddFreemiumConfigTable.cs` per AC1
  - [ ] Single-row table + bitemporal history per ADR-022
  - [ ] Seed INSERT for the single row

### Backend — Config provider

- [ ] **T2 (AC: 2)** — Define `IFreemiumConfigProvider` + `FreemiumConfigProvider` in `Faktuboh.Application/Freemium/`
  - [ ] Cached read with 10-min TTL via `IMemoryCache`
  - [ ] `InvalidateAsync()` for Story 8.2 PATCH endpoint to call

### Backend — Gate interface + implementation

- [ ] **T3 (AC: 8)** — Define `IFreemiumGate` + `FreemiumGateOutcome` discriminated-union per AC8
- [ ] **T4 (AC: 3, 4)** — Implement `FreemiumGate` in `Faktuboh.Infrastructure/Freemium/`
  - [ ] `CheckContactCreationAsync` per AC3
  - [ ] `CheckDebtCreationAsync` per AC4
  - [ ] Reads grandfathering status from Story 8.3's `IGrandfatheringRepository` (placeholder if Story 8.3 hasn't shipped — assumes `IsGrandfatheredAsync(userId): Task<bool>` interface exists)
  - [ ] If Story 8.3 not yet implemented, default `IsGrandfatheredAsync` returns false (no users grandfathered until 8.3 ships)

### Backend — Wire gate into endpoints

- [ ] **T5 (AC: 3)** — Inject `IFreemiumGate` into Story 2.1's `CreateContactHandler`
  - [ ] Call before any persistence
  - [ ] On `Blocked`, return 403 RFC 9457
  - [ ] On `Approaching`, attach approaching-state info to response (consumed by FE for banner state)
- [ ] **T6 (AC: 4)** — Inject `IFreemiumGate` into Story 3.1's debt-creation handler
  - [ ] Same pattern

### Backend — Error catalog

- [ ] **T7 (AC: 3, 4)** — Register `faktuboh.freemium.contacts_limit_exceeded` (403) + `faktuboh.freemium.transactions_limit_exceeded` (403) in `ErrorCatalog`

### Backend — Telemetry

- [ ] **T8 (AC: 9)** — Define `FreemiumGateCheckedTelemetry` + `FreemiumLimitHitTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T9 (AC: 9)** — Wire emission in `FreemiumGate`

### Backend — Architecture tests

- [ ] **T10 (AC: 7)** — NetArchTest: only `CreateContactHandler` + debt-creation handler import `IFreemiumGate`
  - [ ] No read endpoint imports the gate
  - [ ] Verifies the "paywall gates creation, not access" invariant

### Frontend — Banner + dialog

- [ ] **T11 (AC: 5)** — Create `apps/owner-workspace/src/app/core/freemium/approaching-limit-banner.component.ts`
  - [ ] Standalone, OnPush
  - [ ] Subscribes to a `freemium-state.service` signal
  - [ ] Renders `<p-message severity="info">` with limit-state copy
  - [ ] Hidden when `enforcement_enabled=false` (read from a config-state signal)
- [ ] **T12 (AC: 6)** — Create `apps/owner-workspace/src/app/core/freemium/upgrade-prompt-dialog.component.ts`
  - [ ] Triggered by error handler on 403 with freemium codes
  - [ ] Bilingual copy via Transloco
  - [ ] "Upgrade to Pro" button navigates to `/upgrade-coming-soon` (placeholder route — Phase-2 wires Stripe)

### Frontend — State service

- [ ] **T13 (AC: 5, 6)** — Create `apps/owner-workspace/src/app/core/freemium/freemium-state.service.ts`
  - [ ] Tracks current count + limit + enforcement-enabled
  - [ ] Updated by API responses (the `Approaching` outcome attached to creation responses)
  - [ ] Exposes `bannerVisible: Signal<boolean>` derived signal

### Tests — Backend

- [ ] **T14 (AC: 1, 2)** — Integration test for config provider
  - [ ] Initial value matches seed (false, 500, 10)
  - [ ] Update via direct DB write + invalidate → next read reflects update
- [ ] **T15 (AC: 3, 10)** — Integration tests for contact-creation gate (both enforcement states)
  - [ ] `enforcement_enabled=false` + 100 existing contacts → contact created (no enforcement)
  - [ ] `enforcement_enabled=true` + 9 contacts → contact created (under limit) + `Approaching` returned at 9 ≥ 0.9 * 10
  - [ ] `enforcement_enabled=true` + 10 contacts → 403 `faktuboh.freemium.contacts_limit_exceeded`
  - [ ] `enforcement_enabled=true` + grandfathered + 100 contacts → contact created
- [ ] **T16 (AC: 4, 10)** — Integration tests for debt-creation gate (mirror T15 for transactions limit 500)
- [ ] **T17 (AC: 7)** — Integration test for view/share/export not restricted
  - [ ] User with 1000 contacts (over limit) + enforcement_enabled=true → can still GET /v1/contacts, GET /v1/debts, POST /v1/contacts/{id}/statements, GET /v1/exports/csv
- [ ] **T18 (AC: 9, 10)** — Telemetry assertions for both states + outcomes

### Tests — Frontend

- [ ] **T19 (AC: 5)** — Vitest test for approaching-limit banner
  - [ ] Hidden when enforcement_enabled=false
  - [ ] Visible when enforcement_enabled=true + counts at 90%+
- [ ] **T20 (AC: 6)** — Vitest test for upgrade-prompt dialog
  - [ ] Triggered by 403 with freemium code
  - [ ] Buttons render bilingually
- [ ] **T21 (AC: 6)** — Playwright E2E: trigger over-limit + assert dialog appears + tap "Maybe later" + assert dialog closes + user remains on page

## Dev Notes

### Critical guardrails (do not violate)

1. **Both enforcement states are integration-tested.** Per the AC explicit requirement. Without it, flipping `enforcement_enabled=true` post-launch could surface latent bugs at the worst time.
2. **Paywall gates creation, NOT access.** Per PRD Critical Behavioral Flow #3. NetArchTest enforces — only creation endpoints import the gate.
3. **Single-row config table.** `CHECK (id = 1)` prevents accidental multi-row configs. The seed INSERT ensures the row always exists.
4. **10-minute cache TTL on the config provider.** Eliminates per-request DB hit. Story 8.2's PATCH endpoint MUST call `InvalidateAsync()` for changes to propagate immediately.
5. **Discriminated-union outcome.** `Allowed | Blocked | Approaching` enables uniform handling of banner-trigger + hard-block at the endpoint layer.
6. **Grandfathering bypass before count check.** Cheapest check first. Once grandfathered, count query is skipped.
7. **Banner suppressed entirely while disabled at MVP.** Per AC5 explicit clarification — UI is implemented + tested but silent until `enforcement_enabled=true`. Avoid surprising disabled-state users with limit messaging.
8. **Upgrade-prompt at MVP is a placeholder.** Phase-2 wires Stripe. The placeholder copy + route prevent the dialog from being a dead-end.
9. **Telemetry fires on EVERY gate check.** Even when disabled, telemetry shows what enforcement WOULD do — gives Wahid pre-launch visibility into impact.
10. **Bitemporal history on `freemium_config`.** Every config change recorded — vital for audit + debugging "when did the limit change" questions.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/
│   │       ├── Contacts/Create/CreateContactHandler.cs         # MODIFY (inject gate)
│   │       └── Debts/Create/CreateDebtHandler.cs               # MODIFY (inject gate)
│   ├── Faktuboh.Application/
│   │   ├── Freemium/
│   │   │   ├── IFreemiumConfigProvider.cs                      # NEW
│   │   │   ├── FreemiumConfigProvider.cs                       # NEW
│   │   │   ├── IFreemiumGate.cs                                # NEW
│   │   │   └── FreemiumGateOutcome.cs                          # NEW (discriminated union)
│   │   ├── Telemetry/Events/
│   │   │   ├── FreemiumGateCheckedTelemetry.cs                 # NEW
│   │   │   └── FreemiumLimitHitTelemetry.cs                    # NEW
│   │   └── Errors/ErrorCatalog.cs                              # MODIFY (+2 codes)
│   ├── Faktuboh.Domain/Freemium/
│   │   ├── FreemiumConfig.cs                                   # NEW (entity)
│   │   └── IFreemiumConfigRepository.cs                        # NEW
│   └── Faktuboh.Infrastructure/
│       ├── Freemium/FreemiumGate.cs                            # NEW
│       ├── Persistence/
│       │   ├── Configurations/FreemiumConfigConfiguration.cs   # NEW
│       │   └── Migrations/YYYYMMDDHHMMSS_AddFreemiumConfigTable.cs # NEW
│       └── Repositories/FreemiumConfigRepository.cs            # NEW
└── tests/Faktuboh.Api.Tests/
    ├── Slices/Freemium/
    │   ├── FreemiumGateTests.cs                                # NEW (Integration)
    │   └── ConfigProviderTests.cs                              # NEW
    └── Faktuboh.ArchitectureTests/
        └── FreemiumGateTests.cs                                # NEW (no read-endpoint imports)

frontend/
└── apps/owner-workspace/src/app/core/freemium/
    ├── freemium-state.service.ts                               # NEW
    ├── approaching-limit-banner.component.ts                   # NEW
    ├── approaching-limit-banner.component.html                 # NEW
    ├── upgrade-prompt-dialog.component.ts                      # NEW
    └── upgrade-prompt-dialog.component.html                    # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + NSubstitute |
| Integration (BE) | Testcontainers Postgres + `WithEnforcementEnabled` test helper |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story scaffolds the `Slices/Freemium/`-adjacent infrastructure. Since the gate is invoked from existing slices (Contacts, Debts), it lives in `Faktuboh.Application/Freemium/` (cross-slice abstraction) + `Faktuboh.Infrastructure/Freemium/` (implementation). The slice itself has no HTTP endpoints (Story 8.2 owns the admin UI for config; Story 8.3 owns grandfathering UI). NetArchTest enforces the cross-slice gate-injection pattern.

### Architecture compliance

- [epics.md Epic 8 Story 8.6](../planning-artifacts/epics.md)
- [prd.md FR48, Critical Behavioral Flow #3, Phase-A revenue-readiness](../planning-artifacts/prd.md)
- [architecture.md ADR-022 bitemporal](../planning-artifacts/architecture.md)
- [Story 2.1 contact creation](./2-1-contacts-create-opening-balance.md) — gate consumer
- [Story 3.1 debt creation](./3-1-record-new-debt.md) — gate consumer
- [Story 8.2 admin config UI](./8-2-freemium-threshold-configuration-ui.md) — invalidator
- [Story 8.3 grandfathering](./8-3-grandfathering-rules-management.md) — bypass source

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Microsoft.Extensions.Caching.Memory | Config cache |
| Frontend | PrimeNG (Message, Dialog) | Banner + upgrade prompt |

### Testing requirements

- **Both enforcement states tested for every variant.** Without this, the dormant-state ship → Revenue-readiness flip is a high-risk transition.
- **Read-endpoint exclusion (NetArchTest)** — without this, a future PR could "helpfully" gate read endpoints and break the read-always-free contract.
- **Grandfathering bypass test** — needs a stub `IsGrandfatheredAsync = true` for setup; verifies the bypass branch.
- **Telemetry-on-disabled state** — confirms operational visibility before launch.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 2.1 — contact creation (gate consumer)
- Story 3.1 — debt creation (gate consumer)
- Story 0.14 — telemetry primitive

**This story is a prerequisite for:**
- Story 8.2 — config UI (calls `InvalidateAsync` after PATCH)
- Story 8.3 — grandfathering (provides `IsGrandfatheredAsync` source)

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.6
- [_bmad-output/planning-artifacts/prd.md FR48, Critical Behavioral Flow #3](../planning-artifacts/prd.md)
- [Story 2.1](./2-1-contacts-create-opening-balance.md)
- [Story 3.1](./3-1-record-new-debt.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
