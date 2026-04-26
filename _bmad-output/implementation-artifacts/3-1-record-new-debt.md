# Story 3.1: Record new debt (lent or borrowed)

Status: ready-for-dev

## Story

As a **user recording a debt with an existing contact**,
I want to **tap the mobile GlanceFab (SpeedDial) or the column-level "+" on desktop, select contact + amount + denomination + direction (lent/borrowed) + optional note, and have it appear in my Glance immediately**,
so that **"in the moment" capture completes in under 30 seconds (FR17) and under 500ms server-side (NFR-P4)**, the row slots into the correct Direction 6 column (لي or عليّ) with subtle 120ms fade-in, and **no toast, no sound, no animation flourish** signals the save — the row appearing IS the feedback (UX-DR27).

**Covers requirements:** FR13 (record debt), FR17 (30-second capture target), NFR-P4 (500ms end-to-end), UX-DR14 (GlanceFab), UX-DR5 (AmountDisplay), UX-DR8 (DenominationPicker), UX-DR27 (silent success), UX-DR31 (loading + rollback on failure).

**Why this slice is high-frequency:** Debt recording is the daily action at the heart of the app. Every other Epic 3 story depends on this one's shape — 3.2 (edit), 3.3 (delete), 3.4 (history), 3.5 (Glance) all consume its write surface. The slice also introduces `JournalEntry` as a standalone aggregate (Story 2.1 created the opening-balance variant; this is the general-case writer).

## Acceptance Criteria

### AC1 — POST creates debt with bitemporal history

**Given** an authenticated user with at least one existing contact
**When** the user submits `POST /v1/journal-entries` with body `{ contactId, amount: "300.00", denominationCode: "USD", direction: "receivable", occurredOn: "2026-04-22", note?: string }` + `Idempotency-Key` header (UUID v4)
**Then** a `journal_entries` row is written atomically with `valid_from = now()`, `valid_to = null`, `recorded_at = now()`
**And** the bitemporal interceptor readies future UPDATE/DELETE to populate `journal_entries_history`
**And** the response returns `201 Created` with `CreateJournalEntryResponse` containing the entry ID, contact ID, echo of submitted fields, and server-computed balance deltas per denomination
**And** `JournalEntryCreated` integration event emits via Wolverine same-transaction outbox (the Glance projection consumes it)
**And** end-to-end response time p95 < 500ms per NFR-P4

### AC2 — Idempotency-Key double-submit protection

**Given** the user double-taps the save button on mobile (common failure mode)
**When** two concurrent `POST`s carry the same `Idempotency-Key`
**Then** exactly one handler execution completes; the second blocks on the row-level lock on `idempotency_keys` and returns the cached response with `returnedExisting: true`
**And** the integration test `ConcurrentDoubleSubmitTest` asserts this

### AC3 — RFC 9457 on validation failure

**Given** invalid input (amount ≤ 0, unsupported currency, nonexistent contactId, contact belonging to another user, direction not in `{receivable, payable}`)
**When** FluentValidation runs
**Then** response is `4xx` RFC 9457 with JSON Pointers (`/amount`, `/denominationCode`, `/contactId`, `/direction`) walked onto Signal Forms via `applyServerErrors(form, problemDetails)`
**And** `ErrorCatalog` contains:
  - `faktuboh.journal-entries.amount_invalid` (400)
  - `faktuboh.journal-entries.contact_not_found_or_forbidden` (404 — constant-time response to avoid enumeration oracle)
  - `faktuboh.journal-entries.denomination_not_supported` (400)
  - `faktuboh.journal-entries.direction_invalid` (400)

### AC4 — GlanceFab + column-level "+" wire the form

**Given** the user is on `/dashboard`
**When** on mobile (<768px) the user taps the `GlanceFab` (PrimeNG `<p-speedDial>` wrapped in Epic 1's `glance-fab` component per UX-DR14) and selects "Record new debt"
**Then** a `<p-drawer position="bottom">` opens containing the debt-entry form
**And** on desktop (≥1024px) a "+" `<p-button>` in each column header opens the same form as a `<p-dialog>` pre-selected with that column's direction
**And** the form layout uses `IftaLabel` (≥1024px) / stacked `FloatLabel variant="on"` (<1024px) per UX-DR28

### AC5 — Form field composition

**Given** the debt-entry form renders
**When** the user interacts with it
**Then** the fields render with exact PrimeNG components:
  - **Contact:** `contact-autocomplete` (from Story 2.5) — `<p-autoComplete>` wrapper rendering ContactAvatar + name; "Add \"{query}\" as new contact" footer deep-links to `/contacts/new?name={query}&returnTo=/dashboard/new-debt`
  - **Amount:** `<p-inputNumber mode="decimal" [minFractionDigits]="0" [maxFractionDigits]="4" [useGrouping]="true">` with locale-appropriate grouping separators (comma for English, Arabic-Indic digits are NOT auto-converted — keep Western digits per accounting conventions)
  - **Denomination:** `DenominationPicker` from `libs/domain-ui` (Epic 1 Story 1.9) with "Recent" section showing the user's 3 most-used denominations (cached via a lightweight GET `/v1/users/me/denominations/recent`) + "All" section sorted alphabetically
  - **Direction:** `<p-selectButton [options]="directionOptions">` with two segments: "لي" (receivable — I'm owed) / "عليّ" (payable — I owe), pre-selected from the column that opened the form
  - **Date:** `<p-datePicker>` defaulting to today, Gregorian + Hijri toggle (Hijri read-only) per NFR-A9(f)
  - **Note:** `<p-textarea [autoResize]="true" [maxlength]="500">` optional, labeled "(optional)"
**And** no red asterisks on required fields (required is default, optional labeled "(optional)") per UX-DR28
**And** submit button uses `loading=true` during network with frozen width

### AC6 — Optimistic UI with rollback on failure

**Given** the user submits
**When** the network is in flight
**Then** the row appears immediately in the correct Direction 6 column with `opacity: 0.6` and a `<p-progressSpinner size="small">` overlay — the "saving…" signal per UX "Server-authoritative writes" pattern
**And** on 201 response, the overlay fades out over 120ms; the row fully renders; the drawer/dialog closes silently (no toast — UX-DR27)
**And** on 4xx/5xx response, the optimistic row fades out; the form drawer re-opens with validation messages + a persistent `<p-toast severity="error" sticky="true">` with Retry action per UX-DR31
**And** on network failure specifically, the form preserves the user's draft (does not clear) and the toast reads "Couldn't save — check your connection" with Retry

### AC7 — Glance re-render on success

**Given** the `JournalEntryCreated` integration event emits
**When** the Debts slice's projection handler processes it
**Then** the Glance's `rxResource()` backing `GET /v1/dashboard` invalidates (either via signal-refresh on the message arriving at the client via AR-047 SSE primitive in Phase-A, OR via simple router re-navigation on form success — **Phase-A default: the form closes → triggers `dashboardResource.reload()` on the parent signal; SSE push is deferred**)
**And** the row appears in the correct column with a 120ms fade-in animation
**And** the HeroTile sum updates with tabular-numeral transition (no animated counters — Stability Signals Trust principle)

### AC8 — Telemetry events

**Given** AR-047 telemetry primitive active
**When** the debt records successfully
**Then** if this is the user's first debt ever, emit `first_debt_recorded`; else emit `debt_recorded`
**And** payload is PII-safe: `{ opaque_user_id, contact_id_hash, denomination_code, direction, amount_bucket: "<100|100-1000|1k-10k|10k+", is_first: bool }`
**And** NO amount values, notes, or contact identifiers in plaintext

### AC9 — ProcessingActivity + audit

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** the write commits
**Then** the audit log row (Story 0.6 primitive) captures `{ actor_subject_id, action: "debt.created", entity_id, before: null, after: <snapshot>, source_ip, processing_activity }`
**And** Serilog + OTel include `ProcessingActivity` structured property

### AC10 — A11y + RTL gate

**Given** the form renders
**When** axe-core runs in E2E
**Then** zero WCAG 2.1 AA violations
**And** manual VoiceOver Arabic + TalkBack Arabic walk-through passes (NFR-A9 RTL gate scenario)
**And** keyboard navigation completes the flow without touching mouse (Tab through all fields → Enter submits)

## Tasks / Subtasks

### Backend — Slice scaffold

- [ ] **T1 (AC: 1, 3, 9)** — Create vertical slice at `backend/src/Faktuboh.Api/Slices/JournalEntries/Create/`
  - [ ] `CreateJournalEntryRequest.cs` — sealed record `(Guid ContactId, decimal Amount, string DenominationCode, Direction Direction, DateOnly OccurredOn, string? Note)`
  - [ ] `CreateJournalEntryResponse.cs` — sealed record `(Guid Id, Guid ContactId, decimal Amount, string DenominationCode, Direction Direction, DateOnly OccurredOn, DateTimeOffset RecordedAt)` + factory `From(JournalEntry)`
  - [ ] `CreateJournalEntryRequestValidator.cs` — rules: amount > 0, denomination in `CurrencyRegistry`, direction enum parse, `occurredOn <= today + 1 day` (allow tomorrow for timezone edge), note ≤ 500 chars
  - [ ] `CreateJournalEntryHandler.cs` — Wolverine handler signature `public async Task<CreateJournalEntryResponse> Handle(...)` that (a) verifies contact ownership via `IContactRepository.GetByIdAsync(contactId, ownerSubjectId)`, (b) constructs `Money(amount, denominationCode)`, (c) calls `JournalEntry.Create(...)` static factory, (d) persists + publishes event
- [ ] **T2 (AC: 3)** — Create `JournalEntriesExceptionHandler` catching `JournalEntryException` base → RFC 9457
- [ ] **T3 (AC: 3)** — Register 4 error codes in `ErrorCatalog`
- [ ] **T4 (AC: 1, 9)** — Endpoint registration `Slices/JournalEntries/JournalEntries.Endpoints.cs` with `MapGroup("/v1/journal-entries").RequireAuthorization()` + `ProcessingActivity.OwnerLedger` endpoint filter

### Backend — Domain extensions

- [ ] **T5 (AC: 1)** — Extend `JournalEntry` aggregate from Story 2.1
  - [ ] Add `JournalEntry.Create(Guid contactId, Money amount, Direction direction, DateOnly occurredOn, string? note, Guid ownerSubjectId)` static factory (alongside existing `CreateOpeningBalance`)
  - [ ] Raise `JournalEntryCreatedEvent` with all fields needed for Glance projection
- [ ] **T6 (AC: 1)** — Verify `IJournalEntryRepository` supports `AddAsync` + `GetByIdAsync`; no changes expected from Story 2.1

### Backend — Projection for Glance

- [ ] **T7 (AC: 7)** — Replace the no-op `ContactCreatedHandler` stub from Story 2.1 with a real `JournalEntryCreatedHandler` in `Slices/Debts/Projections/`
  - [ ] For Phase-A: handler logs the event; no secondary projection table (architecture §4 D4.4 — no CQRS at MVP; primary store is the query)
  - [ ] Fitness test #3 still validates cross-slice-via-IntegrationEvents
- [ ] **T8 (AC: 8)** — Emit `debt_recorded` or `first_debt_recorded` telemetry via AR-047 primitive; detection via `IJournalEntryRepository.CountForOwnerAsync(ownerSubjectId) == 1` post-insert

### Backend — User's recent denominations endpoint

- [ ] **T9 (AC: 5)** — Create `Slices/Users/GetRecentDenominations/GetRecentDenominationsHandler.cs`
  - [ ] Endpoint `GET /v1/users/me/denominations/recent?limit=3`
  - [ ] Query: `SELECT denomination_code, COUNT(*) FROM journal_entries WHERE owner_subject_id = @id AND valid_to IS NULL GROUP BY denomination_code ORDER BY COUNT(*) DESC LIMIT 3`
  - [ ] Returns `RecentDenominationsResponse { codes: string[] }`

### Frontend — Feature slice

- [ ] **T10 (AC: 4, 5, 6, 10)** — Create `apps/owner-workspace/src/app/features/debts/record/record-debt.component.ts`
  - [ ] Standalone + signals + OnPush; zoneless
  - [ ] Signal Form via `FormMutationConfig`; fields as in AC5
  - [ ] Consumes: `ContactAutocomplete` (Story 2.5), `DenominationPicker` (Story 1.9), `AmountDisplay` for preview
  - [ ] Route `/dashboard/new-debt` + deep link `?contactId=...&direction=receivable` for column-level "+" buttons
- [ ] **T11 (AC: 4)** — Wire `GlanceFab` (from Epic 1 Story 1.9) to route `/dashboard/new-debt`
  - [ ] Mobile: SpeedDial opens `<p-drawer position="bottom">`; desktop: "+" button opens `<p-dialog>`
  - [ ] Responsive detection via `@media` breakpoints + Angular `BreakpointObserver` signal
- [ ] **T12 (AC: 6)** — Optimistic UI logic
  - [ ] On submit: add a `PendingJournalEntry` to the parent Glance's signal with `status: 'saving'`; server response promotes to `status: 'saved'` or removes on error
  - [ ] Rollback animation: `opacity: 0 → 0.6 → 0` over 120ms
- [ ] **T13 (AC: 6)** — Error toast via `MessageService.add({ severity: 'error', sticky: true, detail: '...', life: null })` with Retry action per UX-DR31
  - [ ] Network-failure branch (no response) vs server-4xx branch (response with problem-details body) handled differently per UX-DR31 copy
- [ ] **T14 (AC: 5)** — Generated TS client `libs/core/api/journal-entries.client.ts` (NSwag-generated per delegated decision; until generator lands, handwritten typed wrapper with `// TODO: regenerate` comment)

### Tests — Backend

- [ ] **T15 (AC: 1, 3, 9)** — `CreateJournalEntryHandlerTests` — happy path + 4 RFC 9457 negatives
- [ ] **T16 (AC: 1-3, 7, 9, 10)** — `CreateJournalEntryEndpointTests` (Integration, Testcontainers Postgres + real Wolverine)
  - [ ] Happy path: 201; row in DB; history interceptor wired; event consumed by `JournalEntryCreatedHandler` (test-only handler asserts shape, not just delivery per Murat's landmine)
  - [ ] Concurrent double-submit with same Idempotency-Key → single execution
  - [ ] `AsOf(t)` time-travel post-insert shows the row only at `t >= valid_from`
  - [ ] Contact-ownership forbidden path → 404 constant-time (two test runs: existing-but-forbidden vs nonexistent — measure timing diff < 5ms)
  - [ ] p95 < 500ms over 100 iterations
- [ ] **T17 (AC: 8)** — Verify snapshot of OpenAPI for this endpoint
- [ ] **T18 (AC: 8)** — FsCheck property test: `amount_bucket` classification is correct across the full decimal domain

### Tests — Frontend

- [ ] **T19 (AC: 5, 6, 10)** — Vitest + Angular Testing Library
  - [ ] Form submits with Idempotency-Key header
  - [ ] Optimistic row appears immediately; rollback on 400 response
  - [ ] `DenominationPicker` Recent section renders user's top-3
  - [ ] Direction pre-selection from query param works
- [ ] **T20 (AC: 4, 6, 10)** — Playwright E2E (Chromium + WebKit)
  - [ ] Desktop: "+" in receivable column → dialog pre-selected `receivable` → save → row appears
  - [ ] Mobile viewport (iPhone 14): GlanceFab → drawer → save → drawer closes silently
  - [ ] Arabic locale: full flow in RTL; axe a11y; visual regression

## Dev Notes

### Critical guardrails (do not violate)

1. **Silent success ALWAYS (UX-DR27).** No `MessageService.add({severity:'success'})` anywhere in this slice. The row appearing in the column IS the feedback. Only errors get toasts.
2. **Server-authoritative writes.** Optimistic UI shows the row immediately but with `opacity: 0.6` + spinner overlay — the user reads this as "saving…". Do NOT flip to full-opacity until the 201 response arrives. On error, the row fades out with the form re-opening.
3. **Idempotency-Key regenerated per form render.** UUID v4 generated on form mount; refreshed on success/reset. Never reuse.
4. **Contact ownership check is a 404, not 403.** Returning 403 leaks contact existence. The handler must return the same 404 shape with same-timing response (hash lookup + DB query always executed, even when the eventual answer is "forbidden") per AR-016 constant-time philosophy.
5. **No amount in telemetry.** `amount_bucket` is the one allowed coarsening. NetArchTest verifies no `Amount` property appears on `Telemetry*` event types.
6. **Direction enum is backend-source-of-truth.** Frontend passes `"receivable"` or `"payable"` strings; backend parses into `Direction` enum. Any other value → RFC 9457 `direction_invalid`.
7. **Column pre-selection via query param.** The "+" buttons use `routerLink="/dashboard/new-debt" [queryParams]="{direction: 'receivable'}"` — the form reads `route.queryParamMap` on init and pre-selects.
8. **Note field is PII when it exists.** Store encrypted at rest per AR-018 (key management layers in at Epic 5; MVP acceptable state: plaintext Postgres column with a `// TODO: encrypt in Epic 5` comment on the EF configuration).

### Source tree — files to create or touch

```
backend/
├── src/Faktuboh.Api/Slices/JournalEntries/
│   ├── JournalEntries.Endpoints.cs                        # NEW
│   ├── JournalEntriesExceptionHandler.cs                  # NEW
│   └── Create/
│       ├── CreateJournalEntryRequest.cs                   # NEW
│       ├── CreateJournalEntryResponse.cs                  # NEW
│       ├── CreateJournalEntryRequestValidator.cs          # NEW
│       └── CreateJournalEntryHandler.cs                   # NEW
├── src/Faktuboh.Api/Slices/Users/GetRecentDenominations/
│   └── GetRecentDenominationsHandler.cs                   # NEW
├── src/Faktuboh.Api/Slices/Debts/Projections/
│   └── JournalEntryCreatedHandler.cs                      # NEW (replaces stub from Story 2.1)
├── src/Faktuboh.Domain/JournalEntries/
│   ├── JournalEntry.cs                                    # MODIFY — add Create factory
│   └── JournalEntryException.cs                           # NEW (base exception)
├── src/Faktuboh.Application/Errors/ErrorCatalog.cs        # MODIFY — add 4 new codes
└── tests/Faktuboh.Api.Tests/Slices/JournalEntries/Create/
    ├── CreateJournalEntryHandlerTests.cs                  # NEW
    └── CreateJournalEntryEndpointTests.cs                 # NEW (Integration)

frontend/
├── apps/owner-workspace/src/app/features/debts/record/
│   ├── record-debt.component.ts                           # NEW
│   ├── record-debt.component.html                         # NEW
│   ├── record-debt.component.scss                         # NEW (logical props only)
│   └── record-debt.component.spec.ts                      # NEW
├── apps/owner-workspace/src/app/features/dashboard/
│   ├── dashboard.component.ts                             # MODIFY — wire GlanceFab + column "+"
│   └── dashboard.routes.ts                                # MODIFY — add new-debt child route
└── libs/core/api/
    └── journal-entries.client.ts                          # NEW (generated or thin wrapper)
```

### Testing standards (per architecture §5.3.3)

| Level | Tools |
|---|---|
| **Unit** | xUnit v3 + FluentAssertions + NSubstitute + FsCheck (amount_bucket, Money arithmetic) |
| **Integration** | `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine — tagged `[Trait("Category", "Integration")]` |
| **Snapshot** | Verify for OpenAPI + per-endpoint JSON |
| **E2E** | Playwright Chromium + WebKit; axe-core; visual regression LTR + RTL |

**Murat's landmines addressed:**
1. Integration test asserts event CONSUMPTION shape, not just publication
2. Idempotency test exercises actual concurrency (two parallel HttpClient calls)
3. Optimistic-UI test includes rollback branch — not just happy path
4. Contact-ownership test measures timing diff between "exists but forbidden" vs "nonexistent" — must be < 5ms

### Project Structure Notes

Debt-entry is the FIRST slice under `Slices/JournalEntries/`. Story 2.1's opening-balance variant lives in `Slices/Contacts/Create/` (it's a Contacts concern there). Going forward, general journal-entry CRUD lives in `Slices/JournalEntries/`. The Debts slice (`Slices/Debts/`) contains only projection handlers + the Glance read path — no primary writes. This separation keeps the action folder organization intent intact per §5.3.2.

### Architecture compliance

- **ADR-022** bitemporal — JournalEntry implements IBitemporal; history table written on mutation
- **ADR-024** Minimal APIs + RFC 9457 + FluentValidation + AddProblemDetails
- **AR-016** constant-time ownership check (404 with same-timing path)
- **AR-018** note is PII — encryption deferred to Epic 5
- **AR-047** telemetry primitive with PII-safe event schema
- **§4 D4.1** Postgres primary; **§4 D4.4** no CQRS (projection handler is logging-only)
- **§4 D4.11** Idempotency-Key
- **§4 D4.12** Wolverine Postgres transport + same-transaction outbox
- **§5.2.4** DateOnly for `occurredOn`; DateTimeOffset for `recordedAt`
- **§5.2.5** Money `numeric(19,4)` + decimal-string wire
- **UX-DR5** AmountDisplay; **UX-DR8** DenominationPicker; **UX-DR14** GlanceFab; **UX-DR27** silent success; **UX-DR28** IftaLabel/FloatLabel; **UX-DR31** loading + rollback
- **NFR-P4** 500ms p95 end-to-end
- **NFR-A9** RTL gate (scenarios d + e)

### Library/framework requirements

Per Story 2.1. Additional PrimeNG v21 components confirmed via `@primeng/mcp`:
- `<p-speedDial>` for GlanceFab wiring
- `<p-drawer>` (position="bottom") for mobile form
- `<p-dialog>` for desktop form
- `<p-selectButton>` for direction toggle
- `<p-inputNumber>` with mode="decimal"
- `<p-datePicker>` (replaces calendar in v21)
- `<p-textarea>`
- `<p-toast>` with sticky mode for error recovery

### Testing requirements

Per Story 2.1. Emphasis on the **optimistic-UI rollback path** — tests for happy path are necessary but insufficient; the rollback animation + draft preservation on network failure is where bugs hide.

### Previous Story Intelligence

- **Story 0.5** (Idempotency-Key) — middleware active for POST
- **Story 0.6** (IBitemporal interceptor) — JournalEntry history writes
- **Story 0.7** (ProcessingActivity) — endpoint filter stamps OwnerLedger
- **Story 0.14** (AR-047 telemetry) — `first_debt_recorded` / `debt_recorded` events
- **Story 0.15** (ICompoundJournalEntry seam) — NOT consumed in this slice (Epic 5 Story 5.2 first consumer; but the `JournalEntry.Create` factory must leave a seam for compound inputs that lands later)
- **Story 1.9** (Phase-1 components) — `GlanceFab`, `DenominationPicker`, `AmountDisplay`, `ContactAvatar` all consumed unchanged
- **Story 2.1** (Contacts/Create) — `JournalEntry` aggregate + schema + `ContactCreated` event shape established; this story extends the aggregate with general `Create` factory
- **Story 2.5** (Contact search) — `ContactAutocomplete` consumed in the contact field
- **Story 3.5** depends on this story's `JournalEntryCreatedEvent` shape — coordinate payload at slice commit

## References

- [epics.md Story 3.1](../planning-artifacts/epics.md)
- [architecture.md §4 D4.1, D4.4, D4.11, D4.12](../planning-artifacts/architecture.md)
- [architecture.md §5.2.4, §5.2.5](../planning-artifacts/architecture.md)
- [architecture.md §5.3.1, §5.3.2](../planning-artifacts/architecture.md)
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md)
- [prd.md FR13, FR17, NFR-P4, NFR-A9](../planning-artifacts/prd.md)
- [ux-design-specification.md UX-DR5, DR8, DR14, DR27, DR28, DR31](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md Flows 1 and 2, "Effortless Interactions"](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
