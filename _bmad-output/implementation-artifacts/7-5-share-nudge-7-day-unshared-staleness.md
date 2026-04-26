# Story 7.5: Share nudge — 7-day unshared staleness

Status: ready-for-dev

## Story

As a **user with debts recorded but not shared**,
I want **a reminder when recorded-but-unshared transactions with a contact are > 7 days old**,
so that **I reconcile statements before they age into forgotten territory** (FR52b) [Source: epics.md Epic 7 Story 7.5; prd.md FR52b].

**Covers requirements:** Epic 7 — FR52b (7-day staleness nudge), UX-DR11 ContextualNudge `staleness-reminder` kind, reuse of `nudge_dismissals` table from Story 7.4 [Source: epics.md Story 7.5].

**Why this story now:** Second of three nudge variants. Reuses the dismissal table + cooldown logic from Story 7.4. Adds a different trigger condition (oldest-unshared-debt age) but the same dismiss-cooldown semantics.

## Acceptance Criteria

### AC1 — Nudge eligibility — `GET /v1/nudges/staleness-reminder?contactId={id}`

**Given** the user lands on the contact detail page
**When** the page calls the eligibility endpoint
**Then** the handler resolves eligibility:
1. Find the oldest debt for `(user_id, contact_id)` in `journal_entries` that has NEVER been included in any shared `statements` row for that owner+contact
2. If no such debt exists OR its age < 7 days, return `{ eligible: false }`
3. Check `nudge_dismissals` for matching `(user_id, contact_id, 'staleness-reminder')` row with `suppress_until > now()` — if so, return `{ eligible: false }`
4. Otherwise, return `{ eligible: true, kind: 'staleness-reminder', contactId, oldestUnsharedDebtAt: ISO8601 }`
**And** "included in a shared statement" means: a `statements` row exists for `(owner_subject_id, contact_id)` with `created_at > debt.created_at` (statements snapshot all debts up to their creation time)
**And** the endpoint p95 < 100ms — uses indexed query on `journal_entries(contact_id, owner_subject_id, created_at)` [Source: epics.md Story 7.5 AC; FR52b].

### AC2 — `ContextualNudge kind="staleness-reminder"` renders per UX-DR11

**Given** the eligibility check returns `{ eligible: true }`
**When** `<ow-contextual-nudge kind="staleness-reminder">` renders
**Then** the component shows:
- Text: "You haven't shared [contact name]'s statement in over 7 days" (Transloco key `nudges.staleness_reminder.body`)
- Primary action: "Share now" — opens share flow (Story 4.4)
- Secondary action: "Dismiss"
- Same inline-card layout per UX-DR11 (no modal, non-blocking)
**And** the nudge appears at top of contact detail page (NOT dashboard tile — staleness is per-contact, not aggregate)
**And** Arabic + English Transloco keys cover all copy [Source: ux-design-specification.md UX-DR11].

### AC3 — Dismiss action — same cooldown rule as Story 7.4

**Given** the user taps "Dismiss"
**When** `POST /v1/nudges/staleness-reminder/dismiss` is called with `{ contactId }`
**Then** the handler:
1. Looks up existing `nudge_dismissals` row for `(user_id, contact_id, 'staleness-reminder')`
2. Same upsert + 3-strikes-30-days logic as Story 7.4 AC4 (shared dismissal cooldown library function)
3. Returns `200 OK` with `{ suppressedUntil: ISO8601 | null }`
**And** dismissal of `staleness-reminder` does NOT affect `share-first-statement` or `threshold-crossing` for the same contact (per-kind isolation)
**And** an integration test asserts cross-kind isolation [Source: Story 7.4 AC4; delegated decisions].

### AC4 — Auto-clear after share

**Given** the user shares a statement for the contact
**When** subsequent eligibility checks run
**Then** the staleness condition re-evaluates: the just-created `statements` row's `created_at` covers all prior debts → no debt is "unshared" → `{ eligible: false }`
**And** the nudge disappears on next page load (passive — no real-time update at MVP) [Source: epics.md Story 7.5 implicit; same model as Story 7.4 AC5].

### AC5 — Telemetry — reuse Story 7.4 events with `kind="staleness-reminder"`

**Given** AR-047 telemetry primitive
**When** events fire
**Then** `nudge_shown` / `nudge_clicked` / `nudge_dismissed` emit with `nudge_kind: "staleness-reminder"` (same payload schemas from Story 7.4)
**And** PiiGuard validates [Source: Story 0.14; Story 7.4 telemetry contracts].

### AC6 — Eligibility endpoint shares rate-limit policy with Story 7.4

**Given** a single user may load multiple contact detail pages in rapid succession
**When** the rate-limit middleware applies
**Then** the same per-user `NudgesEligibilityPolicy` from Story 7.4 (60 req/min/user) covers this endpoint
**And** the rate-limit pool is shared across all `/v1/nudges/*` GET endpoints (a single global per-user counter, not per-kind)
**And** test asserts: 30 calls to share-first + 31 calls to staleness in same minute → 61st returns 429 [Source: Story 7.4 AC7; prd.md NFR-S5].

### AC7 — Statements snapshot semantics — debts created after share are still "unshared"

**Given** the share-then-add-debt sequence
**When** a user shares at T1, then records a new debt at T2 (T2 > T1)
**Then** the new debt is "unshared" until the user shares again at T3 (T3 > T2) — the prior `statements` row at T1 does NOT cover the T2 debt
**And** if (now - T2) > 7 days and no T3 share has happened, the staleness nudge fires
**And** an integration test covers the share-add-share-add sequence to verify the snapshot semantics are correct [Source: epics.md Story 7.5 AC implicit; Story 4.4 statement-snapshot model].

## Tasks / Subtasks

### Backend — Eligibility endpoint

- [ ] **T1 (AC: 1)** — Create slice action `Slices/Nudges/StalenessReminder/Eligibility/`
  - [ ] `EligibilityRequest.cs`, `EligibilityResponse.cs`, `EligibilityHandler.cs`
  - [ ] Query: `journal_entries.contact_id = ? AND owner_subject_id = ? AND created_at < (SELECT MIN(s.created_at) FROM statements s WHERE s.contact_id = ? AND s.owner_subject_id = ? AND s.created_at > journal_entries.created_at)` — returns oldest unshared debt
  - [ ] Endpoint `MapGet("/v1/nudges/staleness-reminder", ...)` with `.RequireAuthorization()` + shared rate-limit policy

### Backend — Dismiss endpoint

- [ ] **T2 (AC: 3)** — Create slice action `Slices/Nudges/StalenessReminder/Dismiss/`
  - [ ] Reuses the `INudgeDismissalRepository` + cooldown library function from Story 7.4
  - [ ] `DismissHandler.cs` calls the shared function with `nudge_kind = "staleness-reminder"`
  - [ ] Endpoint `MapPost("/v1/nudges/staleness-reminder/dismiss", ...)`

### Backend — Shared cooldown function

- [ ] **T3 (AC: 3)** — Extract Story 7.4's dismissal upsert into `Faktuboh.Application/Nudges/NudgeDismissalService.cs`
  - [ ] Method `RegisterDismissalAsync(userId, contactId, nudgeKind) -> DateTimeOffset? suppressedUntil`
  - [ ] Encapsulates the 3-strikes-30-days rule
  - [ ] Story 7.4's handler refactored to call this function (refactor noted in 7.4 follow-up if not done as part of this story)

### Backend — Update `nudge_kind` enum

- [ ] **T4 (AC: 1)** — Verify the CHECK constraint on `nudge_dismissals.nudge_kind` from Story 7.4 already includes `'staleness-reminder'` (it does per Story 7.4 AC1)
  - [ ] No migration change needed — enum value pre-allowed

### Backend — Telemetry

- [ ] **T5 (AC: 5)** — Reuse `NudgeShownTelemetry` etc. from Story 7.4 — emit with `nudge_kind = "staleness-reminder"`

### Frontend — Component variant

- [ ] **T6 (AC: 2)** — Extend `<ow-contextual-nudge>` (from Story 7.4) to handle `kind="staleness-reminder"`
  - [ ] Add Transloco keys for staleness body/actions
  - [ ] Component switch on kind for copy + icon (per UX-DR11)
- [ ] **T7 (AC: 1, 4)** — Extend `nudges.client.ts` (from Story 7.4)
  - [ ] `getStalenessEligibility(contactId): rxResource<EligibilityResponse>`
  - [ ] `dismissStaleness(contactId): Observable<DismissResponse>`
- [ ] **T8 (AC: 2)** — Wire nudge into contact detail page (already wired for share-first per Story 7.4 — add second resource subscription + render in priority order if both eligible: share-first wins, staleness suppresses)
  - [ ] Suppression rule: if both `share-first-statement` AND `staleness-reminder` would fire, only render `share-first-statement` (the higher-engagement-moment one)

### Tests — Backend

- [ ] **T9 (AC: 1, 7)** — Integration tests for eligibility
  - [ ] No debts → not eligible
  - [ ] Debt < 7 days, no share → not eligible
  - [ ] Debt > 7 days, no share → eligible
  - [ ] Debt > 7 days, but a later share covers it → not eligible
  - [ ] Share at T1, new debt at T2 (T2 > T1, age > 7d), no T3 share → eligible
  - [ ] Suppressed via dismissal → not eligible
- [ ] **T10 (AC: 3)** — Integration test for dismiss + cross-kind isolation
  - [ ] Dismiss `staleness-reminder` 3x → suppressed for 30 days
  - [ ] `share-first-statement` for same contact still eligible (when its conditions met)
- [ ] **T11 (AC: 6)** — Rate-limit test: 31 share-first + 30 staleness calls fine; 32nd share-first call → 429

### Tests — Frontend

- [ ] **T12 (AC: 2)** — Vitest component test for `<ow-contextual-nudge kind="staleness-reminder">`
- [ ] **T13 (AC: 2)** — Playwright E2E: create old debt (mock `created_at` to 8 days ago via test seed) → nudge appears; share → nudge disappears; dismiss → nudge disappears
- [ ] **T14 (AC: 2)** — Axe-core: zero WCAG violations

## Dev Notes

### Critical guardrails (do not violate)

1. **Reuse Story 7.4's `NudgeDismissalService` — do NOT duplicate the 3-strikes-30-days logic.** If Story 7.4 didn't extract the service, this story extracts it as a refactor (T3) and back-ports Story 7.4 to call it.
2. **Staleness eligibility query MUST be indexed.** The subquery `MIN(s.created_at) WHERE s.created_at > je.created_at` is a tail-cost trap — verify the query plan uses index scans on both tables. Add `EXPLAIN ANALYZE` output to a comment in the handler if the query is non-trivial.
3. **Statement snapshot semantics are crucial.** A statement at T1 does NOT cover a debt at T2 (T2 > T1). Without this rule, sharing once would silence the staleness nudge forever. AC7 + the test variant covers this.
4. **Cross-kind isolation.** Dismissing one kind does NOT affect other kinds for the same contact. The unique-constraint on `(user_id, contact_id, nudge_kind)` enforces row separation.
5. **Priority rule when multiple kinds eligible.** Per the wiring in T8: `share-first-statement` wins over `staleness-reminder`. Document in component logic + add a test that asserts this.
6. **Eligibility query is cacheable per (user, contact) but cache invalidation is non-trivial.** At MVP, no caching — re-query each page load. Re-evaluate if p95 exceeds 100ms in production.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/Nudges/StalenessReminder/
│   │       ├── Eligibility/
│   │       │   ├── EligibilityRequest.cs                       # NEW
│   │       │   ├── EligibilityResponse.cs                      # NEW
│   │       │   └── EligibilityHandler.cs                       # NEW
│   │       └── Dismiss/
│   │           ├── DismissRequest.cs                           # NEW
│   │           ├── DismissResponse.cs                          # NEW
│   │           └── DismissHandler.cs                           # NEW (calls NudgeDismissalService)
│   └── Faktuboh.Application/Nudges/
│       └── NudgeDismissalService.cs                            # NEW (extracted from Story 7.4)
└── tests/Faktuboh.Api.Tests/Slices/Nudges/
    └── StalenessReminderTests.cs                               # NEW (Integration)

frontend/
├── apps/owner-workspace/src/app/core/nudges/
│   └── nudges.client.ts                                        # EDIT (add staleness methods)
└── libs/domain-ui/contextual-nudge/
    ├── contextual-nudge.component.ts                           # EDIT (add staleness kind)
    └── contextual-nudge.component.spec.ts                      # EDIT (add staleness test)
```

### Testing standards

| Level | Tools |
|---|---|
| Integration (BE) | Testcontainers Postgres + `WebApplicationFactory<Program>` + mock `IClock` |
| Unit (FE) | Vitest |
| E2E | Playwright |

### Project Structure Notes

This story adds a sibling action folder under the `Nudges/` slice scaffolded in Story 7.4. The slice's domain types (`NudgeDismissal`, `NudgeKind`, `INudgeDismissalRepository`) are reused unchanged. The shared `NudgeDismissalService` lives in `Faktuboh.Application/Nudges/` because both `ShareFirstStatement` and `StalenessReminder` actions consume it — extracting prevents drift in the cooldown rule.

### Architecture compliance

- [epics.md Epic 7 Story 7.5](../planning-artifacts/epics.md)
- [ux-design-specification.md UX-DR11](../planning-artifacts/ux-design-specification.md)
- [prd.md FR52b, NFR-S5](../planning-artifacts/prd.md)
- [Story 7.4](./7-4-share-nudge-after-first-3-debts-per-contact.md) — table + dismissal service + component
- [Story 4.4](./4-4-generate-statement-html-shareable-link.md) — statement creation source

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | (No new packages) | Reuses Story 7.4 stack |

### Testing requirements

- **Mock clock for the 7-day age threshold.** Same `IClock` injection as Story 7.4. Without this, the test takes 7 days.
- **Statement-snapshot test (AC7)** — non-obvious; without it, the share-then-add-debt scenario silently fails to nudge.
- **Cross-kind isolation test** — confirms Story 7.4 + 7.5 (and later 7.6) coexist correctly.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 7.4 — `nudge_dismissals` table + `<ow-contextual-nudge>` component + dismissal service
- Story 4.4 — statement creation (defines "shared")
- Story 0.14 — telemetry primitive

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.5
- [_bmad-output/planning-artifacts/ux-design-specification.md UX-DR11](../planning-artifacts/ux-design-specification.md)
- [_bmad-output/planning-artifacts/prd.md FR52b, NFR-S5](../planning-artifacts/prd.md)
- [Story 7.4](./7-4-share-nudge-after-first-3-debts-per-contact.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
