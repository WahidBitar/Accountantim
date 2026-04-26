# Story 7.7: Recipient fraud-report affordance

Status: ready-for-dev

## Story

As a **recipient of a shared statement who believes it is fraudulent or harassing**,
I want **a "Report as fraudulent or harassing" button visible on the statement page that queues a report to admin review**,
so that **I have recourse against bad-actor senders** (FR53) [Source: epics.md Epic 7 Story 7.7; prd.md FR53].

**Covers requirements:** Epic 7 — FR53 (recipient fraud-report), §2.9 public-surface protection (no auto-revocation, neutral confirmation, IP-hash rate limit), Epic 8 prerequisite (`admin_review_queue` table) [Source: epics.md Story 7.7; architecture.md §2.9].

**Why this story now:** Last Epic 7 story. Establishes the `admin_review_queue` table that Epic 8 Story 8.4 consumes. This is the public-surface counterpart to Stories 4.1–4.13 — adds the only state-mutating recipient action besides Story 4.6 confirmation.

## Acceptance Criteria

### AC1 — `admin_review_queue` Postgres table

**Given** Epic 8 Story 8.4 will consume this queue
**When** migration `YYYYMMDDHHMMSS_AddAdminReviewQueueTable.cs` lands
**Then** the table:
```sql
CREATE TABLE admin_review_queue (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    capability_token_hash bytea NOT NULL,    -- the reported link's hash (lookup key for admin)
    owner_subject_id uuid NOT NULL,           -- the link's owner (denormalized for fast admin queries)
    contact_id uuid NULL,                     -- the link's target contact (if known)
    recipient_ip_hash bytea NOT NULL,         -- SHA256(ip + daily_salt) per Story 4.9's salt
    recipient_ua_hash bytea NOT NULL,
    report_category text NOT NULL CHECK (report_category IN ('fraud', 'harassment', 'other')),
    free_text text NULL,                      -- max 1000 chars; user-supplied
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'reviewed_no_action', 'flagged_for_action')),
    reviewed_by uuid NULL,                    -- admin subject_id (set in Story 8.4)
    reviewed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    processing_activity text NOT NULL CHECK (processing_activity = 'DebtorConfirmation')
);
CREATE INDEX ix_admin_review_queue_status_created ON admin_review_queue(status, created_at DESC);
CREATE INDEX ix_admin_review_queue_token_hash ON admin_review_queue(capability_token_hash);
CREATE INDEX ix_admin_review_queue_ip_hash_24h
    ON admin_review_queue(recipient_ip_hash, created_at) WHERE created_at > now() - interval '24 hours';
```
**And** the table is append-only by Postgres trigger (admins update `status`/`reviewed_*` only — see Story 8.4)
- Wait — admins DO need to update status. Refine: only INSERT + UPDATE OF (status, reviewed_by, reviewed_at) allowed; DELETE blocked
**And** Postgres trigger blocks DELETE; UPDATE allowed only on `status`, `reviewed_by`, `reviewed_at` columns:
```sql
CREATE OR REPLACE FUNCTION block_admin_review_queue_destructive_updates()
RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'admin_review_queue rows cannot be deleted';
    END IF;
    -- For UPDATE: ensure all columns except status/reviewed_by/reviewed_at are unchanged
    IF NEW.capability_token_hash != OLD.capability_token_hash
        OR NEW.owner_subject_id != OLD.owner_subject_id
        OR NEW.contact_id IS DISTINCT FROM OLD.contact_id
        OR NEW.recipient_ip_hash != OLD.recipient_ip_hash
        OR NEW.recipient_ua_hash != OLD.recipient_ua_hash
        OR NEW.report_category != OLD.report_category
        OR NEW.free_text IS DISTINCT FROM OLD.free_text
        OR NEW.created_at != OLD.created_at
        OR NEW.processing_activity != OLD.processing_activity
    THEN RAISE EXCEPTION 'admin_review_queue immutable columns cannot be modified';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER prevent_destructive_admin_review_queue_changes
BEFORE UPDATE OR DELETE ON admin_review_queue
FOR EACH ROW EXECUTE FUNCTION block_admin_review_queue_destructive_updates();
```
**And** integration tests assert: DELETE blocked + UPDATE on protected columns blocked + UPDATE on `status`/`reviewed_by`/`reviewed_at` allowed [Source: epics.md Story 7.7 AC; Story 8.4].

### AC2 — `POST /v1/statements/t/{token}/report` accepts a fraud report

**Given** a recipient on a valid statement view
**When** the recipient submits a report via `POST /v1/statements/t/{token}/report` with body `{ category: 'fraud' | 'harassment' | 'other', freeText?: string }` + headers `Idempotency-Key: <uuid>` + `X-CSRF-Token: <token>`
**Then** the handler:
1. Validates the token via `CapabilityTokenValidator` (constant-time per Story 4.1) — invalid → 404 generic
2. Validates CSRF double-submit per Story 4.6 AC3 pattern
3. Validates body: `category` in enum; `freeText` length ≤ 1000 chars; FluentValidation rejects with RFC 9457 on failure
4. Computes `recipient_ip_hash = SHA256(remote_ip + daily_salt)` per Story 4.9
5. Checks IP-rate-limit (AC4) — 429 if exceeded
6. Inserts row to `admin_review_queue` with `status = 'pending'`
7. Returns `200 OK` with neutral body `{ received: true }` (no info about the link, owner, etc.)
**And** the endpoint sets `ProcessingActivity.DebtorConfirmation`
**And** does NOT auto-revoke the link (admin review in Story 8.4 decides per FR47) [Source: epics.md Story 7.7; delegated decisions].

### AC3 — Neutral confirmation page protects against retaliation signal

**Given** the report submission succeeds
**When** the recipient SPA receives the response
**Then** the SPA navigates to `/report/received` (or shows an inline confirmation panel)
**And** the page text is: "Report received — thank you. Our team reviews each report. You will not receive a follow-up." (Transloco keys `report.received.title` + `report.received.body` for AR + EN)
**And** the page does NOT mention: the link owner, the contact, link revocation status, prior reports against this link, ETA for review
**And** the page does NOT include any link to the original statement (avoids retaliation reuse-the-link signal) [Source: delegated decisions; epics.md Story 7.7 AC neutral confirmation].

### AC4 — Rate limit per IP-hash: max 10 reports per 24h

**Given** abuse mitigation per delegated decision
**When** a recipient IP-hash has already submitted 10 reports in the last 24 hours
**Then** the 11th request returns `429` with RFC 9457 `faktuboh.statements.report_rate_limit_exceeded`
**And** the response body is generic — does NOT mention the limit value (avoids tuning-by-attacker)
**And** the rate-limit query uses the partial index `ix_admin_review_queue_ip_hash_24h` for fast counting
**And** an integration test simulates 11 reports from the same IP-hash → 11th is 429 [Source: delegated decisions].

### AC5 — "Report" button visible on statement view

**Given** the recipient is viewing the statement
**When** the public-statement page renders
**Then** a `<button>` with text "Report this statement" (Transloco key `statement.report_button`) is visible at the bottom of the view, NOT prominent (de-emphasized — bottom-of-page, plain text style, no icon)
**And** tapping opens a dialog (PrimeNG `<p-dialog>`) with:
- Header: "Report this statement"
- Category radio: Fraud / Harassment / Other
- Optional textarea for free-text (max 1000 chars; counter visible)
- Submit + Cancel buttons
**And** Submit calls the API; on success, navigates to `/report/received`
**And** Cancel closes the dialog with no action
**And** Axe-core: zero WCAG 2.1 AA violations on the dialog [Source: epics.md Story 7.7 AC button; ux-design-specification.md].

### AC6 — Submit is idempotent + deduplicated

**Given** a recipient may double-tap submit
**When** two concurrent requests with the same `Idempotency-Key` arrive
**Then** Story 0.5 idempotency middleware short-circuits the second
**And** if the recipient submits a SECOND report (different `Idempotency-Key`) for the same `(capability_token_hash, recipient_ip_hash, report_category)` within 5 minutes
**Then** the second insert is silently absorbed (returns 200, no new row inserted) — natural-key deduplication to prevent accidental double-submissions
**And** an integration test asserts both paths [Source: architecture.md §4.4 D4.11].

### AC7 — Telemetry — `fraud_report_submitted`

**Given** AR-047 telemetry primitive
**When** a report is submitted (and accepted, not rate-limited)
**Then** `TelemetryEmitter.Emit("fraud_report_submitted", new FraudReportSubmittedTelemetry(opaque_token_owner_id, contact_id_hash, category, has_free_text))` fires
**And** the payload does NOT include: free-text content, IP, IP-hash, recipient user-agent
**And** PiiGuard validates [Source: Story 0.14].

### AC8 — Reports do NOT trigger any owner-side notification

**Given** preventing retaliation requires not signaling the report to the owner
**When** a report is filed
**Then** NO SignalR event fires to the owner's `subject:{X}` group
**And** NO email or push notification is sent to the owner
**And** NO entry appears in any owner-visible UI (the owner can see their own links via Story 7.3, but the report is invisible to them)
**And** the only path the report reaches the owner is via Story 8.5 admin-imposed suspension (after admin review)
**And** NetArchTest asserts no `IHubContext<NotificationsHub>` reference exists in the report endpoint handler [Source: delegated decisions; architecture.md §2.9].

## Tasks / Subtasks

### Backend — Migration + table

- [ ] **T1 (AC: 1)** — Create migration `YYYYMMDDHHMMSS_AddAdminReviewQueueTable.cs` per AC1
  - [ ] Table + indexes + immutability trigger
- [ ] **T2 (AC: 1)** — Define `AdminReviewQueueEntry` entity in `Faktuboh.Domain/AdminReviewQueue/`
  - [ ] Repository interface `IAdminReviewQueueRepository`

### Backend — Report endpoint

- [ ] **T3 (AC: 2, 4, 6, 7)** — Create slice action `Slices/PublicStatements/Report/`
  - [ ] `ReportRequest.cs` — `(string Category, string? FreeText)`
  - [ ] `ReportResponse.cs` — `(bool Received)`
  - [ ] `ReportRequestValidator.cs` — category enum + free-text length
  - [ ] `ReportHandler.cs` — full handler per AC2
  - [ ] Endpoint `MapPost("/v1/statements/t/{token}/report", ...)` with `[AllowAnonymous]` + `ProcessingActivity.DebtorConfirmation` + Idempotency-Key middleware

### Backend — IP-hash rate limit

- [ ] **T4 (AC: 4)** — Implement `ReportRateLimitChecker` in `Faktuboh.Application/AdminReviewQueue/`
  - [ ] `Task<bool> IsRateLimitExceededAsync(byte[] ipHash)`
  - [ ] Query: `SELECT count(*) FROM admin_review_queue WHERE recipient_ip_hash = ? AND created_at > now() - interval '24 hours'`
  - [ ] Returns true if count ≥ 10
- [ ] **T5 (AC: 4)** — Register `faktuboh.statements.report_rate_limit_exceeded` (429) in `ErrorCatalog`

### Backend — IP-hash computation (reuse Story 4.9)

- [ ] **T6 (AC: 2)** — Reuse `IIpHashComputer` from Story 4.9 — inject into handler
  - [ ] Daily-salt rotation already handled by 4.9

### Backend — Deduplication

- [ ] **T7 (AC: 6)** — Add 5-minute dedup check in handler before insert
  - [ ] Query: `SELECT 1 FROM admin_review_queue WHERE capability_token_hash = ? AND recipient_ip_hash = ? AND report_category = ? AND created_at > now() - interval '5 minutes'`
  - [ ] If exists, return 200 with `{ received: true }` without inserting

### Backend — Telemetry

- [ ] **T8 (AC: 7)** — Define `FraudReportSubmittedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
  - [ ] Fields: `OpaqueTokenOwnerId, ContactIdHash, Category, HasFreeText`
- [ ] **T9 (AC: 7)** — Wire emission in handler

### Backend — Architecture tests

- [ ] **T10 (AC: 8)** — NetArchTest asserts no `IHubContext` injection in `ReportHandler`
  - [ ] Adds rule to `tests/Faktuboh.ArchitectureTests/PublicStatementsTests.cs`

### Frontend — Public-statement updates

- [ ] **T11 (AC: 5)** — Add "Report this statement" button to `apps/public-statement/src/app/features/statement-view/statement-view.component.ts`
  - [ ] De-emphasized: bottom-of-page, plain text styling
  - [ ] Tap opens `<p-dialog>` for `report-dialog.component.ts`
- [ ] **T12 (AC: 5)** — Create `apps/public-statement/src/app/features/report/report-dialog.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Form: category radio + optional textarea (max 1000 chars + counter)
  - [ ] Submit calls report API; on success, router-navigates to `/report/received`
  - [ ] Cancel closes dialog
- [ ] **T13 (AC: 3)** — Create `apps/public-statement/src/app/features/report/report-received.component.ts`
  - [ ] Standalone page at `/report/received`
  - [ ] Static neutral copy from Transloco
  - [ ] No links back to statement; no link to anything besides "Return to faktuboh.com"

### Frontend — API client

- [ ] **T14 (AC: 2)** — Create `apps/public-statement/src/app/core/api/report.client.ts`
  - [ ] `submitReport(token: string, category: string, freeText?: string): Observable<ReportResponse>`
  - [ ] Generated via NSwag from owner-workspace contracts (NOTE: report endpoint is in PublicStatements slice; separate NSwag config for public-statement may be needed — flag if blocked)

### Tests — Backend

- [ ] **T15 (AC: 1)** — Integration test for table immutability
  - [ ] Insert row; verify `status` updates allowed
  - [ ] Attempt to UPDATE `recipient_ip_hash` → exception
  - [ ] Attempt to DELETE → exception
- [ ] **T16 (AC: 2, 6)** — Integration test for happy path + dedup
  - [ ] Submit report → row inserted + 200 returned
  - [ ] Resubmit same `(token, ip, category)` within 5 min → 200, no second row
  - [ ] Idempotency-Key replay → cached response
- [ ] **T17 (AC: 4)** — Rate-limit test
  - [ ] Submit 10 reports from same IP-hash → all 200
  - [ ] 11th → 429 + `faktuboh.statements.report_rate_limit_exceeded`
- [ ] **T18 (AC: 8)** — Architecture test for no SignalR reference

### Tests — Frontend

- [ ] **T19 (AC: 5)** — Vitest for `report-dialog.component.ts`
  - [ ] Form validation: category required; free-text 0-1000 chars
  - [ ] Submit calls API; success navigates to `/report/received`
- [ ] **T20 (AC: 3)** — Vitest for `report-received.component.ts` neutrality test
  - [ ] Asserts page text contains no owner reference, no contact reference, no link
- [ ] **T21 (AC: 5)** — Playwright E2E `apps/public-statement/e2e/report/report-flow.spec.ts`
  - [ ] Navigate to statement → tap Report button → dialog opens → fill form → submit → land on /report/received
  - [ ] Axe-core: zero violations
  - [ ] RTL parity (Arabic locale)

## Dev Notes

### Critical guardrails (do not violate)

1. **No auto-revocation.** Reports are queued for admin review (Story 8.4); they do NOT immediately revoke the link. A bad-actor recipient could otherwise weaponize reports to silence legitimate senders.
2. **Neutral confirmation page protects the recipient.** No information leaks back about: link owner, prior reports, link status, ETA. The recipient just knows the report was received.
3. **No owner-side signal.** No SignalR push, no email, no UI badge. The owner only learns of the report if admin acts on it via Story 8.5 suspension. NetArchTest enforces no SignalR reference.
4. **IP-hash rate limit is per-IP not per-link.** A single bad-actor IP submitting 11 reports across different links is rate-limited; multiple legitimate recipients of the same problematic link can each submit one report.
5. **Append-only with selective UPDATE.** The trigger is more nuanced than Story 4.6 — UPDATE is allowed but only on the three admin-resolution columns. Without this, admin review (Story 8.4) cannot update status.
6. **Free-text length cap (1000 chars).** Without it, a malicious actor could spam huge text payloads to clog the admin queue UI. 1000 chars is "tweet-thread length" — sufficient for any genuine report.
7. **5-minute dedup is best-effort.** A motivated bad actor could circumvent by waiting 6 minutes; that's acceptable since the rate-limit (10/24h) provides the harder ceiling.
8. **Token never appears in logs.** Reuses Story 4.1's redaction discipline. The endpoint is decorated with the same `[ConstantTimeResponse]` attribute as the other token-validating endpoints.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/PublicStatements/Report/
│   │       ├── ReportRequest.cs                                # NEW
│   │       ├── ReportResponse.cs                               # NEW
│   │       ├── ReportRequestValidator.cs                       # NEW
│   │       └── ReportHandler.cs                                # NEW
│   ├── Faktuboh.Domain/AdminReviewQueue/
│   │   ├── AdminReviewQueueEntry.cs                            # NEW
│   │   └── IAdminReviewQueueRepository.cs                      # NEW
│   ├── Faktuboh.Application/
│   │   ├── AdminReviewQueue/
│   │   │   └── ReportRateLimitChecker.cs                       # NEW
│   │   ├── Telemetry/Events/
│   │   │   └── FraudReportSubmittedTelemetry.cs                # NEW
│   │   └── Errors/ErrorCatalog.cs                              # MODIFY (+1 code)
│   └── Faktuboh.Infrastructure/
│       ├── Persistence/
│       │   ├── Configurations/AdminReviewQueueConfiguration.cs # NEW
│       │   └── Migrations/YYYYMMDDHHMMSS_AddAdminReviewQueueTable.cs # NEW
│       └── Repositories/AdminReviewQueueRepository.cs          # NEW
└── tests/
    ├── Faktuboh.Api.Tests/Slices/PublicStatements/
    │   └── ReportTests.cs                                      # NEW (Integration)
    └── Faktuboh.ArchitectureTests/
        └── PublicStatementsTests.cs                            # EDIT (no SignalR rule)

frontend/
└── apps/public-statement/src/app/
    ├── features/
    │   ├── statement-view/statement-view.component.ts          # EDIT (add Report button)
    │   └── report/
    │       ├── report-dialog.component.ts                      # NEW
    │       ├── report-dialog.component.html                    # NEW
    │       ├── report-dialog.component.spec.ts                 # NEW
    │       ├── report-received.component.ts                    # NEW
    │       └── report-received.component.spec.ts               # NEW
    └── core/api/
        └── report.client.ts                                    # NEW
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + FluentAssertions |
| Integration (BE) | Testcontainers Postgres + immutability trigger validation |
| Unit (FE) | Vitest + Angular Testing Library |
| E2E | Playwright Chromium + WebKit |

### Project Structure Notes

The Report action lives under `Slices/PublicStatements/` (the existing public-surface slice from Stories 4.1–4.13). The `admin_review_queue` table is read by Epic 8 Story 8.4 — that consumer lives in a separate `Slices/Admin/` slice, with cross-slice access via the `IAdminReviewQueueRepository` (defined here in `Faktuboh.Domain/AdminReviewQueue/` as a shared infrastructure aggregate). This is acceptable because the queue is a true cross-cutting concern + the table acts as the seam between the public-surface slice + the admin slice.

### Architecture compliance

- [epics.md Epic 7 Story 7.7](../planning-artifacts/epics.md)
- [architecture.md §2.9](../planning-artifacts/architecture.md) — public-surface protection
- [Story 4.1 capability tokens](./4-1-capability-token-scheme-with-constant-time-response.md) — token validator + constant-time
- [Story 4.6 confirmation pattern](./4-6-recipient-confirm-button-timestamps-agreement.md) — CSRF + audit-table immutability pattern
- [Story 4.9 IP-hash + daily salt](./4-9-token-rate-limiting-20-links-hour-user-afd-edge-rule.md)
- [Story 0.14 telemetry](./0-14-phase-a-telemetry-primitive.md)
- [prd.md FR53](../planning-artifacts/prd.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Frontend | PrimeNG (Dialog, RadioButton, Textarea, Button) | Standard PrimeNG v21 |

### Testing requirements

- **Immutability trigger test (AC1)** — UPDATE on protected columns + DELETE both must throw. Without it, admin review (Story 8.4) could accidentally modify report data.
- **Rate-limit test (AC4)** — without it, an attacker could submit thousands of reports per IP, clogging admin review.
- **No-owner-signal test (AC8)** — NetArchTest assertion. A code change that "helpfully" notifies the owner is the silent retaliation-leak regression.
- **Neutral page text test (AC3)** — Vitest snapshot asserting the page contains no owner / contact / link references. Regression guard on copy changes.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 4.1 — capability tokens + constant-time validator + redaction discipline
- Story 4.6 — CSRF double-submit pattern + append-only-table trigger pattern
- Story 4.9 — IP-hash + daily salt computation
- Story 0.5 — Idempotency-Key middleware
- Story 0.14 — telemetry primitive

**Successor consumes (Epic 8):**
- Story 8.4 — admin investigation reads from `admin_review_queue`
- Story 8.5 — admin suspension is the only owner-side outcome of accepted reports

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.7
- [_bmad-output/planning-artifacts/architecture.md §2.9](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/prd.md FR53](../planning-artifacts/prd.md)
- [Story 4.1 capability tokens](./4-1-capability-token-scheme-with-constant-time-response.md)
- [Story 4.6 confirmation pattern](./4-6-recipient-confirm-button-timestamps-agreement.md)
- [Story 4.9 IP-hash](./4-9-token-rate-limiting-20-links-hour-user-afd-edge-rule.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
