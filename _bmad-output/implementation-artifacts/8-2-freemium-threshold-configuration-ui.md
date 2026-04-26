# Story 8.2: Freemium threshold configuration UI

Status: ready-for-dev

## Story

As **Wahid**,
I want to **configure freemium thresholds (transactions per user, contacts per user) via an admin UI**,
so that **when the Revenue-readiness trigger fires I can tune thresholds without a code deploy** (FR44) [Source: epics.md Epic 8 Story 8.2; prd.md FR44].

**Covers requirements:** Epic 8 — FR44 (admin threshold UI), NFR-S10 admin elevated auth + audit logging, integrates with Story 8.6 enforcement gate via `IFreemiumConfigProvider.InvalidateAsync` [Source: epics.md Story 8.2].

**Why this story now:** Story 8.6 just shipped the gate + the `freemium_config` table + the cached provider. This story is the admin UI on top — read + write endpoints + the page itself.

## Acceptance Criteria

### AC1 — `/admin/freemium` route renders current config

**Given** the admin navigates to `/admin/freemium` with elevated auth (per Story 8.1)
**When** the page renders
**Then** the page shows:
- Current `transactions_per_user_limit` value (default 500) — input box, integer
- Current `contacts_per_user_limit` value (default 10) — input box, integer
- Master toggle `enforcement_enabled` — `<p-toggleSwitch>` with tooltip "Enabling activates freemium gates platform-wide"
- "Save changes" button (disabled when no changes)
- Last updated by + at metadata (e.g., "Last updated by [admin email] at 2026-04-26T10:30Z" — admin email displayed since admin-investigation surfaces are admin-internal)
**And** data fetched via `GET /v1/admin/freemium-thresholds`
**And** the page is accessible only to admin role + elevated auth (Story 8.1 inheritance) [Source: epics.md Story 8.2 AC; Story 8.1].

### AC2 — `PATCH /v1/admin/freemium-thresholds` persists changes

**Given** the admin edits values + taps Save
**When** the form submits
**Then** `PATCH /v1/admin/freemium-thresholds` accepts body `{ enforcement_enabled?: bool, transactions_per_user_limit?: int, contacts_per_user_limit?: int }`
**And** the handler:
1. Validates: `transactions_per_user_limit > 0`, `contacts_per_user_limit > 0`, both must be ≤ 1,000,000 (sanity cap)
2. Updates the single `freemium_config` row (id=1)
3. Sets `updated_at = now()`, `updated_by_subject_id = admin's id`
4. Calls `IFreemiumConfigProvider.InvalidateAsync()` — next gate check reads the new values
5. Writes audit-log entry with `action = 'admin.freemium.config_updated'` + payload showing diff (before/after values)
6. Returns `200 OK` with updated config
**And** the bitemporal interceptor writes the previous row to `freemium_config_history`
**And** integration test asserts: PATCH succeeds → next contact-creation gate check uses new limit immediately [Source: epics.md Story 8.2 AC; Story 8.6 invalidate].

### AC3 — Master toggle warning UX

**Given** flipping `enforcement_enabled` from false → true is a high-impact change
**When** the admin taps the toggle to enable enforcement
**Then** a `<p-confirmDialog>` opens with:
- Header: "Enable freemium enforcement?"
- Body: "Activating enforcement will block users from creating contacts beyond [10] or transactions beyond [500] (current limits). Existing data is unaffected. Confirm to proceed."
- Confirm button: "Enable enforcement"
- Cancel button: "Cancel"
**And** flipping from true → false is silent (relaxing limits is safe)
**And** the dialog is dismissed before the toggle visually flips — toggle state reflects the persisted value, not the in-flight change [Source: ux-design-specification.md UX-DR29].

### AC4 — Validation feedback

**Given** invalid inputs
**When** the user enters `0` or negative numbers or > 1,000,000
**Then** inline validation messages appear under each input (Transloco-localized)
**And** the Save button is disabled
**And** server-side validation also rejects (defense in depth) — RFC 9457 with JSON Pointers `/transactionsPerUserLimit` etc. walked onto Signal Forms fields [Source: Story 0.4 RFC 9457 + Story 2.1 form pattern].

### AC5 — Tooltip on master toggle

**Given** the warning text per delegated decision
**When** hovering the toggle
**Then** a tooltip shows "Enabling activates freemium gates platform-wide" (Transloco)
**And** the tooltip uses PrimeNG `<p-tooltip>` directive
**And** is keyboard-accessible (focusable + visible on focus) per WCAG 2.1 AA [Source: delegated decisions; ux-design-specification.md].

### AC6 — Audit log diff payload

**Given** every config change must be auditable
**When** the audit-log row is written
**Then** the `payload` JSONB contains:
```json
{
  "before": { "enforcement_enabled": false, "transactions_per_user_limit": 500, "contacts_per_user_limit": 10 },
  "after": { "enforcement_enabled": true, "transactions_per_user_limit": 500, "contacts_per_user_limit": 10 }
}
```
**And** the diff captures only changed fields when partial PATCH (smaller payload)
**And** integration test asserts the diff structure [Source: prd.md NFR-S10; Story 3.8].

### AC7 — Telemetry — `freemium_config_updated`

**Given** AR-047 telemetry primitive
**When** config updates
**Then** `TelemetryEmitter.Emit("freemium_config_updated", new FreemiumConfigUpdatedTelemetry(opaque_admin_id, change_kind: "enabled" | "disabled" | "limits_only", ...))` fires
**And** PiiGuard validates [Source: Story 0.14].

## Tasks / Subtasks

### Backend — GET endpoint

- [ ] **T1 (AC: 1)** — Create slice action `Slices/Admin/Freemium/Get/`
  - [ ] `GetFreemiumConfigResponse.cs`
  - [ ] `GetFreemiumConfigHandler.cs`
  - [ ] Endpoint `MapGet("/v1/admin/freemium-thresholds", ...)`

### Backend — PATCH endpoint

- [ ] **T2 (AC: 2, 3, 4, 6)** — Create slice action `Slices/Admin/Freemium/Update/`
  - [ ] `UpdateFreemiumConfigRequest.cs` — `(bool? EnforcementEnabled, int? TransactionsPerUserLimit, int? ContactsPerUserLimit)`
  - [ ] `UpdateFreemiumConfigRequestValidator.cs` — limits > 0 + ≤ 1,000,000
  - [ ] `UpdateFreemiumConfigHandler.cs` — performs update + invalidate + audit-log diff
  - [ ] Endpoint `MapPatch("/v1/admin/freemium-thresholds", ...)`

### Backend — Telemetry

- [ ] **T3 (AC: 7)** — Define `FreemiumConfigUpdatedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T4 (AC: 7)** — Wire emission in handler

### Frontend — Page

- [ ] **T5 (AC: 1, 2, 4)** — Create `apps/owner-workspace/src/app/features/admin/freemium/`
  - [ ] `freemium-config.component.ts` + `.html` + `.scss`
  - [ ] Signal Form binding to current config
  - [ ] Save button enabled only on dirty form
  - [ ] Inline validation per AC4
- [ ] **T6 (AC: 3)** — Wire `<p-confirmDialog>` for false → true toggle flip
- [ ] **T7 (AC: 5)** — Wire `<p-tooltip>` on master toggle

### Frontend — API client

- [ ] **T8 (AC: 1, 2)** — Extend `admin.client.ts`
  - [ ] `getFreemiumConfig(): rxResource<FreemiumConfigResponse>`
  - [ ] `updateFreemiumConfig(body): Observable<FreemiumConfigResponse>`

### Tests — Backend

- [ ] **T9 (AC: 1)** — Integration test for GET endpoint
- [ ] **T10 (AC: 2, 6)** — Integration test for PATCH
  - [ ] Update succeeds + bitemporal history written
  - [ ] Invalidate called → next gate check uses new value
  - [ ] Audit-log diff structure correct
- [ ] **T11 (AC: 4)** — Validation tests: 0, negative, > 1M → 400
- [ ] **T12 (AC: 7)** — Telemetry assertion

### Tests — Frontend

- [ ] **T13 (AC: 1, 2, 3, 4)** — Vitest for `freemium-config.component.ts`
  - [ ] Renders current values
  - [ ] Save disabled when no changes
  - [ ] Validation errors render inline
  - [ ] Toggle false → true triggers confirm dialog
- [ ] **T14 (AC: 5)** — Axe-core: tooltip is keyboard-accessible
- [ ] **T15 (AC: 1, 2)** — Playwright E2E: edit limits + save + assert API called + page reflects new state

## Dev Notes

### Critical guardrails (do not violate)

1. **Single-row table.** PATCH always targets `id = 1`. Never INSERT another row.
2. **Master toggle UX matters.** Flipping enforcement on at the wrong moment could cause visible user-facing breakage. The confirm dialog + descriptive copy are the safety.
3. **`InvalidateAsync()` must be called.** Without it, the cached config in `IFreemiumConfigProvider` (10-min TTL) takes up to 10 minutes to reflect changes — surprising for the admin.
4. **Diff payload in audit log.** Without it, "what changed" is lost. Diff captures only changed fields to keep the JSON small.
5. **Server-side validation is authoritative.** Client validation is hint-only (per Story 0.4 RFC 9457 pattern).
6. **No deploy required for changes** — runtime config is the whole point per FR44.
7. **Tooltip is keyboard-accessible.** WCAG 2.1 AA — focus state shows the tooltip.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/Slices/Admin/Freemium/
│   │   ├── Get/
│   │   │   ├── GetFreemiumConfigResponse.cs                    # NEW
│   │   │   └── GetFreemiumConfigHandler.cs                     # NEW
│   │   └── Update/
│   │       ├── UpdateFreemiumConfigRequest.cs                  # NEW
│   │       ├── UpdateFreemiumConfigRequestValidator.cs         # NEW
│   │       └── UpdateFreemiumConfigHandler.cs                  # NEW
│   └── Faktuboh.Application/Telemetry/Events/
│       └── FreemiumConfigUpdatedTelemetry.cs                   # NEW
└── tests/Faktuboh.Api.Tests/Slices/Admin/Freemium/
    └── FreemiumConfigEndpointTests.cs                          # NEW

frontend/
└── apps/owner-workspace/src/app/features/admin/freemium/
    ├── freemium-config.component.ts                            # NEW
    ├── freemium-config.component.html                          # NEW
    └── freemium-config.component.spec.ts                       # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 |
| Integration (BE) | Testcontainers Postgres + invalidation roundtrip |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story extends `Slices/Admin/` with the `Freemium/` action group. The handlers depend on `IFreemiumConfigProvider` from Story 8.6; the call to `InvalidateAsync()` is the only cross-coupling point between this story and Story 8.6.

### Architecture compliance

- [epics.md Epic 8 Story 8.2](../planning-artifacts/epics.md)
- [prd.md FR44, NFR-S10](../planning-artifacts/prd.md)
- [Story 8.1 admin auth](./8-1-admin-dashboard-with-platform-metrics.md)
- [Story 8.6 freemium gate + provider](./8-6-freemium-limit-enforcement-ships-disabled.md)
- [Story 3.8 audit log](./3-8-user-visible-audit-trail-surface.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Frontend | PrimeNG (ToggleSwitch, Tooltip, ConfirmDialog, InputNumber) | Standard |

### Testing requirements

- **Invalidation roundtrip** — assert that PATCH → InvalidateAsync → next gate check uses new value, in a single integration test.
- **Diff payload structure** — without snapshot, the diff format could regress.
- **Toggle confirm dialog** — without test, accidental enable could ship.

### Previous Story Intelligence

**Prerequisites:**
- Story 8.1 — admin slice + auth
- Story 8.6 — `IFreemiumConfigProvider` + `freemium_config` table
- Story 3.8 — audit log
- Story 0.14 — telemetry

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.2
- [_bmad-output/planning-artifacts/prd.md FR44](../planning-artifacts/prd.md)
- [Story 8.6](./8-6-freemium-limit-enforcement-ships-disabled.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
