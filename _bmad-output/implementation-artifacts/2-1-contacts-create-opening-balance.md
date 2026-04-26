# Story 2.1: Contacts/Create with opening-balance (§7.5.2 first slice)

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a **new Faktuboh user recording my first relationship**,
I want to **create a contact with an optional opening balance captured atomically in the same operation**,
so that I can **express "Ahmed owes me $300 as of today" in a single form**, and the architecture's first slice (§7.5.2) exercises Money VO + `Idempotency-Key` + bitemporal + RFC 9457 + cross-slice integration event end-to-end.

**Covers requirements:** FR8 (add contact), FR13 (opening-balance variant — spans Epic 3 for general record-debt flow).

**Why this slice first:** Per architecture §7.5.2, this is the *deliberately-designed first slice* for the MVP. It is not the smallest slice — it is the slice that makes the scaffold's four NetArchTest fitness tests + Money VO + idempotency-with-financial-consequence + bitemporal-write + RFC 9457 emission + cross-slice `ContactCreated` integration event with a no-op Debts projection handler all come alive in a single merge. Shipping a simpler Contacts/Create without opening-balance would leave the scaffold's compound invariants unasserted.

## Acceptance Criteria

### AC1 — Create contact + opening-balance atomically

**Given** an authenticated user on `/contacts/new`
**When** the user enters name + optional phone/email/notes + optional opening balance `{ amount: "300.00", currency: "USD", direction: "receivable" }` + submits with an `Idempotency-Key` header (UUID v4)
**Then** `POST /v1/contacts` creates the `contacts` row and atomically writes the opening-balance `journal_entries` row as a bitemporal history-tracked record in a single DB transaction
**And** the response returns `201 Created` with `CreateContactResponse` including contact ID + opening-balance entry ID
**And** omitting the `openingBalance` field produces a contact-only creation (no journal entry) and the operation still succeeds with `201`

### AC2 — Idempotency across concurrent double-submit

**Given** the `Idempotency-Key` middleware is active on `POST /v1/contacts`
**When** two concurrent requests submit the same key (e.g., form double-tap on mobile)
**Then** only one handler execution completes; the second request blocks on the row-level lock on the `idempotency_keys` row and returns the cached response
**And** a key-replay-after-success returns the same response body with `returnedExisting: true` flag in the response envelope
**And** a concurrent-double-submit integration test asserts the single-execution invariant

### AC3 — RFC 9457 error response on validation failure

**Given** an authenticated user submits invalid input (e.g., duplicate contact name for this user, opening-balance amount negative, unsupported currency code)
**When** the endpoint validates via FluentValidation
**Then** the response is HTTP 4xx with `Content-Type: application/problem+json` per RFC 9457
**And** the body contains `type`, `title`, `status`, `detail`, `instance`, `traceId`, `code` (in form `faktuboh.contacts.<condition>`), and `errors[]` with JSON Pointers (`/name`, `/openingBalance/amount`, etc.) walked onto Signal Forms fields on the frontend
**And** NetArchTest asserts that every code thrown in this slice exists in `ErrorCatalog`

### AC4 — Cross-slice integration event emitted and consumed

**Given** the slice commits successfully
**When** the transaction finalizes
**Then** a `ContactCreated` integration event is published via Wolverine with same-transaction outbox guarantee (no enqueue on aborted transaction)
**And** the no-op `ContactCreatedHandler` in the Debts slice (projection handler stub) receives the event and logs it
**And** a `ContactCreatedEvent` is also published when `openingBalance` is provided (event payload includes contact ID + opening-balance entry ID for downstream Debts slice consumption)
**And** the fitness test "cross-slice communication only via IntegrationEvents" (AR-037c) passes against this slice

### AC5 — Bitemporal history rows written

**Given** the `contacts` and `journal_entries` tables implement `IBitemporal`
**When** the contact or the opening-balance entry is later updated/deleted
**Then** exactly one row is inserted into `contacts_history` / `journal_entries_history` per mutation (audit round-trip fitness test asserts)
**And** the `RecordedAt` column is stamped with the mutation wall-clock
**And** repositories expose `AsOf(DateTimeOffset)` query extension returning the view at any past wall-clock

### AC6 — Money VO enforces dual-precision + currency registry

**Given** the Money value object `record Money(decimal Amount, string Currency)`
**When** the opening balance is constructed in the handler
**Then** currency is validated against `CurrencyRegistry` (`EUR`, `USD`, `GBP`, `AED`, `SAR`, `EGP`, `JOD`, `KWD`, `BHD`, `TND` + metals as initial set)
**And** Postgres column `journal_entries.amount` is `numeric(19, 4)` (user-facing precision per AR-035)
**And** if internal FX pivots are introduced downstream, a separate `numeric(28, 8)` column pattern applies (not in this slice)
**And** FsCheck property-based tests assert `serialize → deserialize → arithmetic → serialize` round-trip is exact for all currencies in the registry

### AC7 — ProcessingActivity ambient context stamped

**Given** the endpoint sets `ProcessingActivity.OwnerLedger` at its `MapGroup` endpoint filter
**When** the handler writes rows
**Then** every Serilog log record includes `ProcessingActivity` as structured property
**And** every OTel span includes `ProcessingActivity` as span attribute
**And** the audit log row has non-null `ProcessingActivity` column
**And** the outgoing `ContactCreated` Wolverine message has `ProcessingActivity` as a header re-established on the receiving side
**And** NetArchTest asserts this invariant across the slice

### AC8 — Frontend form integration

**Given** the Angular owner-workspace at `/contacts/new`
**When** the Signal Form submits via `FormMutationConfig`
**Then** the `Idempotency-Key` UUID v4 header is attached via HTTP interceptor
**And** RFC 9457 error response's `errors[]` JSON Pointers are walked onto Signal Forms nodes via `applyServerErrors(form, problemDetails)` helper
**And** validation messages are localized via Transloco + `ValidationCatalog` (Arabic + English)
**And** `DenominationPicker` (Phase-1 UX component from Epic 1) is consumed from `libs/domain-ui` unchanged (single-owner rule)
**And** `ContactAvatar` renders post-creation with deterministic tint from hash of contact ID
**And** the AC-A11y gate: axe-core reports zero WCAG 2.1 AA violations on this form; manual screen-reader test pass on VoiceOver Arabic + TalkBack Arabic per NFR-A9

### AC9 — Telemetry event emitted

**Given** AR-047 Phase-A telemetry primitive is wired
**When** the contact is created
**Then** a `contact_created` event emits to App Insights with PII-safe schema (`opaque_user_id`, `contact_id_hash`, `opening_balance_present: bool`)
**And** no PII fields (name, phone, email) appear in the event (NetArchTest asserts)
**And** integration test helper `AssertEventEmitted("contact_created")` passes

### AC10 — Performance target met

**Given** the full POST→persist→integration-event→response flow
**When** measured end-to-end
**Then** p95 response time is < 500ms per NFR-P4 against the Testcontainers Postgres baseline
**And** the integration test suite asserts this via `[Fact]` with timing measurement

## Tasks / Subtasks

### Backend — Slice scaffold

- [ ] **T1 (AC: 1, 3, 4, 6, 7)** — Create vertical slice at `backend/src/Faktuboh.Api/Slices/Contacts/` with per-architecture structure
  - [ ] Create `Contacts.Endpoints.cs` with `MapGroup("/v1/contacts")` + `.RequireAuthorization()` + `ProcessingActivity.OwnerLedger` endpoint filter
  - [ ] Create `Create/CreateContactRequest.cs` — sealed record with `Name`, `Phone?`, `Email?`, `Notes?`, `OpeningBalance?` (nested `Money` + direction `receivable|payable`)
  - [ ] Create `Create/CreateContactResponse.cs` — sealed record with `Id`, `CreatedAt`, `OpeningBalanceEntryId?` + static factory `From(Contact, JournalEntry?)`
  - [ ] Create `Create/CreateContactRequestValidator.cs` — `AbstractValidator<CreateContactRequest>` — rules for name non-empty, max length, opening-balance amount positive if present, currency in `CurrencyRegistry`
  - [ ] Create `Create/CreateContactHandler.cs` — Wolverine handler with `public async Task<CreateContactResponse> Handle(...)` signature (Wolverine discovery)
  - [ ] Register endpoint in `Program.cs` via `app.MapContactsEndpoints()`

### Backend — Domain layer

- [ ] **T2 (AC: 6)** — Ensure `Money` value object exists in `Faktuboh.Domain/Primitives/` with constructor validation against `CurrencyRegistry`
  - [ ] Verify `Money` is already in Shared Kernel per AR-001 (Epic 0 Story 0.1) or add it if missing
  - [ ] Add `CurrencyRegistry` static class with initial fiat set (EUR, USD, GBP, AED, SAR, EGP, JOD, KWD, BHD, TND) mapped to minor-units
  - [ ] Add unit tests with FsCheck property-based round-trip tests per AC6
- [ ] **T3 (AC: 1, 4)** — Create `Contact` aggregate root in `Faktuboh.Domain/Contacts/`
  - [ ] `Contact` implements `IBitemporal` (has `ValidFrom`, `ValidTo?`, `RecordedAt`)
  - [ ] `Contact.Create(Guid ownerId, string name, ...)` static factory method
  - [ ] Domain event `ContactCreatedEvent` in `Faktuboh.Domain.Events.IntegrationEvents/` namespace (cross-slice-allowed)
  - [ ] Repository interface `IContactRepository` with `GetByIdAsync`, `AddAsync`, `AsOf(DateTimeOffset)`
- [ ] **T4 (AC: 1, 5)** — Ensure `JournalEntry` aggregate exists in `Faktuboh.Domain/JournalEntries/`
  - [ ] If not yet created (it may be since Epic 3 stories haven't shipped yet), create it with `IBitemporal` support
  - [ ] `JournalEntry.CreateOpeningBalance(Guid contactId, Money amount, Direction direction, ...)` static factory
  - [ ] Integration event `JournalEntryCreatedEvent` in `IntegrationEvents/` namespace

### Backend — Infrastructure layer

- [ ] **T5 (AC: 1, 5)** — Add EF Core entity configurations in `Faktuboh.Infrastructure/Persistence/Configurations/`
  - [ ] `ContactConfiguration` with `contacts` table + `numeric(19,4)` on any amount columns + `char(3)` on currency with regex check constraint + `snake_case` naming (enforced by `EFCore.NamingConventions`)
  - [ ] `JournalEntryConfiguration` similar — `journal_entries` table with `contact_id` FK
  - [ ] Migration `YYYYMMDDHHMMSS_AddContactsAndJournalEntries.cs` via `dotnet ef migrations add ...`
  - [ ] Migration also creates `contacts_history` + `journal_entries_history` tables per ADR-022 bitemporal pattern (the `IBitemporal` interceptor writes to these)

### Backend — Error handling

- [ ] **T6 (AC: 3)** — Register error codes in `ErrorCatalog` per D4.9
  - [ ] `faktuboh.contacts.duplicate_name_for_owner` (409)
  - [ ] `faktuboh.contacts.opening_balance_amount_invalid` (400)
  - [ ] `faktuboh.contacts.currency_not_supported` (400)
  - [ ] Each code has unique `id` GUID + `transloco_key` + `is_externally_visible: true` + HTTP status
- [ ] **T7 (AC: 3)** — Create slice-scoped `IExceptionHandler`: `ContactsExceptionHandler` catching `ContactException` base
  - [ ] Returns RFC 9457 problem details via `.NET 10 AddProblemDetails()` customizer that stamps `type = https://faktuboh.com/errors/{code}`
  - [ ] Validation failures route through shared `ValidationExceptionHandler` — one handler for all slices

### Backend — Integration event

- [ ] **T8 (AC: 4)** — Publish `ContactCreated` integration event via Wolverine outbox
  - [ ] Same-transaction outbox — event enqueued within the same `DbContext.SaveChangesAsync` transaction
  - [ ] Wolverine handler registered in `Faktuboh.Api` for discovery
- [ ] **T9 (AC: 4)** — Create no-op `ContactCreatedHandler` projection stub in `Slices/Debts/`
  - [ ] Logs receipt of event with contact ID
  - [ ] Fitness test #3 (AR-037c) now has a consumer to validate against
  - [ ] This stub is *intentionally minimal* — Epic 3 stories replace it with real projection logic

### Backend — Telemetry

- [ ] **T10 (AC: 9)** — Emit `contact_created` telemetry event via AR-047 primitive
  - [ ] `TelemetryEmitter.Emit("contact_created", new { opaque_user_id, contact_id_hash, opening_balance_present })`
  - [ ] NetArchTest verifies no PII fields appear in the event schema

### Frontend — Form + slice

- [ ] **T11 (AC: 8)** — Create Angular feature slice at `apps/owner-workspace/src/app/features/contacts/create/`
  - [ ] `create-contact.component.ts` with standalone + signal inputs + OnPush
  - [ ] Signal Form via `FormMutationConfig` — `generateIdempotencyKey` = UUID v4 per render, refreshed on success/reset
  - [ ] Consume `DenominationPicker` from `libs/domain-ui` unchanged (Epic 1 single-owner component API)
  - [ ] Form validation: client hints from FluentValidation schema (ValidationCatalog); server-authoritative final check
  - [ ] `applyServerErrors(form, problemDetails)` helper walks RFC 9457 JSON Pointers onto Signal Forms nodes
  - [ ] Transloco keys for every validation message (AR + EN)
  - [ ] `IftaLabel` ≥1024px / stacked `FloatLabel variant="on"` <1024px per UX-DR28
  - [ ] No red asterisks (required is default; optional fields labeled "(optional)")
  - [ ] Submit button `loading=true` during network; frozen width; RFC 9457 error → inline `<p-message severity="error">` at top of form body
- [ ] **T12 (AC: 8)** — Generated TypeScript client for `POST /v1/contacts`
  - [ ] Contracts generator (NSwag / openapi-typescript / Kiota — generator choice deferred to Step 5/6 ADR; if not yet decided, handwrite a typed thin wrapper over `HttpClient` that regenerates after the decision)
  - [ ] `libs/core/api/contacts.client.ts` exposes typed `createContact(request): Observable<CreateContactResponse>`
  - [ ] OpenAPI-drift gate baselines here (layered from Story 3.11 per Epic 0 deferral; if not active, commit the `openapi.json` snapshot for future activation)

### Tests — Backend

- [ ] **T13 (AC: 1-10)** — Unit tests in `tests/Faktuboh.Api.Tests/Slices/Contacts/Create/`
  - [ ] `CreateContactHandlerTests` — happy path + all RFC 9457 negative paths (duplicate name, invalid amount, unsupported currency)
  - [ ] `CreateContactRequestValidatorTests` — FluentValidation rules
  - [ ] `Money` VO rounding tests — use **explicit `MidpointRounding.ToEven` (banker's rounding)** and pin the enum; ESLint/Roslyn analyzer should fail if anyone calls `Math.Round` without the explicit enum per Murat's guardrail
  - [ ] **No in-memory EF Core provider** — integration tests always use Testcontainers Postgres (bitemporal interceptor behavior differs on in-memory)
- [ ] **T14 (AC: 1-7, 10)** — Integration tests (tagged `[Trait("Category", "Integration")]`)
  - [ ] `CreateContactEndpointTests` — full slice via `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine bus
  - [ ] Happy path: POST returns 201 with correct payload; contact row + journal entry row + history rows present in correct state
  - [ ] Concurrent-double-submit test — two parallel POSTs with same `Idempotency-Key` → one handler execution + both return same response (per AC2)
  - [ ] Test-only `ContactCreatedHandler` asserts event shape (not just "bus got the message" — assert end-to-end consumption per Murat's landmine)
  - [ ] `AsOf(t)` time-travel assertion on bitemporal history
  - [ ] Negative path: duplicate name → 409 RFC 9457 with correct code + JSON Pointer
  - [ ] Negative path: invalid currency → 400 RFC 9457 with correct code
  - [ ] Timing assertion: p95 < 500ms
- [ ] **T15 (AC: 3)** — OpenAPI snapshot test via Verify: PR that changes the endpoint's JSON shape unexpectedly fails CI
- [ ] **T16 (AC: 6)** — Property-based test: FsCheck round-trip for `Money` across all currencies in `CurrencyRegistry`
- [ ] **T17 (AC: 4, 7)** — Architecture tests update
  - [ ] NetArchTest #3 (cross-slice via IntegrationEvents) now validates against real slice (previously validated against stub from Story 0.8)
  - [ ] NetArchTest: `ProcessingActivity` set on every endpoint + message handler (no ambient default permitted)

### Tests — Frontend

- [ ] **T18 (AC: 8)** — Vitest + Angular Testing Library unit/component tests in `apps/owner-workspace/src/app/features/contacts/create/create-contact.component.spec.ts`
  - [ ] Form submits with `Idempotency-Key` header
  - [ ] RFC 9457 server error with JSON Pointers renders inline under correct form fields
  - [ ] Transloco key lookup works for both AR and EN locales
  - [ ] `DenominationPicker` consumed unchanged from `libs/domain-ui`
- [ ] **T19 (AC: 8)** — Playwright E2E in `apps/owner-workspace/e2e/contacts/create-contact.spec.ts`
  - [ ] Happy path: fill form, submit, see new contact in list (Chromium + WebKit per architecture §5.4.2)
  - [ ] Axe-core a11y assertion — zero WCAG 2.1 AA violations
  - [ ] RTL parity: repeat same flow in Arabic locale; visual regression screenshot diff against baseline

## Dev Notes

### Critical guardrails (do not violate)

1. **Vertical-slice boundary rigor.** Do NOT reference types from `Slices/Debts/*` or any other slice's `Domain/` folder. The only cross-slice allowed contract is `Faktuboh.Domain.Events.IntegrationEvents/*`. Fitness test #3 fails the build on violation.
2. **No `DateTime` in DTOs or entities.** Use `DateTimeOffset` (instant) or `DateOnly` (date) per §5.2.4. Postgres columns: `_at` suffix → `timestamptz`; `_date` suffix → `date`. NetArchTest asserts this.
3. **No `[JsonPropertyName]` attributes.** Global `camelCase` policy via `ConfigureJsonGlobally` extension. Exception only for RFC-mandated fields (`type`, `title`, etc.) which are already allowlisted.
4. **No `[Table]` or `[Column]` attributes** — `EFCore.NamingConventions` handles `snake_case`. Architecture test fails build on attribute use.
5. **Money storage precision** — `journal_entries.amount` is `numeric(19, 4)`. FX-rate columns (not in this slice) use `numeric(28, 8)`. Schema-audit CI gate asserts.
6. **`Idempotency-Key` UUID v4 generated per form render.** Refresh on form success/reset. Never reuse across forms.
7. **Silent success UX (UX-DR27).** On contact creation success, the drawer/dialog closes silently; the contacts list updates in place; NO success toast. Only errors surface as toasts. The row appearing IS the feedback.
8. **Integration tests use real Postgres via Testcontainers.** The EF Core in-memory provider lies about bitemporal behavior. Wahid's standing rule + Murat's confirmation: never mock the database.
9. **Banker's rounding** (`MidpointRounding.ToEven`) explicitly. Currency rounding drift is the stereotypical fintech silent failure — pin the enum in every `Math.Round` call, enforced by Roslyn analyzer if available.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/
│   │       ├── Contacts/
│   │       │   ├── Contacts.Endpoints.cs                    # NEW
│   │       │   ├── ContactsExceptionHandler.cs              # NEW
│   │       │   └── Create/
│   │       │       ├── CreateContactRequest.cs              # NEW
│   │       │       ├── CreateContactResponse.cs             # NEW
│   │       │       ├── CreateContactRequestValidator.cs     # NEW
│   │       │       ├── CreateContactHandler.cs              # NEW
│   │       │       └── CreateContactEvents.cs               # NEW (re-exports IntegrationEvents)
│   │       └── Debts/
│   │           └── Projections/
│   │               └── ContactCreatedHandler.cs             # NEW (no-op stub)
│   ├── Faktuboh.Domain/
│   │   ├── Contacts/
│   │   │   ├── Contact.cs                                   # NEW (aggregate root, IBitemporal)
│   │   │   ├── IContactRepository.cs                        # NEW
│   │   │   └── ContactException.cs                          # NEW (base exception class)
│   │   ├── JournalEntries/
│   │   │   ├── JournalEntry.cs                              # NEW (aggregate root, IBitemporal)
│   │   │   ├── Direction.cs                                 # NEW (enum: Receivable | Payable)
│   │   │   └── IJournalEntryRepository.cs                   # NEW
│   │   ├── Primitives/
│   │   │   ├── Money.cs                                     # VERIFY/CREATE (Shared Kernel VO)
│   │   │   ├── CurrencyRegistry.cs                          # VERIFY/CREATE
│   │   │   └── IBitemporal.cs                               # VERIFIED (Epic 0 Story 0.6)
│   │   └── Events/
│   │       └── IntegrationEvents/
│   │           ├── ContactCreatedEvent.cs                   # NEW
│   │           └── JournalEntryCreatedEvent.cs              # NEW
│   ├── Faktuboh.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   │   ├── ContactConfiguration.cs                  # NEW
│   │   │   │   └── JournalEntryConfiguration.cs             # NEW
│   │   │   └── Migrations/
│   │   │       └── YYYYMMDDHHMMSS_AddContactsAndJournalEntries.cs  # NEW
│   │   └── Repositories/
│   │       ├── ContactRepository.cs                         # NEW
│   │       └── JournalEntryRepository.cs                    # NEW
│   └── Faktuboh.Application/
│       └── Errors/
│           └── ErrorCatalog.cs                              # MODIFY (add 3 new codes from T6)
└── tests/
    ├── Faktuboh.Api.Tests/
    │   └── Slices/
    │       └── Contacts/
    │           └── Create/
    │               ├── CreateContactHandlerTests.cs         # NEW
    │               ├── CreateContactRequestValidatorTests.cs # NEW
    │               └── CreateContactEndpointTests.cs        # NEW (Integration)
    └── Faktuboh.ArchitectureTests/
        └── ContactsSliceTests.cs                            # NEW (fitness test #3 against real slice)

frontend/
├── apps/owner-workspace/
│   ├── src/app/features/contacts/
│   │   ├── create/
│   │   │   ├── create-contact.component.ts                  # NEW
│   │   │   ├── create-contact.component.html                # NEW
│   │   │   ├── create-contact.component.scss                # NEW (logical props only)
│   │   │   └── create-contact.component.spec.ts             # NEW
│   │   └── contacts.routes.ts                               # NEW (lazy route)
│   └── e2e/contacts/
│       └── create-contact.spec.ts                           # NEW
└── libs/core/api/
    └── contacts.client.ts                                   # NEW (generated or thin wrapper)
```

### Testing standards (per architecture §5.3.3)

| Level | Coverage | Tools |
|---|---|---|
| **Unit** | 70% of tests, 15% of confidence | xUnit v3 + FluentAssertions + NSubstitute + FsCheck (property-based for Money) |
| **Integration** | 25% of tests, 55% of confidence | `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine bus — tagged `[Trait("Category", "Integration")]` |
| **API-contract / Snapshot** | 5% of tests, 30% of confidence | Verify (per-endpoint JSON snapshot + OpenAPI snapshot) |
| **E2E** | 1 flow (happy path + a11y) | Playwright Chromium + WebKit; axe-core + visual-regression screenshots LTR + RTL |

**No browser E2E on this slice beyond the one happy-path flow.** Save Playwright investment for Epic 4 (public-statement surface). Murat's guidance from party-mode: API tests are first-class citizens, not just UI support.

**Forbidden:**
- Moq (commercial-license drama — use NSubstitute)
- EF Core in-memory provider (bitemporal interceptor behavior differs)
- Test isolation via shared `IDisposable` fixtures across test classes
- `[JsonPropertyName]` attributes outside RFC-mandated allowlist
- Mocking the database (Wahid's standing rule)

### Project Structure Notes

This slice lands in the vertical-slice organization defined by architecture §5.3.2 (one-folder-per-action inside a per-slice root). The Contacts slice root is `Slices/Contacts/`; the Create action lives in `Slices/Contacts/Create/`. Subsequent Contacts stories (2.2 Edit, 2.3 Delete, 2.4 List, 2.5 Search) add sibling folders (`Edit/`, `Delete/`, `List/`, `Search/`).

**Detected variances / things to reconcile before first commit:**

1. **TS-contracts generator not yet decided** (ADR-005 deferred to Step 5/6). Until the generator lands, the frontend `contacts.client.ts` is a handwritten thin wrapper over `HttpClient` with types mirrored from the C# DTO names. Mark the file with a leading comment: `// TODO: replace with generated client when ADR-005 resolves`. This is acceptable per ADR-005's "seams reserved" approach — the ESLint rule `no-handwritten-shared-contracts` forbids shared contracts in `contracts/` but permits app-local typed wrappers.
2. **OpenAPI-drift gate baseline** formally lands in Story 3.11 (Epic 0 deferral). For this story, commit the `openapi.json` snapshot to establish the baseline; Story 3.11 activates the drift check against it.
3. **NetArchTest fitness tests #2 (slice isolation) and #4 (no `IAggregateRoot` in Shared Kernel)** layer in during Epic 3 per Story 3.10. For this story, only #1 (Shared Kernel purity) and #3 (IntegrationEvents-only cross-slice) are active — they are sufficient to validate this slice's compound invariants.

### Architecture compliance — ADRs and sections this slice binds

- **ADR-001** (two build targets) — this slice only touches `apps/owner-workspace`, not `apps/public-statement`. Compile-boundary CI gate asserts.
- **ADR-002** (greenfield backend) — no DiTracker ports; every convention re-derived.
- **ADR-004** (compliance posture day-one) — `IBitemporal`, `ProcessingActivity`, per-data-class erasure scaffolding all active.
- **ADR-006** (vertical-slice architecture) — this slice is the first real validator of ADR-006's "slice is the unit" property.
- **ADR-021** (Auth0) — the endpoint `.RequireAuthorization()` validates against Auth0-issued tokens; no additional auth code needed in this slice.
- **ADR-022** (application-level bitemporal) — `IBitemporal` interceptor writes `_history` rows on UPDATE/DELETE.
- **ADR-024** (Minimal APIs + FluentValidation + source-gen OpenAPI + .NET 10 `AddProblemDetails()` + `IExceptionHandler`) — all wire-up conventions follow this ADR.
- **ADR-025** (Key Vault Standard) — no direct interaction in this slice (no PII encryption yet — PII encryption layers in via AR-018 during Epic 3 when debt notes land).

- **§4 D4.9** (RFC 9457 + domain code taxonomy) — all error responses conform.
- **§4 D4.11** (Idempotency-Key + natural-key backstop) — `Idempotency-Key` is the primary guard; natural-key constraint on `(owner_subject_id, name_lower)` is the DB-level belt-and-suspenders.
- **§5.2.1** (DB naming `snake_case`), **§5.2.3** (JSON `camelCase`), **§5.2.4** (dates as `DateTimeOffset`/`timestamptz`), **§5.2.5** (Money `numeric(19,4)` + decimal-string wire format), **§5.2.6** (error code taxonomy `faktuboh.contacts.<condition>`).
- **§5.3.1** (DTO/endpoint naming — `CreateContactRequest`/`CreateContactResponse`/`CreateContactRequestValidator`/`CreateContactHandler`), **§5.3.2** (slice-internal organization).
- **§7.5.2** (first-slice definition — this story IS that slice).

### Library / framework requirements (pinned versions from architecture §3.2)

| Layer | Package | Version | Notes |
|---|---|---|---|
| Runtime — backend | .NET | 10 (LTS) | Default target across all projects |
| Language — backend | C# | 14 | Bundled with .NET 10 SDK |
| Host orchestration | .NET Aspire | 13.x | AppHost + ServiceDefaults pattern |
| HTTP framework | ASP.NET Core Minimal APIs | GA (first-party on .NET 10) | Per ADR-024 |
| Validation | FluentValidation | latest stable | `AbstractValidator<T>` + endpoint filter |
| Messaging / outbox | Wolverine | latest stable | Postgres transport per D4.12; same-transaction outbox |
| ORM | EF Core | 10 | `SaveChanges` interceptor for bitemporal per ADR-022 |
| DB naming | `EFCore.NamingConventions` | latest stable | `UseSnakeCaseNamingConvention()` |
| Mapping | Mapperly | latest stable | Zero-reflection on hot path (not needed for this slice — use static factory `Response.From(entity)`) |
| Logging | Serilog + OpenTelemetry | latest stable | Stamped with `ProcessingActivity` |
| Architectural tests | NetArchTest | latest stable | Fitness tests #1 + #3 active |
| Test runner | xUnit v3 | latest stable | |
| Assertions | FluentAssertions | latest stable | Counts as one logical assertion |
| Mocking | NSubstitute | latest stable | (Moq forbidden) |
| Integration-test DB | Testcontainers (Postgres) | latest stable | |
| Property-based tests | FsCheck | latest stable | For Money round-trip |
| Snapshot tests | Verify | latest stable | For OpenAPI + per-endpoint JSON |
| Runtime — frontend | Node.js | 22 LTS | |
| Framework — frontend | Angular | 21 | Zoneless default; Signal Forms |
| UI kit | PrimeNG | 21 | Aura preset + `tailwindcss-primeui` |
| Styling | Tailwind CSS | 4.x | Layout-only; PrimeNG theme for component-level visuals |
| i18n | Transloco | latest stable | Runtime bilingual AR/EN + RTL |
| Test runner (FE) | Vitest | latest stable | |
| E2E | Playwright | latest stable | Chromium + WebKit |

### File structure requirements

**Backend naming conventions (per §5.3.1):**
- Request DTO: `{Verb}{Noun}Request` (sealed record) — **`CreateContactRequest`**
- Response DTO: `{Verb}{Noun}Response` (sealed record) — **`CreateContactResponse`**
- Validator: `{Verb}{Noun}RequestValidator` — **`CreateContactRequestValidator`**
- Mapper: static method `{Verb}{Noun}Response.From(entity)`
- Handler: `{Verb}{Noun}Handler` with method **`public async Task<TResponse> Handle(...)`** (Wolverine discovery requires this exact method name)
- Route group: `Contacts.Endpoints.cs` with `MapGroup("/v1/contacts").WithTags("Contacts").RequireAuthorization()`
- Domain event: `{Noun}{PastTenseVerb}Event` (sealed record) — **`ContactCreatedEvent`** (past tense always)

**Frontend naming conventions (per §5.4.1):**
- Component: `create-contact.component.ts` → `CreateContactComponent` (keep `.component.ts` suffix)
- Selector prefix: `ow-` (owner-workspace) — `<ow-create-contact>`
- Signal naming: plain camelCase, no `$` or `Signal` suffix
- Form `pending` signal (not `loading`, `submitting`, `isLoading`, `isPending` — enforced by ESLint rule)

### Testing requirements (per architecture §5.3.3 + party-mode Murat commitments)

**Test pyramid for this slice:**
- **Unit (70% of tests):** Money VO arithmetic edges, bitemporal interceptor in isolation (if added here), RFC 9457 serialization, idempotency-key hash collision behavior
- **Integration (25% of tests, 55% of confidence):** Real Postgres + real Wolverine bus + full compound behavior — happy path + 6 negative paths + 2 retry paths + 1 time-travel assertion
- **API-contract snapshot (5%):** Verify-based OpenAPI + per-endpoint JSON snapshots

**Murat's false-confidence landmines to avoid:**
1. EF Core in-memory provider — hard ban
2. Idempotency tests that don't exercise concurrency — MUST include concurrent-double-submit integration test
3. IntegrationEvent tests that assert publication but not consumption — write test-only `ContactCreatedHandler` that asserts shape
4. Money VO rounding tests that use default `MidpointRounding` — pin banker's rounding explicitly
5. Bitemporal assertions that only check current row — assert history row's `ValidTo` is correctly closed

### Previous Story Intelligence

**None applicable.** This is the first implementation story being expanded. No `2-1-*.md` or earlier story files exist in `implementation-artifacts/`. Epic 0 stories (0.1 through 0.17) are scaffold stories that should logically ship first, but they have not yet been expanded to individual context files — their content is in `epics.md` only.

**Assumed scaffold state at start of this story's implementation:**

This story assumes Epic 0's hard gates are already in CI-green state. If the dev agent starts this story against an un-scaffolded repo, stop and run Epic 0's stories first. Hard-gate pre-conditions:
- Solution + Angular workspace scaffolded per Stories 0.1 + 0.2
- Auth0 tenant live per Story 0.3
- RFC 9457 middleware + ErrorCatalog active per Story 0.4
- Idempotency-Key middleware active per Story 0.5
- `IBitemporal` interceptor + MigrationService active per Story 0.6
- `ProcessingActivity` ambient context enforced per Story 0.7
- NetArchTest fitness tests #1 + #3 active per Story 0.8
- CI/CD workflows + OIDC per Story 0.9
- Azure infra per Story 0.10
- App Insights + traceId correlation per Story 0.11
- AR-047 telemetry primitive per Story 0.14
- AR-049 `ICompoundJournalEntry` interface seam per Story 0.15 (not consumed in this slice; first consumed by Epic 5 Story 5.2)

### Git Intelligence

```
e69555f add epics file
469e334 Phase 3 solutioning: architecture reconciled + PRD validated (5/5)
9c1aa04 almost finished bmad architecture
ac57de9 add backend architecture documentation
f3dd0f4 prd with design document
592a398 Initial commit
```

**No implementation commits yet.** Prior commits are planning artifacts only. This story will produce the first code commits against the repo.

**Recommended commit discipline:** squash-merge the full Epic 0 scaffold as one commit, then land this story's backend slice + tests as a second commit, then the frontend slice + tests as a third commit. Small commits enable clean bisection if a downstream fitness test regression appears.

### Latest Technical Information

**.NET 10 Minimal APIs specifics:**
- `TypedResults` replaces the older `Results` class for strong-typing — always use `TypedResults.Created<T>(...)`, `TypedResults.Problem(...)`, etc.
- `AddProblemDetails()` + `IExceptionHandler` are the canonical replacement for Hellang.Middleware.ProblemDetails (which predates .NET 8). Explicit in ADR-024: no Hellang dependency.
- `Results<T1, T2>` discriminated union for endpoints that return multiple success/error types: `Results<Created<CreateContactResponse>, ProblemHttpResult>`
- Endpoint filters run in a pipeline per `MapGroup` — use `.AddEndpointFilter<ValidationFilter<T>>()` for FluentValidation integration.

**Wolverine same-transaction outbox (EF Core):**
- Register via `services.AddDbContext<...>()` + `.IntegrateWithWolverine()` — Wolverine auto-detects the DbContext and wraps outbox publications in the same `SaveChangesAsync` transaction.
- Integration events must be `public sealed record` types in the `IntegrationEvents/` namespace.
- Wolverine's `IMessageBus` is the inject point; call `bus.PublishAsync(evt)` — the message enqueues in the outbox, commits atomically, and delivers asynchronously.

**EF Core 10 + Npgsql:**
- `UseSnakeCaseNamingConvention()` comes from the `EFCore.NamingConventions` NuGet package (not first-party).
- `timestamptz` is the default for `DateTimeOffset` in Npgsql.
- `numeric(19,4)` is the correct PostgreSQL type for the Money amount column (use HasColumnType("numeric(19,4)") on the EF configuration).
- `check (currency ~ '^[A-Z]{3}$')` is a Postgres regex check constraint; add via `HasCheckConstraint("ck_currency_iso_4217", "currency ~ '^[A-Z]{3}$'")` in the EF configuration.

**Angular 21 Signal Forms + PrimeNG v21:**
- `signal()`, `computed()`, `input()`, `model()`, `output()` replace legacy `@Input()`/`@Output()`.
- `rxResource()` + `resource()` are the built-in async-state primitives — no NgRx needed at this slice (escape hatch only if cross-slice aggregation demands it per AR-022).
- PrimeNG 21's `<p-iftaLabel>` + `<p-floatLabel variant="on">` provide the label strategy per UX-DR28.
- `@primeng/mcp` MCP server is configured (per architecture §1.1) — use it to query exact component APIs during implementation rather than pattern-matching from training data.

### Project Context Reference

**No `project-context.md` found in the repository.** All project context flows from the planning artifacts (PRD + Architecture + UX Spec + ADRs + Epics).

## References

All technical details cite source paths and sections:

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 2.1
- First-slice rationale — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §7.5.2
- Vertical-slice architecture — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.3, §5.3.1, §5.3.2, ADR-006
- RFC 9457 + ErrorCatalog — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4 D4.9, §5.2.6, §5.3.1.1
- Idempotency-Key middleware — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4 D4.11
- Bitemporal interceptor pattern — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5, §4 D4.1, [_bmad-output/planning-artifacts/adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- Minimal APIs + FluentValidation — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.2, §5.3.1, [_bmad-output/planning-artifacts/adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md)
- Money dual-precision rule — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- DB naming (snake_case) — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.1
- JSON wire format (camelCase) — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.3
- Date/time conventions — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.4
- ProcessingActivity ambient context — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5, ADR-004
- Test conventions — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.3.3
- Frontend conventions — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.4.1, §5.4.2, §5.4.3
- Auth0 integration — [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- FR8 (add contact) + FR13 (record debt) — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Contact Management + Debt Management sections
- NFR-P4 (500ms debt-recording) + NFR-SC1 (500 concurrent users) — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Performance + Scalability
- UX component APIs (DenominationPicker, ContactAvatar, amount display) — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component Strategy
- UX-DR28 form patterns, UX-DR27 silent success, UX-DR31 loading states, UX-DR32 error recovery — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) UX Consistency Patterns

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created.
- This slice is explicitly the §7.5.2 first slice per architecture — exercises Money VO + idempotency + bitemporal + RFC 9457 + integration event end-to-end.
- Expected outcome: Contacts domain fully scaffolded + JournalEntries domain initiated (continues into Epic 3) + NetArchTest #3 validated against real cross-slice traffic for the first time.

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
