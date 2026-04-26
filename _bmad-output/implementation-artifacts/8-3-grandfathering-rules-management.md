# Story 8.3: Grandfathering rules management

Status: ready-for-dev

## Story

As **Wahid**,
I want to **manage grandfathering rules for users who exceeded thresholds before freemium was enabled**,
so that **existing users aren't abruptly capped when enforcement activates** (FR45) [Source: epics.md Epic 8 Story 8.3; prd.md FR45].

**Covers requirements:** Epic 8 — FR45 (grandfathering management), NFR-S10 admin elevated auth + audit, integrates with Story 8.6 gate via `IsGrandfatheredAsync` source [Source: epics.md Story 8.3].

**Why this story now:** Story 8.2 (config UI) just shipped. Story 8.6 (gate) reads `IsGrandfatheredAsync`. This story owns the table + the source of truth for that read.

## Acceptance Criteria

### AC1 — `grandfathering_rules` table

**Given** rules persist per-user
**When** migration `YYYYMMDDHHMMSS_AddGrandfatheringRulesTable.cs` lands
**Then** the table:
```sql
CREATE TABLE grandfathering_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_subject_id uuid NOT NULL,
    rule_kind text NOT NULL CHECK (rule_kind IN ('permanent_exemption', 'grace_period')),
    grace_expires_at timestamptz NULL,         -- NULL for permanent_exemption
    reason text NOT NULL,
    granted_by_subject_id uuid NOT NULL,
    granted_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL,
    revoked_by_subject_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_grandfathering_rules_active_per_user
    ON grandfathering_rules(owner_subject_id) WHERE revoked_at IS NULL;
CREATE INDEX ix_grandfathering_rules_grace_expiring
    ON grandfathering_rules(grace_expires_at)
    WHERE rule_kind = 'grace_period' AND revoked_at IS NULL AND grace_expires_at IS NOT NULL;
```
**And** the table is bitemporal (`IBitemporal`) per ADR-022
**And** unique partial index ensures only one active rule per user
**And** integration test asserts insert + history + uniqueness [Source: epics.md Story 8.3 AC; ADR-022].

### AC2 — `IGrandfatheringRepository` source for `IsGrandfatheredAsync`

**Given** Story 8.6's gate calls `IsGrandfatheredAsync(userId)`
**When** this story implements the source
**Then** `IGrandfatheringRepository.IsGrandfatheredAsync(Guid ownerSubjectId)` returns:
- `true` if a row exists with `owner_subject_id = ?` AND `revoked_at IS NULL` AND (`rule_kind = 'permanent_exemption'` OR `grace_expires_at > now()`)
- `false` otherwise
**And** the query uses the partial unique index for fast lookup
**And** result is cached per-user for 60 seconds (in-memory) — keeps gate fast under sustained load [Source: Story 8.6 placeholder interface].

### AC3 — `/admin/freemium/grandfathering` page lists users above thresholds

**Given** the admin needs to identify candidates
**When** the admin navigates to `/admin/freemium/grandfathering`
**Then** the page shows two sections:
- **Users above contact threshold** — list of `owner_subject_id` (opaque) + current contact count + current grandfathering status (None / Permanent / Grace until [date])
- **Users above transaction threshold** — same shape for transactions
**And** the lists are computed via `GET /v1/admin/freemium/grandfathering/candidates` returning aggregate counts only (no PII)
**And** sorted by current count desc (most-impacted users first)
**And** pagination + search by `owner_subject_id` short hash for direct lookup [Source: epics.md Story 8.3 AC; AR-043 minimum-k applies if breakdowns exposed].

### AC4 — Bulk action: mark grandfathered

**Given** the admin selects users via checkboxes
**When** the admin chooses "Mark as grandfathered" with rule kind selection
**Then** a `<p-confirmDialog>` opens listing the selected users + chosen rule kind + reason input (mandatory, max 500 chars)
**And** confirm triggers `POST /v1/admin/freemium/grandfathering/bulk` with body `{ userIds: Guid[], ruleKind: 'permanent_exemption' | 'grace_period', graceDays?: int (required if grace_period; default 30), reason: string }`
**And** the handler:
1. Validates: `ruleKind` enum; `graceDays` required if grace_period (range 1-365); `reason` non-empty
2. For each user, inserts a `grandfathering_rules` row (skip if active rule already exists — idempotent)
3. Writes one audit-log entry per user OR a bulk audit-log entry with `payload.userIds = [...]` (choose: bulk for fewer rows but lose per-user search granularity; chose per-user for cleaner audit trail per user)
4. Returns `200 OK` with summary `{ granted: int, skipped: int }`
**And** integration test seeds 5 users; bulk grants on 5 with 1 already-granted → response `{ granted: 4, skipped: 1 }` [Source: epics.md Story 8.3 AC bulk actions].

### AC5 — Revoke action

**Given** the admin removes a grandfathering rule
**When** the admin taps "Revoke" on an active rule
**Then** `<p-confirmDialog>` opens with body warning the user will be subject to enforcement immediately if `enforcement_enabled=true`
**And** confirm triggers `POST /v1/admin/freemium/grandfathering/{id}/revoke` with `{ reason: string }`
**And** updates `revoked_at = now()`, `revoked_by_subject_id = admin id` + writes audit
**And** the cached `IsGrandfatheredAsync` value for that user is invalidated [Source: epics.md Story 8.3 implicit revoke].

### AC6 — Grace-period auto-expiration

**Given** grace-period rules have `grace_expires_at`
**When** `grace_expires_at < now()` AND `revoked_at IS NULL`
**Then** the `IsGrandfatheredAsync` query naturally excludes them — no scheduled job needed
**And** an integration test seeds an expired grace-period rule + asserts gate enforces [Source: epics.md Story 8.3 implicit].

### AC7 — Telemetry — `grandfathering_rule_granted` + `grandfathering_rule_revoked`

**Given** AR-047 telemetry primitive
**When** events fire
**Then** events emit:
- `grandfathering_rule_granted` — `{ opaque_admin_id, opaque_user_id, rule_kind, grace_days_bucket }`
- `grandfathering_rule_revoked` — `{ opaque_admin_id, opaque_user_id, days_active_bucket }`
**And** PiiGuard validates [Source: Story 0.14].

## Tasks / Subtasks

### Backend — Migration + table

- [ ] **T1 (AC: 1)** — Create migration per AC1 + bitemporal history
- [ ] **T2 (AC: 1)** — Define `GrandfatheringRule` aggregate in `Faktuboh.Domain/Grandfathering/`
  - [ ] Implements `IBitemporal`
  - [ ] `Revoke(adminId, reason)` method
  - [ ] Repository interface

### Backend — IsGrandfathered query

- [ ] **T3 (AC: 2)** — Implement `GrandfatheringRepository.IsGrandfatheredAsync` with cache
  - [ ] In-memory per-user cache with 60s TTL
  - [ ] Cache invalidation triggered on grant/revoke

### Backend — Candidates endpoint

- [ ] **T4 (AC: 3)** — Create slice action `Slices/Admin/Grandfathering/Candidates/`
  - [ ] `CandidatesResponse.cs` — two lists (contact-over-threshold, transaction-over-threshold)
  - [ ] `CandidatesHandler.cs` — joins `contacts` count + `journal_entries` count vs `freemium_config` thresholds + current rule status
  - [ ] Endpoint `MapGet("/v1/admin/freemium/grandfathering/candidates", ...)`

### Backend — Bulk grant endpoint

- [ ] **T5 (AC: 4)** — Create slice action `Slices/Admin/Grandfathering/BulkGrant/`
  - [ ] `BulkGrantRequest.cs` — `(Guid[] UserIds, string RuleKind, int? GraceDays, string Reason)`
  - [ ] `BulkGrantRequestValidator.cs`
  - [ ] `BulkGrantHandler.cs`
  - [ ] Endpoint `MapPost("/v1/admin/freemium/grandfathering/bulk", ...)`

### Backend — Revoke endpoint

- [ ] **T6 (AC: 5)** — Create slice action `Slices/Admin/Grandfathering/Revoke/`
  - [ ] `RevokeRequest.cs`, `RevokeHandler.cs`
  - [ ] Endpoint `MapPost("/v1/admin/freemium/grandfathering/{id}/revoke", ...)`

### Backend — Telemetry

- [ ] **T7 (AC: 7)** — Define telemetry events + wire emission

### Frontend — Page

- [ ] **T8 (AC: 3, 4, 5)** — Create `apps/owner-workspace/src/app/features/admin/grandfathering/`
  - [ ] `grandfathering.component.ts` + `.html` + `.scss`
  - [ ] Two PrimeNG `<p-table>` instances with multi-select checkboxes
  - [ ] Bulk action bar with rule-kind selector + grace-days input + reason textarea
  - [ ] Per-row "Revoke" action with confirm dialog

### Frontend — API client

- [ ] **T9 (AC: 3, 4, 5)** — Extend `admin.client.ts`
  - [ ] `getGrandfatheringCandidates(): rxResource<CandidatesResponse>`
  - [ ] `bulkGrantGrandfathering(body): Observable<BulkGrantResponse>`
  - [ ] `revokeGrandfathering(id, reason): Observable<void>`

### Tests — Backend

- [ ] **T10 (AC: 1, 2)** — Integration tests for table + IsGrandfathered query
  - [ ] Permanent rule → eligible
  - [ ] Grace within window → eligible
  - [ ] Grace expired → not eligible
  - [ ] Revoked → not eligible
  - [ ] No rule → not eligible
- [ ] **T11 (AC: 4)** — Integration test for bulk grant
  - [ ] 5 users + 1 already-granted → response shows 4 granted, 1 skipped
  - [ ] All audit-log rows written
  - [ ] Cache invalidation: post-grant, `IsGrandfatheredAsync` returns true within 1s
- [ ] **T12 (AC: 5)** — Integration test for revoke + cache invalidation
- [ ] **T13 (AC: 6)** — Integration test for grace auto-expiration
- [ ] **T14 (AC: 7)** — Telemetry assertions

### Tests — Frontend

- [ ] **T15 (AC: 3, 4)** — Vitest tests for page + bulk action flow
- [ ] **T16 (AC: 3, 4, 5)** — Playwright E2E for full flow

## Dev Notes

### Critical guardrails (do not violate)

1. **Unique partial index on active rules.** Prevents accidental double-grant for the same user. Bulk handler also checks before insert (defense in depth).
2. **Cache invalidation on grant/revoke.** Without it, the gate could enforce on a just-grandfathered user for up to 60s.
3. **Bulk operations should be atomic per-user, not per-batch.** If user 3 of 10 fails, users 1-2 remain granted; user 3 reported as skipped/failed; users 4-10 still attempted. No batch rollback.
4. **`<p-confirmDialog>` on grant + revoke.** Both are admin-impactful actions.
5. **Mandatory reason field.** Length-capped (500 chars). Without it, audit trail loses context.
6. **Grace auto-expiration via query, no job.** Same pattern as Story 8.5 suspensions.
7. **Bitemporal history of every rule change.** ADR-022 compliance.
8. **No PII in lists.** Only `owner_subject_id` (opaque) + counts.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/Slices/Admin/Grandfathering/
│   │   ├── Candidates/
│   │   │   ├── CandidatesResponse.cs                           # NEW
│   │   │   └── CandidatesHandler.cs                            # NEW
│   │   ├── BulkGrant/
│   │   │   ├── BulkGrantRequest.cs                             # NEW
│   │   │   ├── BulkGrantRequestValidator.cs                    # NEW
│   │   │   └── BulkGrantHandler.cs                             # NEW
│   │   └── Revoke/
│   │       ├── RevokeRequest.cs                                # NEW
│   │       └── RevokeHandler.cs                                # NEW
│   ├── Faktuboh.Domain/Grandfathering/
│   │   ├── GrandfatheringRule.cs                               # NEW
│   │   └── IGrandfatheringRepository.cs                        # NEW
│   ├── Faktuboh.Application/Telemetry/Events/
│   │   ├── GrandfatheringRuleGrantedTelemetry.cs               # NEW
│   │   └── GrandfatheringRuleRevokedTelemetry.cs               # NEW
│   └── Faktuboh.Infrastructure/
│       ├── Persistence/
│       │   ├── Configurations/GrandfatheringRuleConfiguration.cs # NEW
│       │   └── Migrations/YYYYMMDDHHMMSS_AddGrandfatheringRulesTable.cs # NEW
│       └── Repositories/GrandfatheringRepository.cs            # NEW (with cache)
└── tests/Faktuboh.Api.Tests/Slices/Admin/Grandfathering/
    ├── CandidatesTests.cs                                      # NEW
    ├── BulkGrantTests.cs                                       # NEW
    └── RevokeTests.cs                                          # NEW

frontend/
└── apps/owner-workspace/src/app/features/admin/grandfathering/
    ├── grandfathering.component.ts                             # NEW
    ├── grandfathering.component.html                           # NEW
    └── grandfathering.component.spec.ts                        # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + NSubstitute |
| Integration (BE) | Testcontainers Postgres + cache-invalidation roundtrip |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story extends `Slices/Admin/` with the `Grandfathering/` action group. The `IGrandfatheringRepository` is the source-of-truth that Story 8.6's gate consumes — the contract was placeholder-stubbed in 8.6; this story owns the implementation.

### Architecture compliance

- [epics.md Epic 8 Story 8.3](../planning-artifacts/epics.md)
- [prd.md FR45, NFR-S10](../planning-artifacts/prd.md)
- [Story 8.1 admin auth](./8-1-admin-dashboard-with-platform-metrics.md)
- [Story 8.2 freemium config](./8-2-freemium-threshold-configuration-ui.md)
- [Story 8.6 gate consumer](./8-6-freemium-limit-enforcement-ships-disabled.md)
- [ADR-022 bitemporal](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Frontend | PrimeNG (Table multi-select, ConfirmDialog) | Standard |

### Testing requirements

- **Cache invalidation roundtrip** — without it, post-grant gates could still enforce.
- **Bulk-with-skip semantics** — exercises the "5 attempted, 1 skipped" branch.
- **Grace expiration** — without test, expired rules could still grant exemption.

### Previous Story Intelligence

**Prerequisites:**
- Story 8.1 — admin slice + auth
- Story 8.2 — freemium config (thresholds for candidate identification)
- Story 8.6 — gate consumer
- Story 3.8 — audit log
- Story 0.14 — telemetry

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.3
- [_bmad-output/planning-artifacts/prd.md FR45](../planning-artifacts/prd.md)
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
