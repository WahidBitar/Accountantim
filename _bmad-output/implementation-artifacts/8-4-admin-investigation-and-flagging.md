# Story 8.4: Admin investigation and flagging

Status: ready-for-dev

## Story

As **Wahid**,
I want to **investigate fraud-report submissions (from Story 7.7) + flag accounts showing abusive patterns**,
so that **bad-actor senders are surfaced and addressed** (FR46) [Source: epics.md Epic 8 Story 8.4; prd.md FR46].

**Covers requirements:** Epic 8 — FR46 (admin investigation), AR-043 minimum-k policy, NFR-S10 admin elevated auth + audit logging, downstream-trigger to Story 8.5 suspension flow [Source: epics.md Story 8.4].

**Why this story now:** Story 8.1 wired the admin shell + auth pattern. Story 7.7 produces the `admin_review_queue` rows. This story consumes them and gives Wahid the UI to act. Sequencing 8.4 before 8.5 ensures the investigation surface is live before the suspension hammer.

## Acceptance Criteria

### AC1 — `/admin/review` route lists pending fraud reports

**Given** Story 7.7 is live + reports exist in `admin_review_queue`
**When** the admin navigates to `/admin/review`
**Then** the page renders a PrimeNG `<p-table>` with columns:
- Created at (sortable; default sort desc)
- Token-hash short (first 8 chars of base64url for visual scan; full hash on hover)
- Owner subject_id (opaque short — hash prefix)
- Recipient IP-hash short
- Category (Fraud / Harassment / Other)
- Free-text preview (first 80 chars; full text on row expand)
- Status (Pending / Reviewed-no-action / Flagged-for-action)
- Action buttons: View detail / Mark no-action / Flag for action
**And** default filter is `status = pending`; status filter pills allow toggling
**And** pagination: 20 rows per page; total count displayed
**And** the data source is `GET /v1/admin/review?status=pending&page=1&size=20`
**And** all admin auth + audit + minimum-k inheritance from Story 8.1 applies [Source: epics.md Story 8.4 AC; Story 8.1].

### AC2 — Detail view shows reported link's owner + recent activity

**Given** the admin taps "View detail" on a row
**When** the detail panel opens (slide-over or dedicated route `/admin/review/{reportId}`)
**Then** the panel shows:
- Full report metadata (all columns from AC1 expanded)
- Reported link's owner: account creation date, total contacts, total debts (aggregate counts only — no PII)
- Owner's link-generation activity (last 30 days): total links generated, average per day, max in any 24h window
- Other reports against the same owner (if any): count + statuses
- Other reports from the same recipient IP-hash (if any): count + statuses (for cross-IP-hash pattern detection)
**And** all owner data is fetched via `GET /v1/admin/review/{reportId}/context` — a dedicated endpoint
**And** PII (owner name, contact names, debt amounts) is NEVER shown — admin investigation is metadata-only at MVP per AR-043 [Source: epics.md Story 8.4 AC detail; AR-043].

### AC3 — "Mark no-action" closes report

**Given** the admin determines no action needed
**When** the admin taps "Mark no-action" on a row or detail view
**Then** `<p-confirmDialog>` opens with header "Close this report with no action?" + body explaining the report will remain in the audit trail
**And** confirm triggers `PATCH /v1/admin/review/{reportId}` with `{ status: 'reviewed_no_action' }`
**And** the row updates to show status `Reviewed (no action)` with `reviewed_by = admin's subject_id` + `reviewed_at = now()`
**And** an audit-log entry is written: `action = 'admin.review.marked_no_action'` + payload `{ reportId, tokenHash }`
**And** the report row remains queryable in the queue but is excluded from default `status=pending` filter [Source: epics.md Story 8.4 AC actions].

### AC4 — "Flag for action" triggers Story 8.5 suspension flow

**Given** the admin determines suspension is warranted
**When** the admin taps "Flag for action"
**Then** `<p-confirmDialog>` opens with header "Flag this account for link-generation suspension?" + body listing the consequences (the account's owner cannot generate new links until suspension is lifted)
**And** confirm triggers `PATCH /v1/admin/review/{reportId}` with `{ status: 'flagged_for_action', suspensionReason: 'fraud-report' }`
**And** this also writes a row to `account_suspensions` (the table from Story 8.5) — a Wolverine handler `OnReportFlaggedForAction` consumes the `ReportFlaggedForActionEvent` integration event and creates the suspension
**And** the suspension is created with default 30-day expiration (admin can adjust in Story 8.5 UI)
**And** an audit-log entry is written for both the report flagging + the suspension creation
**And** the row updates to show `Flagged (suspension active)` [Source: epics.md Story 8.4 AC; Story 8.5].

### AC5 — Auto-flag rule: ≥ 3 reports within 7 days

**Given** the AC requires automatic flagging on repeated patterns
**When** a Wolverine job (`AutoFlagSuspiciousAccountsJob`) runs (scheduled hourly via Wolverine's scheduled-job feature)
**Then** the job queries:
```sql
SELECT owner_subject_id, COUNT(*) as report_count
FROM admin_review_queue
WHERE status = 'pending' AND created_at > now() - interval '7 days'
GROUP BY owner_subject_id
HAVING COUNT(*) >= 3
```
**And** for each result:
1. Marks all matching reports as `flagged_for_action` with `auto_flagged = true` (new column on the table — see Task)
2. Triggers the suspension flow per AC4
3. Writes audit entries with `action = 'admin.review.auto_flagged'`
**And** the job is idempotent — running twice produces the same state
**And** an admin notification (App Insights alert via email) fires on each auto-flag for review visibility [Source: epics.md Story 8.4 AC auto-flag].

### AC6 — Auto-flag column added to `admin_review_queue`

**Given** distinguishing manual vs auto flags matters for reporting
**When** migration `YYYYMMDDHHMMSS_AddAutoFlaggedColumnToAdminReviewQueue.cs` lands
**Then** the column `auto_flagged boolean NOT NULL DEFAULT false` is added
**And** the immutability trigger from Story 7.7 is updated to allow UPDATE on this column when transitioning to `flagged_for_action` [Source: Story 7.7 immutability trigger].

### AC7 — Manual single-report flagging always available

**Given** a single report MAY warrant immediate action (e.g., child-safety language detected)
**When** the admin opens any single report (not just ones with auto-flag count ≥ 3)
**Then** the "Flag for action" button is always available on every pending report
**And** the auto-flag rule (≥ 3 reports) is additive — admin can flag manually without waiting for auto-trigger [Source: epics.md Story 8.4 AC manual single-report flagging always available].

### AC8 — Audit logging for every action

**Given** NFR-S10 audit logging
**When** any of (load list, view detail, mark no-action, flag for action) happens
**Then** an audit-log entry is written via the Story 8.1 endpoint filter
**And** the entry's `payload` JSONB includes the relevant `reportId` + outcome
**And** the auto-flag job writes audit entries via a dedicated `IAuditLogService.RecordSystemActionAsync` overload (not the endpoint filter — no HTTP context for scheduled jobs) [Source: prd.md NFR-S10; Story 3.8].

### AC9 — Telemetry — `admin_report_resolved`

**Given** AR-047 telemetry
**When** any report is resolved (no-action OR flagged)
**Then** `TelemetryEmitter.Emit("admin_report_resolved", new AdminReportResolvedTelemetry(opaque_admin_id, report_category, outcome, was_auto_flagged, days_since_report_bucket))` fires
**And** `outcome` is `"no_action"` or `"flagged"`
**And** `days_since_report_bucket` is one of `"<1d" | "<3d" | "<7d" | ">7d"` — admin response-time metric [Source: Story 0.14].

## Tasks / Subtasks

### Backend — List endpoint

- [ ] **T1 (AC: 1)** — Create slice action `Slices/Admin/Review/List/`
  - [ ] `ListReviewsRequest.cs` — `(string? Status, int Page, int Size)`
  - [ ] `ReviewListItemDto.cs` — sealed record matching AC1 columns
  - [ ] `ListReviewsHandler.cs`
  - [ ] Endpoint `MapGet("/v1/admin/review", ...)` under admin slice (inherits filter + auth)

### Backend — Detail endpoint

- [ ] **T2 (AC: 2)** — Create slice action `Slices/Admin/Review/Context/`
  - [ ] `ReviewContextResponse.cs` — composite payload per AC2
  - [ ] `ReviewContextHandler.cs` — fetches report + owner aggregate counts (no PII)
  - [ ] Endpoint `MapGet("/v1/admin/review/{reportId}/context", ...)`

### Backend — Resolve endpoint

- [ ] **T3 (AC: 3, 4)** — Create slice action `Slices/Admin/Review/Resolve/`
  - [ ] `ResolveReviewRequest.cs` — `(string Status, string? SuspensionReason)`
  - [ ] `ResolveReviewHandler.cs` — performs the UPDATE on `admin_review_queue` + publishes `ReportFlaggedForActionEvent` if status flag-for-action
  - [ ] Endpoint `MapPatch("/v1/admin/review/{reportId}", ...)`
  - [ ] FluentValidation: `Status` in `{"reviewed_no_action", "flagged_for_action"}`

### Backend — Migration for auto_flagged column

- [ ] **T4 (AC: 6)** — Create migration `YYYYMMDDHHMMSS_AddAutoFlaggedColumnToAdminReviewQueue.cs`
  - [ ] Adds `auto_flagged boolean NOT NULL DEFAULT false`
  - [ ] Updates the immutability trigger from Story 7.7 to allow UPDATE on `auto_flagged` when transitioning to `flagged_for_action`

### Backend — Auto-flag scheduled job

- [ ] **T5 (AC: 5)** — Create `AutoFlagSuspiciousAccountsJob` in `Slices/Admin/Jobs/`
  - [ ] Wolverine scheduled job — register via `opts.Schedule<AutoFlagSuspiciousAccountsCommand>().Cron("0 * * * *")` (top of every hour)
  - [ ] Handler queries the SQL from AC5 + processes results
  - [ ] For each candidate, calls `ResolveReviewHandler` logic with `auto_flagged = true`
- [ ] **T6 (AC: 5)** — Configure App Insights alert for auto-flag fires
  - [ ] Custom event `admin_account_auto_flagged` emits per fire
  - [ ] Alert rule `alert-admin-auto-flag` notifies `ag-faktuboh-email`

### Backend — Suspension event integration

- [ ] **T7 (AC: 4)** — Define `ReportFlaggedForActionEvent` integration event in `Faktuboh.Domain.Events.IntegrationEvents/`
  - [ ] `(Guid ReportId, Guid OwnerSubjectId, string Reason, bool AutoFlagged, DateTimeOffset DefaultExpiresAt)`
- [ ] **T8 (AC: 4)** — Wolverine handler `OnReportFlaggedForAction` in `Slices/Admin/Suspensions/Projections/` (creates the `account_suspensions` row — relies on Story 8.5 having created the table)

### Backend — Telemetry

- [ ] **T9 (AC: 9)** — Define `AdminReportResolvedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T10 (AC: 9)** — Wire emission in `ResolveReviewHandler` + auto-flag job

### Backend — Audit log overload for scheduled jobs

- [ ] **T11 (AC: 8)** — Add `IAuditLogService.RecordSystemActionAsync(string action, Guid? targetResource, object payload)` overload
  - [ ] Uses `actor_subject_id = NULL` + `actor_kind = 'system'` to indicate non-human actor

### Frontend — Review list page

- [ ] **T12 (AC: 1)** — Create `apps/owner-workspace/src/app/features/admin/review/` feature folder
  - [ ] `review-list.component.ts` + `.html` + `.scss`
  - [ ] PrimeNG `<p-table>` with sortable columns + status filter pills + pagination
  - [ ] rxResource for `getReviewList()`

### Frontend — Detail panel + actions

- [ ] **T13 (AC: 2, 3, 4)** — Create `review-detail.component.ts`
  - [ ] Slide-over via PrimeNG `<p-sidebar>` OR dedicated route `/admin/review/{id}` (choose route for deep-linkability)
  - [ ] Renders context payload from `getReviewContext(reportId)`
  - [ ] "Mark no-action" + "Flag for action" buttons with `<p-confirmDialog>` per AC3 + AC4

### Frontend — API client

- [ ] **T14 (AC: 1, 2, 3)** — Extend `admin.client.ts`
  - [ ] `getReviewList(status?, page?, size?): rxResource<ReviewListResponse>`
  - [ ] `getReviewContext(reportId): rxResource<ReviewContextResponse>`
  - [ ] `resolveReview(reportId, status, suspensionReason?): Observable<void>`

### Tests — Backend

- [ ] **T15 (AC: 1)** — Integration test for list endpoint
  - [ ] Seed 5 reports (3 pending, 1 no-action, 1 flagged)
  - [ ] Default filter returns 3 pending; `status=all` returns 5
  - [ ] Pagination correctness
- [ ] **T16 (AC: 2)** — Integration test for detail endpoint
  - [ ] Reports for owner X with 4 prior reports → context returns count = 4
  - [ ] No PII in response payload (assert no name / phone / email fields)
- [ ] **T17 (AC: 3, 4)** — Integration test for resolve endpoint
  - [ ] Mark no-action: row updated, audit row written, integration event NOT published
  - [ ] Flag for action: row updated, integration event published, suspension row created (if Story 8.5 wires the consumer; otherwise assert event published)
- [ ] **T18 (AC: 5)** — Integration test for auto-flag job
  - [ ] Seed 4 reports for same owner within 7 days → run job → all 4 flagged with `auto_flagged=true`
  - [ ] Seed 2 reports → run job → no-op
  - [ ] Idempotent: run twice → state unchanged after second run
- [ ] **T19 (AC: 6)** — Integration test for trigger update
  - [ ] UPDATE `auto_flagged` along with status transition → allowed
  - [ ] UPDATE `auto_flagged` without status transition → still allowed (column is admin-managed)
- [ ] **T20 (AC: 8)** — Audit-log assertions for each action
- [ ] **T21 (AC: 9)** — Telemetry assertion `AssertEventEmitted("admin_report_resolved")`

### Tests — Frontend

- [ ] **T22 (AC: 1)** — Vitest for `review-list.component.ts`
  - [ ] Renders table with mocked data
  - [ ] Filter pill click changes query
  - [ ] Pagination triggers refetch
- [ ] **T23 (AC: 3, 4)** — Vitest for `review-detail.component.ts`
  - [ ] Context payload renders without PII fields
  - [ ] Confirm dialogs appear on action button clicks
  - [ ] Confirm triggers API call
- [ ] **T24 (AC: 1)** — Playwright E2E `apps/owner-workspace/e2e/admin/review-investigation.spec.ts`
  - [ ] Mocked admin login → navigate to /admin/review → list renders → open detail → flag action → status updates
  - [ ] Axe-core: zero violations

## Dev Notes

### Critical guardrails (do not violate)

1. **No PII in admin investigation surfaces.** Owner name, contact names, debt amounts are NEVER shown. Admin sees aggregate counts + opaque IDs only. AR-043 minimum-k is not enough by itself — at MVP, just don't expose PII to admin at all.
2. **Auto-flag job MUST be idempotent.** Running twice produces same state. Without this, retries (Wolverine at-least-once semantics) double-flag accounts.
3. **`<p-confirmDialog>` on every destructive action.** Flagging an account is destructive (triggers suspension). Per UX-DR29 destructive-confirm pattern.
4. **Audit log every admin action AND the auto-flag job.** Without it, accountability for false positives is gone.
5. **Auto-flag threshold (≥ 3 in 7 days) is conservative.** A lower threshold risks false positives at low traffic. If the threshold needs tuning post-launch, it's a config value (TBD: extract to runtime config in a future story; at MVP, hardcoded constant).
6. **Manual single-report flag is always available.** Admin discretion overrides the auto-rule. Without this, urgent abuse (e.g., child safety) waits for the threshold.
7. **The `auto_flagged` column requires updating Story 7.7's trigger.** Easy to miss — the trigger from Story 7.7 was strict; this story relaxes it for one column.
8. **The Wolverine cron `0 * * * *` is hourly.** Don't accidentally use `* * * * *` (every minute) — that would flood the DB.
9. **Auto-flag App Insights alert is critical.** Wahid needs awareness when the system auto-acts on his behalf, even if the system was correct.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/Admin/
│   │       ├── Review/
│   │       │   ├── List/
│   │       │   │   ├── ListReviewsRequest.cs                   # NEW
│   │       │   │   ├── ReviewListItemDto.cs                    # NEW
│   │       │   │   └── ListReviewsHandler.cs                   # NEW
│   │       │   ├── Context/
│   │       │   │   ├── ReviewContextResponse.cs                # NEW
│   │       │   │   └── ReviewContextHandler.cs                 # NEW
│   │       │   └── Resolve/
│   │       │       ├── ResolveReviewRequest.cs                 # NEW
│   │       │       └── ResolveReviewHandler.cs                 # NEW
│   │       ├── Jobs/
│   │       │   ├── AutoFlagSuspiciousAccountsCommand.cs        # NEW (Wolverine schedule message)
│   │       │   └── AutoFlagSuspiciousAccountsJob.cs            # NEW (handler)
│   │       └── Suspensions/Projections/
│   │           └── OnReportFlaggedForAction.cs                 # NEW
│   ├── Faktuboh.Domain/Events/IntegrationEvents/
│   │   └── ReportFlaggedForActionEvent.cs                      # NEW
│   ├── Faktuboh.Application/
│   │   ├── Telemetry/Events/
│   │   │   └── AdminReportResolvedTelemetry.cs                 # NEW
│   │   └── Audit/
│   │       └── IAuditLogService.cs                             # MODIFY (+ system overload)
│   └── Faktuboh.Infrastructure/Persistence/Migrations/
│       └── YYYYMMDDHHMMSS_AddAutoFlaggedColumnToAdminReviewQueue.cs  # NEW
└── tests/Faktuboh.Api.Tests/Slices/Admin/Review/
    ├── ListReviewsTests.cs                                     # NEW
    ├── ReviewContextTests.cs                                   # NEW
    ├── ResolveReviewTests.cs                                   # NEW
    └── AutoFlagJobTests.cs                                     # NEW

frontend/
├── apps/owner-workspace/src/app/features/admin/review/
│   ├── review-list.component.ts                                # NEW
│   ├── review-list.component.html                              # NEW
│   ├── review-list.component.spec.ts                           # NEW
│   ├── review-detail.component.ts                              # NEW
│   ├── review-detail.component.html                            # NEW
│   └── review-detail.component.spec.ts                         # NEW
└── apps/owner-workspace/src/app/core/api/
    └── admin.client.ts                                         # EDIT (+ review methods)

infra/azure/modules/
└── alerts.bicep                                                # EDIT (alert-admin-auto-flag)
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + NSubstitute |
| Integration (BE) | Testcontainers Postgres + real Wolverine + scheduled-job test mode |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story extends the `Slices/Admin/` slice from Story 8.1 with the `Review/` action group + a `Jobs/` folder for the scheduled job + a `Suspensions/Projections/` folder for the Wolverine handler that bridges to Story 8.5. The handler's existence here is the seam — Story 8.5 owns the `account_suspensions` table + service, this story just publishes the event that triggers a row creation.

### Architecture compliance

- [epics.md Epic 8 Story 8.4](../planning-artifacts/epics.md)
- [Story 7.7 admin_review_queue](./7-7-recipient-fraud-report-affordance.md) — table source
- [Story 8.1 admin slice + auth pattern](./8-1-admin-dashboard-with-platform-metrics.md)
- [Story 8.5 account_suspensions](./8-5-admin-suspend-statement-link-generation.md) — downstream effect
- [Story 3.8 audit log](./3-8-user-visible-audit-trail-surface.md)
- [architecture.md AR-043](../planning-artifacts/architecture.md) — minimum-k policy
- [prd.md FR46, NFR-S10](../planning-artifacts/prd.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Wolverine scheduled jobs | `opts.Schedule<T>().Cron("...")` |
| Frontend | PrimeNG (Table, Sidebar, ConfirmDialog) | Standard PrimeNG v21 |

### Testing requirements

- **Auto-flag idempotency test** — without it, double-flagging on Wolverine retries goes silently undetected.
- **No-PII assertion in detail response** — Vitest snapshot or backend integration assertion that the JSON contains no name/phone/email keys.
- **Manual single-report flag test** — confirms admin discretion is preserved.
- **Cross-report-pattern detection test** — owner with 5 reports → context returns count 5 (visible to admin for situational awareness).

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 7.7 — `admin_review_queue` table + reports flowing in
- Story 8.1 — admin slice + auth pattern + audit-log filter
- Story 3.8 — `audit_log` table + `IAuditLogService`
- Story 0.14 — telemetry primitive

**Successor (Story 8.5)** consumes the `ReportFlaggedForActionEvent` integration event published here.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 8 Story 8.4
- [_bmad-output/planning-artifacts/architecture.md AR-043](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/prd.md FR46, NFR-S10](../planning-artifacts/prd.md)
- [Story 7.7](./7-7-recipient-fraud-report-affordance.md)
- [Story 8.1](./8-1-admin-dashboard-with-platform-metrics.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
