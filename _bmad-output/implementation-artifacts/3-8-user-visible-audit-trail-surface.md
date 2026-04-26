# Story 3.8: User-visible audit trail surface

Status: ready-for-dev

## Story

As a **user who wants transparent access to the history of my own records**,
I want to **see the audit trail of any debt I edited or deleted at `/contacts/{id}/debts/{debtId}/history`**,
so that I have **chronological visibility into timestamp + action (created/edited/deleted) + before/after values** (FR49 user-visible portion), the list is read-only (FR49 immutability — no "undo" or "restore" at MVP), entries referencing `ProcessingActivity.DebtorConfirmation` are labeled "Recipient confirmed", and entries older than 7 years or from anonymized accounts show opaque identifiers (`User-7a3f`) rather than re-surfacing PII.

**Covers requirements:** FR49 (user-visible audit surface — read-only), AR-034 (7-year retention + opaque-identifier rendering for older entries), UX-DR12 (ReconciliationMeta marker for confirmed entries).

**Why this surface is read-only:** FR49 audit immutability means the user can VIEW but cannot modify audit entries. This aligns with the Story 0.6 DB trigger blocking UPDATE/DELETE on `audit_log`. The surface is the compliance-facing window into changes that have already been persisted atomically with the mutation.

## Acceptance Criteria

### AC1 — GET returns chronological audit entries for a debt

**Given** a journal entry with ≥ 1 edit or delete action
**When** the frontend issues `GET /v1/journal-entries/{id}/history`
**Then** the response is `JournalEntryHistoryResponse` with `entries: AuditLogEntry[]` sorted `recorded_at ASC`
**And** each `AuditLogEntry` has:
  - `id: Guid`
  - `action: "created" | "updated" | "deleted"`
  - `recordedAt: DateTimeOffset`
  - `actorLabel: string` — either a user-facing actor name (for recent entries from active accounts) OR opaque `"User-{hash-suffix}"` for older/anonymized entries
  - `before: object | null` — JSON snapshot (null on "created")
  - `after: object | null` — JSON snapshot (null on "deleted")
  - `processingActivityLabel: string` — localized label like "Owner ledger" or "Recipient confirmed"
  - `source: "owner" | "recipient"` — derived from `ProcessingActivity`

### AC2 — Read-only rendering

**Given** the audit trail renders at `/contacts/{id}/debts/{debtId}/history`
**When** the user views it
**Then** the list shows each entry as a timeline card with the action marker (icon per action type), timestamp, actor label, and diff view (before vs after)
**And** NO undo/restore buttons; NO edit affordances on audit entries
**And** deleted debts render with strikethrough (reused from Story 3.3)

### AC3 — Recipient confirmation labeling

**Given** an audit entry was recorded with `ProcessingActivity.DebtorConfirmation` (Epic 4 activates this path; MVP state renders the label when the enum value matches)
**When** the entry renders
**Then** a badge "Recipient confirmed" appears (localized AR/EN) with a distinct styling (`<p-tag severity="info">`)
**And** the actor label reads the contact's name (which is PII but intentional here since the user already knows the counterparty)

### AC4 — Opaque identifiers for old/anonymized entries

**Given** an entry is older than 7 years (AR-034 retention window) OR the entry's actor account is anonymized (post-erasure Story 1.7)
**When** the entry renders
**Then** the actor label renders as `User-{4-char-hash-suffix}` (e.g., `User-7a3f`) computed deterministically from `SHA256(actor_subject_id)[0:4]`
**And** PII fields in `before`/`after` snapshots are replaced with `"[redacted]"` for anonymized actors
**And** the before/after diff still renders structurally so the historical action is visible

### AC5 — RFC 9457 on forbidden access

**Given** the debt belongs to a different user
**When** the GET is attempted
**Then** response is 404 RFC 9457 with same-timing response per AR-016 (constant-time lookup to avoid enumeration oracle)

### AC6 — Diff rendering

**Given** an "updated" entry with before + after snapshots
**When** the entry renders
**Then** a side-by-side (desktop) or stacked (mobile) diff view renders field-by-field
**And** changed fields are highlighted (before in `--surface-200`, after in `--primary-100`)
**And** unchanged fields are collapsed behind a "Show unchanged" toggle

### AC7 — RTL + a11y

**Given** the user's locale is Arabic
**When** the page renders
**Then** timeline flows top-to-bottom (direction-invariant); field labels render RTL
**And** axe-core zero WCAG 2.1 AA violations

### AC8 — Telemetry

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** the audit surface loads
**Then** telemetry `debt_history_viewed` emits with `{ opaque_user_id, journal_entry_id_hash, entry_count_bucket }`

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 5)** — Create `Slices/JournalEntries/History/GetJournalEntryHistoryHandler.cs`
  - [ ] Endpoint `group.MapGet("/{id:guid}/history", ...)`
  - [ ] Ownership check with constant-time 404 per AR-016
  - [ ] Query: `SELECT * FROM audit_log WHERE entity_type = 'journal_entry' AND entity_id = @id ORDER BY recorded_at ASC`
- [ ] **T2 (AC: 1)** — `JournalEntryHistoryResponse` sealed record + `AuditLogEntry` sub-record
- [ ] **T3 (AC: 3)** — `ProcessingActivityLabeler` utility — maps `OwnerLedger → "Owner ledger"`, `DebtorConfirmation → "Recipient confirmed"` (Transloco key mapping)
- [ ] **T4 (AC: 4)** — `ActorLabelResolver` — checks actor subject ID's account status
  - [ ] If account is anonymized (per Story 1.7 tombstone flag) OR entry older than 7 years (AR-034): return opaque `User-{hash-suffix}`
  - [ ] Else: look up actor's display name via Auth0 profile cache
- [ ] **T5 (AC: 4)** — Redact PII in `before`/`after` snapshots for anonymized actors — replace name/phone/email/notes field values with `"[redacted]"` before returning

### Frontend

- [ ] **T6 (AC: 2, 3, 6, 7)** — Create `apps/owner-workspace/src/app/features/debts/history/debt-history.component.ts`
  - [ ] Route `/contacts/:contactId/debts/:debtId/history`
  - [ ] `rxResource()` loads the endpoint
  - [ ] Timeline layout via `<p-timeline>` (PrimeNG v21); event templates rendering each entry
  - [ ] Diff view component `<ow-audit-diff [before]="..." [after]="...">` with side-by-side / stacked responsive logic
  - [ ] "Recipient confirmed" badge via `<p-tag severity="info">`
- [ ] **T7 (AC: 2)** — Deleted entries rendered with strikethrough (CSS `text-decoration: line-through`) on the diff fields
- [ ] **T8 (AC: 8)** — Telemetry emit on route activation

### Tests

- [ ] **T9 (AC: 1, 5)** — `GetJournalEntryHistoryHandlerTests` — happy path + ownership 404 + anonymized-actor path + old-entry path
- [ ] **T10 (AC: 1, 4)** — Integration test (Testcontainers)
  - [ ] Seed debt with created/edited/deleted sequence
  - [ ] GET returns all 3 entries in correct order
  - [ ] Assert `User-xxxx` opaque label for seeded anonymized actor
  - [ ] Assert `[redacted]` PII for anonymized actor's before/after
- [ ] **T11 (AC: 2, 6, 7)** — Vitest: diff view renders correctly; "Recipient confirmed" badge; strikethrough on deleted
- [ ] **T12 (AC: 7)** — Playwright E2E — Arabic + English audit flow; axe a11y

## Dev Notes

### Critical guardrails

1. **Storage primitive is owned by Story 0.6.** This story is a READ surface only — no new audit-writing logic. The append-only trigger on `audit_log` is the compliance backbone.
2. **Opaque identifiers for old/anonymized entries.** Deterministic hash suffix (4 chars from SHA256) balances "same actor across entries is visibly the same" with "no PII re-exposure".
3. **Redaction of PII in snapshots for anonymized actors.** The `before`/`after` JSON is walked; any field matching the PII list (`name`, `phone`, `email`, `notes`) is replaced with `"[redacted]"`. Structural visibility preserved; data hidden.
4. **Constant-time 404 on ownership miss** — AR-016 enumeration-oracle defense. Hash lookup + query always runs.
5. **No undo/restore affordances.** FR49 immutability at MVP. If a UX requirement for "restore deleted" emerges later, it's a separate flow that WRITES a new `create` action, not an audit mutation.
6. **ProcessingActivity.DebtorConfirmation path is wired but MVP-stubbed.** Epic 4 activates recipient-confirmation entries; this story's rendering supports them when they appear.
7. **Audit entries older than 7 years are still returned** (Postgres table retains them), but their actor labels opaque-transform per AR-034. No automatic purge job yet — that's a later compliance story.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/JournalEntries/History/
│   ├── GetJournalEntryHistoryResponse.cs             # NEW
│   ├── AuditLogEntry.cs                              # NEW (response sub-record)
│   ├── GetJournalEntryHistoryHandler.cs              # NEW
│   └── ActorLabelResolver.cs                         # NEW
├── src/Faktuboh.Application/Audit/
│   └── ProcessingActivityLabeler.cs                  # NEW
└── tests/Faktuboh.Api.Tests/Slices/JournalEntries/History/
    ├── GetJournalEntryHistoryHandlerTests.cs         # NEW
    └── GetJournalEntryHistoryEndpointTests.cs        # NEW

frontend/
├── apps/owner-workspace/src/app/features/debts/history/
│   ├── debt-history.component.ts                     # NEW
│   ├── debt-history.component.html                   # NEW
│   ├── debt-history.component.scss                   # NEW (logical props)
│   ├── debt-history.component.spec.ts                # NEW
│   └── audit-diff.component.ts                       # NEW (sub-component)
```

### Diff view shape (reference)

```typescript
type AuditDiffProps = {
  before: Record<string, unknown> | null;
  after:  Record<string, unknown> | null;
  fieldsChanged: string[]; // derived; drives highlighting
};
```

### Testing standards

Per Story 3.2 (which creates the audit-log rows this story reads). **Unique invariant: opaque-actor label determinism** — same actor subject ID always produces the same `User-xxxx` suffix; different actors produce different suffixes with negligible collision probability.

### Project Structure Notes

`Slices/JournalEntries/History/` — the audit surface is journal-entry-scoped (one debt's history), not contact-scoped. Story 3.4 (contact history with date filter) is a different surface with different response shape. This distinction is deliberate per §5.3.2 one-folder-per-action.

### Architecture compliance

- **FR49** user-visible audit surface (read-only)
- **AR-016** constant-time 404
- **AR-018** PII redaction in anonymized snapshots
- **AR-034** 7-year retention + opaque-identifier rendering
- **ADR-022** append-only `audit_log` (Story 0.6 primitive consumed)
- **UX-DR12** (confirmed-entry labeling)

### Library/framework requirements

Per Story 3.1. Additional: PrimeNG `<p-timeline>`, `<p-tag>` — query via `@primeng/mcp`.

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions; actor-label determinism tests |
| Integration | Testcontainers Postgres + seeded audit history |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — Arabic + English; axe a11y |

### Previous Story Intelligence

- **Story 0.6** (bitemporal + append-only audit_log + DB trigger) — storage primitive
- **Story 1.7** (GDPR erasure) — sets the anonymization flag this story reads
- **Story 3.2** (edit debt) — writes audit_log rows with full before/after snapshots
- **Story 3.3** (delete debt) — writes audit_log row with `before` snapshot + `after: null`
- **Story 3.1** (create debt) — writes initial audit_log row with `before: null`
- Epic 4 will add `ProcessingActivity.DebtorConfirmation` entries; this story's rendering supports them

## References

- [epics.md Story 3.8](../planning-artifacts/epics.md)
- [architecture.md §3.5, §4 D4.9](../planning-artifacts/architecture.md)
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [prd.md FR49](../planning-artifacts/prd.md)
- [ux-design-specification.md UX-DR12](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
